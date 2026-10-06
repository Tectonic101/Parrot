// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/TranscriptionEngine.swift.
namespace Parrot.Core.Transcription;

/// One decoded piece of an utterance; times are relative to the start of the samples.
public sealed record TranscribedPiece(string Text, double Start, double End);

/// Turns one utterance (16 kHz mono float samples, -1…1) into text.
/// Implementations: on-device Whisper.net (Parrot.Audio), OpenAI/Groq Whisper, Deepgram.
public interface ITranscriber : IDisposable
{
    string Name { get; }
    bool IsCloud { get; }
    Task<IReadOnlyList<TranscribedPiece>> TranscribeAsync(float[] samples, CancellationToken ct = default);
}

public sealed class TranscriptionException : Exception
{
    public TranscriptionException(string message) : base(message) { }
}
