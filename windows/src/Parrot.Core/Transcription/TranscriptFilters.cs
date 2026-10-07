// Parrot for Windows. Derived from Parrot (GPL-3.0), TranscriptionEngine.swift.
using System.Text.RegularExpressions;

namespace Parrot.Core.Transcription;

public static class TranscriptFilters
{
    /// Classic Whisper silence hallucinations, matched only for low-energy chunks.
    public static readonly HashSet<string> HallucinationPhrases = new()
    {
        "you", "okay", "ok", "thank you", "thanks", "bye", "bye-bye",
        "thank you for watching", "thanks for watching", "hmm", "mm-hmm",
        "uh", "um", "the end", "subtitles by", "1", "2",
    };

    /// True when a decoded chunk is almost certainly invented: punctuation-only text, or a
    /// known silence-hallucination phrase from a chunk without confident speech energy.
    public static bool IsLikelyHallucination(string text, float energy)
    {
        var normalized = text.ToLowerInvariant().Trim().Trim('.', ',', '!', '?', '…', '-', '—').Trim();
        if (normalized.Length == 0) return true;
        if (IsBracketedNonSpeech(text)) return true;
        if (energy >= 0.006f) return false;
        return HallucinationPhrases.Contains(normalized);
    }

    /// whisper.cpp emits "[BLANK_AUDIO]", "(music)", "[Music]" etc. for non-speech.
    public static bool IsBracketedNonSpeech(string text)
    {
        var t = text.Trim();
        return Regex.IsMatch(t, @"^(\[[^\]]*\]|\([^)]*\)|\*[^*]*\*)$");
    }

    /// Strips Whisper special/timestamp tokens ("<|0.00|>") and trims.
    public static string Cleaned(string text) =>
        Regex.Replace(text, @"<\|[^|>]*\|>", "").Replace("[BLANK_AUDIO]", "").Trim();

    /// Scales a quiet chunk to a healthy loudness before decode (gain capped at 32, never attenuates).
    public static float[] NormalizedForDecode(float[] samples)
    {
        const float targetRms = 0.06f, maxGain = 32f;
        if (samples.Length == 0) return samples;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        var rms = (float)Math.Sqrt(sum / samples.Length);
        if (rms <= 0 || rms >= targetRms) return samples;
        var gain = Math.Min(targetRms / rms, maxGain);
        var out_ = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++) out_[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
        return out_;
    }

    public static float MeanAbs(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        float sum = 0;
        foreach (var s in samples) sum += Math.Abs(s);
        return sum / samples.Length;
    }

    /// "Glossary: a, b." from the Settings vocabulary, or null when empty.
    public static string? GlossaryPrompt(string vocabulary)
    {
        var terms = vocabulary.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0 ? null : "Glossary: " + string.Join(", ", terms) + ".";
    }
}
