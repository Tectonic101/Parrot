// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/AudioCaptureManager.swift
// and SystemAudioTap.swift (the macOS process tap is replaced by WASAPI loopback).
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Parrot.Core.Models;
using Parrot.Core.Recording;

namespace Parrot.Audio;

/// One WASAPI capture converted to 16 kHz mono float.
///  - "Them": loopback of a render device (everything the PC plays — the other side of the call).
///  - "Me": a capture device (microphone).
public sealed class WasapiTrackSource : IAudioTrackSource
{
    private readonly MMDevice _device;
    private readonly bool _loopback;
    private WasapiCapture? _capture;
    private WasapiOut? _silence;
    private StreamingResampler? _resampler;
    private bool _isFloat;
    private int _bits, _channels;
    private readonly Stopwatch _clock = new();
    private long _emitted;
    private Timer? _gapTimer;
    private readonly object _lock = new();
    private bool _failed;
    private volatile bool _stopping;

    public Speaker Speaker { get; }
    public string DeviceName { get; }
    public event Action<float[]>? SamplesAvailable;
    public event Action<string>? Failed;

    private WasapiTrackSource(MMDevice device, Speaker speaker, bool loopback)
    {
        _device = device;
        Speaker = speaker;
        _loopback = loopback;
        DeviceName = device.FriendlyName;
    }

    /// System audio ("Them") from the given render device, or the default output.
    public static WasapiTrackSource ForSystemAudio(string? renderDeviceId = null)
    {
        var device = AudioDevices.Find(renderDeviceId, DataFlow.Render);
        return new WasapiTrackSource(device, Speaker.Them, loopback: true);
    }

    /// Microphone ("Me") from the given capture device, or the default communications mic.
    public static WasapiTrackSource ForMicrophone(string? captureDeviceId = null)
    {
        var device = AudioDevices.Find(captureDeviceId, DataFlow.Capture);
        return new WasapiTrackSource(device, Speaker.Me, loopback: false);
    }

    public void Start()
    {
        _capture = _loopback ? new WasapiLoopbackCapture(_device) : new WasapiCapture(_device, true, 100);
        var format = _capture.WaveFormat;
        _channels = format.Channels;
        _bits = format.BitsPerSample;
        _isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
                   || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        _resampler = new StreamingResampler(format.SampleRate);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null && !_stopping) Fail(e.Exception.Message);
        };

        if (_loopback)
        {
            // Loopback delivers nothing while nothing plays. Playing silence keeps the
            // stream (and our timeline) running; the gap timer is the backstop.
            try
            {
                _silence = new WasapiOut(_device, AudioClientShareMode.Shared, true, 200);
                _silence.Init(new SilenceProvider(new WaveFormat(format.SampleRate, 16, 2)));
                _silence.Play();
            }
            catch (Exception)
            {
                _silence = null; // the gap filler alone keeps time
            }
        }

        _clock.Restart();
        _emitted = 0;
        _capture.StartRecording();
        _gapTimer = new Timer(_ => FillGaps(), null, 500, 250);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _resampler == null) return;
        try
        {
            var mono = PcmConversion.ToMonoFloat(e.Buffer.AsSpan(0, e.BytesRecorded), _isFloat, _bits, _channels);
            float[] out16;
            lock (_lock)
            {
                out16 = _resampler.Process(mono);
                _emitted += out16.Length;
            }
            if (out16.Length > 0) SamplesAvailable?.Invoke(out16);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void FillGaps()
    {
        if (_stopping) return;
        int pad;
        lock (_lock)
        {
            pad = GapFiller.SamplesToPad(_clock.Elapsed.TotalSeconds, _emitted);
            _emitted += pad;
        }
        if (pad > 0) SamplesAvailable?.Invoke(new float[pad]);
    }

    private void Fail(string message)
    {
        if (_failed) return;
        _failed = true;
        Failed?.Invoke(message);
    }

    public void Stop()
    {
        _stopping = true;
        _gapTimer?.Dispose();
        _gapTimer = null;
        try { _capture?.StopRecording(); } catch (Exception) { }
        try { _silence?.Stop(); } catch (Exception) { }
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _silence?.Dispose();
        _capture = null;
        _silence = null;
    }
}
