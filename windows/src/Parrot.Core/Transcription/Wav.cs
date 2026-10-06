// Parrot for Windows. Derived from Parrot (GPL-3.0), WAVEncoder in CloudTranscription.swift.
using System.Text;

namespace Parrot.Core.Transcription;

public static class WavEncoder
{
    public const int SampleRate = 16000;

    /// Minimal 16-bit PCM mono WAV around float samples (-1…1).
    public static byte[] Encode(ReadOnlySpan<float> samples, int sampleRate = SampleRate)
    {
        var dataSize = samples.Length * 2;
        var bytes = new byte[44 + dataSize];
        WriteHeader(bytes.AsSpan(0, 44), sampleRate, dataSize);
        var span = bytes.AsSpan(44);
        for (var i = 0; i < samples.Length; i++)
        {
            var v = ToPcm16(samples[i]);
            span[i * 2] = (byte)(v & 0xFF);
            span[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return bytes;
    }

    public static short ToPcm16(float s) => (short)(Math.Clamp(s, -1f, 1f) * 32767);

    internal static void WriteHeader(Span<byte> h, int sampleRate, int dataSize)
    {
        Encoding.ASCII.GetBytes("RIFF").CopyTo(h);
        BitConverter.TryWriteBytes(h[4..], (uint)(36 + dataSize));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(h[8..]);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(h[12..]);
        BitConverter.TryWriteBytes(h[16..], 16u);                       // PCM chunk size
        BitConverter.TryWriteBytes(h[20..], (ushort)1);                 // PCM format
        BitConverter.TryWriteBytes(h[22..], (ushort)1);                 // mono
        BitConverter.TryWriteBytes(h[24..], (uint)sampleRate);
        BitConverter.TryWriteBytes(h[28..], (uint)(sampleRate * 2));    // byte rate
        BitConverter.TryWriteBytes(h[32..], (ushort)2);                 // block align
        BitConverter.TryWriteBytes(h[34..], (ushort)16);                // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(h[36..]);
        BitConverter.TryWriteBytes(h[40..], (uint)dataSize);
    }

    /// Reads a 16-bit PCM mono WAV written by Parrot back into floats.
    public static float[] ReadPcm16Mono(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF") throw new InvalidDataException("Not a WAV file");
        // Walk chunks to find "data" (tolerates extra chunks).
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "data")
            {
                var start = pos + 8;
                var count = Math.Min(size, bytes.Length - start) / 2;
                var samples = new float[count];
                for (var i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(bytes, start + i * 2) / 32768f;
                return samples;
            }
            pos += 8 + size;
        }
        throw new InvalidDataException("WAV has no data chunk");
    }
}

/// Streams 16 kHz mono 16-bit PCM to disk; the header is patched on Dispose,
/// and also every few seconds so a crash still leaves a playable file.
public sealed class WavFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _sampleRate;
    private long _dataBytes;
    private long _lastPatch;
    private readonly object _lock = new();
    public string Path { get; }
    public long SamplesWritten => _dataBytes / 2;

    public WavFileWriter(string path, int sampleRate = WavEncoder.SampleRate)
    {
        Path = path;
        _sampleRate = sampleRate;
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _stream.Write(new byte[44]);
        PatchHeader();
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        var buffer = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = WavEncoder.ToPcm16(samples[i]);
            buffer[i * 2] = (byte)(v & 0xFF);
            buffer[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        lock (_lock)
        {
            _stream.Write(buffer);
            _dataBytes += buffer.Length;
            if (_dataBytes - _lastPatch > _sampleRate * 2 * 5) PatchHeader();
        }
    }

    private void PatchHeader()
    {
        var header = new byte[44];
        WavEncoder.WriteHeader(header, _sampleRate, (int)Math.Min(_dataBytes, int.MaxValue - 36));
        var pos = _stream.Position;
        _stream.Position = 0;
        _stream.Write(header);
        _stream.Position = pos;
        _stream.Flush();
        _lastPatch = _dataBytes;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            PatchHeader();
            _stream.Dispose();
        }
    }
}
