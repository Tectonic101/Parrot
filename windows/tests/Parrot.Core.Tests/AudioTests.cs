// Parrot for Windows tests. Derived from Parrot (GPL-3.0), TranscriptionEngine.swift segmentation/filter checks.
using Parrot.Core.Recording;
using Parrot.Core.Transcription;

namespace Parrot.Core.Tests;

public class AudioTests
{
    private static float[] Tone(int rate, double hz, double seconds, float amp = 0.5f) =>
        Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    private static float Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (var x in s) sum += x * x;
        return (float)Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    [Fact]
    public void Resampler_48kTo16kKeepsSpeechBandAndRemovesAliases()
    {
        var r = new StreamingResampler(48000);
        var speech = r.Process(Tone(48000, 440, 1.0));
        Assert.InRange(speech.Length, 15990, 16010);
        Assert.InRange(Rms(speech.AsSpan(1000)), 0.33f, 0.38f); // 0.5 / sqrt(2) ≈ 0.354

        var r2 = new StreamingResampler(48000);
        var alias = r2.Process(Tone(48000, 12000, 1.0)); // above the 8 kHz Nyquist of the output
        Assert.True(Rms(alias.AsSpan(1000)) < 0.01f);
    }

    [Fact]
    public void Resampler_BlocksMatchOneShot()
    {
        var input = Tone(44100, 300, 0.5);
        var whole = new StreamingResampler(44100).Process(input);
        var streamed = new StreamingResampler(44100);
        var parts = new List<float>();
        for (var i = 0; i < input.Length; i += 441) parts.AddRange(streamed.Process(input.AsSpan(i, Math.Min(441, input.Length - i))));
        Assert.Equal(whole.Length, parts.Count);
        for (var i = 0; i < whole.Length; i++) Assert.Equal(whole[i], parts[i], 4);
    }

    [Fact]
    public void Resampler_PassThroughAt16k()
    {
        var input = Tone(16000, 200, 0.1);
        Assert.Equal(input, new StreamingResampler(16000).Process(input));
    }

    [Fact]
    public void PcmConversion_StereoFormats()
    {
        // Two stereo frames of 16-bit: (16384, -16384) and (32767, 32767).
        var pcm16 = new byte[] { 0x00, 0x40, 0x00, 0xC0, 0xFF, 0x7F, 0xFF, 0x7F };
        var mono = PcmConversion.ToMonoFloat(pcm16, false, 16, 2);
        Assert.Equal(2, mono.Length);
        Assert.Equal(0f, mono[0], 4);
        Assert.Equal(32767 / 32768f, mono[1], 4);

        var f32 = new byte[16];
        BitConverter.TryWriteBytes(f32.AsSpan(0), 0.25f);
        BitConverter.TryWriteBytes(f32.AsSpan(4), 0.75f);
        BitConverter.TryWriteBytes(f32.AsSpan(8), -1f);
        BitConverter.TryWriteBytes(f32.AsSpan(12), 1f);
        Assert.Equal(new[] { 0.5f, 0f }, PcmConversion.ToMonoFloat(f32, true, 32, 2));

        // 24-bit mono: -0.5 (0xC00000).
        var p24 = new byte[] { 0x00, 0x00, 0xC0 };
        Assert.Equal(-0.5f, PcmConversion.ToMonoFloat(p24, false, 24, 1)[0], 4);
    }

    [Fact]
    public void GapFiller_PadsOnlyRealGaps()
    {
        Assert.Equal(0, GapFiller.SamplesToPad(10, 160000 - 8000));          // 0.5 s behind: tolerated
        Assert.Equal(160000 - 1600, GapFiller.SamplesToPad(10, 0));           // 10 s of silence
        Assert.Equal(0, GapFiller.SamplesToPad(1, 32000));                    // ahead of the clock
    }

    [Fact]
    public void Wav_EncodeAndReadBack()
    {
        var samples = Tone(16000, 1000, 0.25, 0.8f);
        var bytes = WavEncoder.Encode(samples);
        Assert.Equal(44 + samples.Length * 2, bytes.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(16000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(samples.Length * 2, BitConverter.ToInt32(bytes, 40));

        using var paths = new TempPaths();
        var path = Path.Combine(paths.Root, "t.wav");
        File.WriteAllBytes(path, bytes);
        var back = WavEncoder.ReadPcm16Mono(path);
        Assert.Equal(samples.Length, back.Length);
        for (var i = 0; i < samples.Length; i += 97) Assert.Equal(samples[i], back[i], 3);
    }

    [Fact]
    public void WavFileWriter_StreamsAndPatchesHeader()
    {
        using var paths = new TempPaths();
        var path = Path.Combine(paths.Root, "s.wav");
        using (var w = new WavFileWriter(path))
        {
            w.Write(Tone(16000, 300, 1));
            w.Write(Tone(16000, 300, 0.5));
            Assert.Equal(24000, w.SamplesWritten);
        }
        Assert.Equal(24000, WavEncoder.ReadPcm16Mono(path).Length);
        Assert.Equal(48000, BitConverter.ToInt32(File.ReadAllBytes(path), 40));
    }

    private static List<float> Speech(double seconds, float amp = 0.1f) => Tone(16000, 220, seconds, amp).ToList();
    private static List<float> Silence(double seconds) => new(new float[(int)(16000 * seconds)]);

    [Fact]
    public void Segmenter_CutsAtPauseWithPad()
    {
        var buffer = Silence(0.5).Concat(Speech(1.0)).Concat(Silence(1.0)).ToList();
        var cut = Segmenter.NextCut(buffer, draining: false);
        Assert.Equal(8000, cut.DropLeading);
        // 1 s of speech + one pad frame.
        Assert.Equal(16000 + Segmenter.Frame, cut.Take);
    }

    [Fact]
    public void Segmenter_WaitsMidSpeechAndCapsLongSpeech()
    {
        var mid = Speech(2.0);
        Assert.Null(Segmenter.NextCut(mid, draining: false).Take);
        Assert.Equal(mid.Count, Segmenter.NextCut(mid, draining: true).Take);

        var longSpeech = Speech(13);
        Assert.Equal(Segmenter.MaxSegmentSamples, Segmenter.NextCut(longSpeech, draining: false).Take);
    }

    [Fact]
    public void Segmenter_DropsBlipsAndSilence()
    {
        var blip = Speech(0.1).Concat(Silence(1.0)).ToList();
        var cut = Segmenter.NextCut(blip, draining: false);
        Assert.Null(cut.Take);
        Assert.True(cut.DropLeading > 1600);

        var silence = Silence(1.05);
        var s = Segmenter.NextCut(silence, draining: false);
        Assert.Equal(10 * Segmenter.Frame, s.DropLeading); // whole frames only
        Assert.Equal(silence.Count, Segmenter.NextCut(silence, draining: true).DropLeading);
    }

    [Fact]
    public void Segmenter_AdaptiveFloorFollowsNoise()
    {
        var quietRoom = Silence(1).Select((_, i) => (i % 2 == 0 ? 1 : -1) * 0.0002f).Concat(Speech(1, 0.01f)).ToList();
        var floor = Segmenter.AdaptiveFloor(quietRoom);
        Assert.InRange(floor, Segmenter.DitherFloor, Segmenter.SilenceFloor);
        Assert.Equal(0.0008f, floor, 5);
    }

    [Theory]
    [InlineData("Thank you.", 0.001f, true)]
    [InlineData("Thank you.", 0.05f, false)]
    [InlineData("[BLANK_AUDIO]", 0.05f, true)]
    [InlineData("(music)", 0.05f, true)]
    [InlineData("...", 0.05f, true)]
    [InlineData("We need SSO before rollout.", 0.001f, false)]
    public void Hallucinations(string text, float energy, bool expected) =>
        Assert.Equal(expected, TranscriptFilters.IsLikelyHallucination(text, energy));

    [Fact]
    public void Filters_CleanAndNormalize()
    {
        Assert.Equal("Hello there", TranscriptFilters.Cleaned("<|0.00|> Hello there<|2.00|>"));
        var quiet = Tone(16000, 200, 0.5, 0.01f);
        var loud = TranscriptFilters.NormalizedForDecode(quiet);
        Assert.InRange(Rms(loud), 0.055f, 0.065f);
        var already = Tone(16000, 200, 0.5, 0.5f);
        Assert.Same(already, TranscriptFilters.NormalizedForDecode(already));
        Assert.Equal("Glossary: Parrot, Acme Corp.", TranscriptFilters.GlossaryPrompt(" Parrot ,\nAcme Corp"));
        Assert.Null(TranscriptFilters.GlossaryPrompt(" "));
    }
}
