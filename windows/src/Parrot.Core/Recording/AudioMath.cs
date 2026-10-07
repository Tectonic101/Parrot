// Parrot for Windows. Derived from Parrot (GPL-3.0).
// Pure-managed audio helpers used by the WASAPI capture (Parrot.Audio); kept here so they are unit-tested.
namespace Parrot.Core.Recording;

public static class PcmConversion
{
    /// Interleaved device samples → mono float (-1…1) by averaging channels.
    /// Supports 32-bit IEEE float and 16/24/32-bit integer PCM.
    public static float[] ToMonoFloat(ReadOnlySpan<byte> data, bool isFloat, int bitsPerSample, int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        var bytesPerSample = bitsPerSample / 8;
        var frameBytes = bytesPerSample * channels;
        var frames = data.Length / frameBytes;
        var output = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = f * frameBytes + c * bytesPerSample;
                sum += ReadSample(data.Slice(o, bytesPerSample), isFloat, bitsPerSample);
            }
            output[f] = sum / channels;
        }
        return output;
    }

    private static float ReadSample(ReadOnlySpan<byte> b, bool isFloat, int bits)
    {
        if (isFloat && bits == 32) return BitConverter.ToSingle(b);
        if (isFloat && bits == 64) return (float)BitConverter.ToDouble(b);
        return bits switch
        {
            16 => BitConverter.ToInt16(b) / 32768f,
            24 => ((b[0] | (b[1] << 8) | (b[2] << 16)) << 8 >> 8) / 8388608f,
            32 => BitConverter.ToInt32(b) / 2147483648f,
            8 => (b[0] - 128) / 128f,
            _ => throw new NotSupportedException($"{bits}-bit audio is not supported"),
        };
    }
}

/// Streaming sample-rate converter to 16 kHz: windowed-sinc low-pass (anti-aliasing,
/// only when downsampling) followed by linear interpolation. Good enough for speech
/// recognition; state carries across blocks so there are no seams.
public sealed class StreamingResampler
{
    private readonly double _step;
    private readonly float[] _taps;
    private readonly float[] _history;
    private double _t;            // absolute filtered-sample position of the next output
    private long _absStart;       // absolute index of the first sample of the current block
    private float _last;          // last filtered sample of the previous block

    public int InputRate { get; }
    public int OutputRate { get; }

    public StreamingResampler(int inputRate, int outputRate = 16000, int tapCount = 63)
    {
        InputRate = inputRate;
        OutputRate = outputRate;
        _step = inputRate / (double)outputRate;
        if (inputRate > outputRate)
        {
            _taps = DesignLowPass(0.45 * outputRate / inputRate, tapCount | 1);
            _history = new float[_taps.Length - 1];
        }
        else
        {
            _taps = new[] { 1f };
            _history = Array.Empty<float>();
        }
    }

    /// Normalized cutoff in cycles/sample; Blackman-windowed sinc, unity DC gain.
    private static float[] DesignLowPass(double cutoff, int n)
    {
        var taps = new double[n];
        var m = (n - 1) / 2.0;
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            var x = i - m;
            var sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            var w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
            taps[i] = sinc * w;
            sum += taps[i];
        }
        return taps.Select(t => (float)(t / sum)).ToArray();
    }

    public float[] Process(ReadOnlySpan<float> input)
    {
        if (input.Length == 0) return Array.Empty<float>();
        if (InputRate == OutputRate) return input.ToArray();

        // FIR filter with carried history.
        var filtered = new float[input.Length];
        var h = _history.Length;
        for (var i = 0; i < input.Length; i++)
        {
            float acc = 0;
            for (var k = 0; k < _taps.Length; k++)
            {
                var idx = i - k;
                var s = idx >= 0 ? input[idx] : _history[h + idx];
                acc += _taps[k] * s;
            }
            filtered[i] = acc;
        }
        if (h > 0)
        {
            if (input.Length >= h) input[^h..].CopyTo(_history);
            else
            {
                Array.Copy(_history, input.Length, _history, 0, h - input.Length);
                input.CopyTo(_history.AsSpan(h - input.Length));
            }
        }

        // Linear interpolation over [last, filtered...]; index 0 = absolute _absStart - 1.
        var output = new List<float>((int)(input.Length / _step) + 2);
        var end = _absStart + input.Length - 1;
        while (_t <= end)
        {
            var rel = _t - (_absStart - 1);
            var i0 = (int)Math.Floor(rel);
            var frac = (float)(rel - i0);
            var a = i0 == 0 ? _last : filtered[i0 - 1];
            var b = i0 + 1 <= input.Length ? filtered[i0] : a;
            output.Add(a + (b - a) * frac);
            _t += _step;
        }
        _last = filtered[^1];
        _absStart += input.Length;
        return output.ToArray();
    }
}

/// Keeps a track on the wall clock. WASAPI loopback delivers no packets while nothing is
/// playing, which would compress the "Them" timeline; this says how much silence to insert.
public static class GapFiller
{
    public static int SamplesToPad(double elapsedSeconds, long emittedSamples, int sampleRate = 16000, double toleranceSeconds = 1.0)
    {
        var expected = (long)(elapsedSeconds * sampleRate);
        var deficit = expected - emittedSamples;
        if (deficit <= toleranceSeconds * sampleRate) return 0;
        // Pad to within 100 ms of the clock: late packets may still be in flight.
        return (int)Math.Max(0, deficit - 0.1 * sampleRate);
    }
}
