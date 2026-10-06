// Parrot for Windows. Derived from Parrot (GPL-3.0), TranscriptionEngine.Segmenter in TranscriptionEngine.swift.
namespace Parrot.Core.Transcription;

/// Pure utterance segmenter for the live loop. Only ever emits speech bounded by a
/// real pause: leading silence is discarded outright (never decoded — the structural
/// fix for Whisper's silence hallucinations), and an utterance is cut when a sustained
/// pause follows it, when it hits the length cap, or when draining at stop.
public static class Segmenter
{
    /// Energy-frame size: 100 ms at 16 kHz.
    public const int Frame = 1600;
    /// Legacy fixed mean-abs floor; the adaptive floor's ceiling.
    public const float SilenceFloor = 0.002f;
    /// Digital dither ceiling: below this can never be speech.
    public const float DitherFloor = 0.0004f;
    /// A flat window longer than this is steady noise, not un-paused quiet speech.
    public const int MaxFlatSpeechFrames = 30;
    /// 600 ms of continuous silence ends an utterance.
    public const int PauseFrames = 6;
    /// Speech islands under 300 ms are clicks/noise: dropped without a decode.
    public const int MinSpeechSamples = 4800;
    /// Forced cut for uninterrupted speech (12 s).
    public const int MaxSegmentSamples = 192_000;
    /// Keep 100 ms of the pause so Whisper hears the word release.
    public const int PadFrames = 1;

    /// Discard DropLeading samples (silence), then cut Take samples for decoding; Take null = keep buffering.
    public readonly record struct Cut(int DropLeading, int? Take);

    public static float FrameEnergy(IReadOnlyList<float> buffer, int i)
    {
        float sum = 0;
        for (var j = i * Frame; j < (i + 1) * Frame; j++) sum += Math.Abs(buffer[j]);
        return sum / Frame;
    }

    /// Adaptive speech/silence threshold from the window's own quietest frame (noise × 4),
    /// clamped to [DitherFloor, SilenceFloor].
    public static float AdaptiveFloor(IReadOnlyList<float> buffer)
    {
        var frames = buffer.Count / Frame;
        if (frames == 0)
        {
            float mean = 0;
            for (var i = 0; i < buffer.Count; i++) mean += Math.Abs(buffer[i]);
            mean = buffer.Count == 0 ? 0 : mean / buffer.Count;
            return mean >= DitherFloor ? DitherFloor : SilenceFloor;
        }
        var minE = float.MaxValue;
        float maxE = 0;
        for (var i = 0; i < frames; i++)
        {
            var e = FrameEnergy(buffer, i);
            minE = Math.Min(minE, e);
            maxE = Math.Max(maxE, e);
        }
        var floor = Math.Min(Math.Max(minE * 4, DitherFloor), SilenceFloor);
        if (maxE >= floor) return floor;
        if (frames > MaxFlatSpeechFrames) return SilenceFloor;
        return maxE >= DitherFloor ? DitherFloor : SilenceFloor;
    }

    public static Cut NextCut(IReadOnlyList<float> buffer, bool draining, float floor = SilenceFloor)
    {
        var n = buffer.Count;
        var frames = n / Frame;

        int? speechFrame = null;
        for (var i = 0; i < frames; i++)
            if (FrameEnergy(buffer, i) >= floor) { speechFrame = i; break; }

        if (speechFrame is not { } s)
            return new Cut(draining ? n : frames * Frame, null);

        var drop = s * Frame;
        var silentRun = 0;
        for (var i = s; i < frames; i++)
        {
            if (FrameEnergy(buffer, i) < floor)
            {
                silentRun++;
                if (silentRun == PauseFrames)
                {
                    var speechEndFrame = i - PauseFrames + 1;
                    var speechLen = speechEndFrame * Frame - drop;
                    if (speechLen < MinSpeechSamples)
                        return new Cut((i + 1) * Frame, null);
                    return new Cut(drop, (speechEndFrame + PadFrames) * Frame - drop);
                }
            }
            else
            {
                silentRun = 0;
            }
        }

        var len = n - drop;
        if (len >= MaxSegmentSamples) return new Cut(drop, MaxSegmentSamples);
        if (draining) return new Cut(drop, len);
        return new Cut(drop, null);
    }
}
