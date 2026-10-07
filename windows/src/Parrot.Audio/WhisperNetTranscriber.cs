// Parrot for Windows. Derived from Parrot (GPL-3.0), the on-device path of TranscriptionEngine.swift
// (WhisperKit on the Mac → whisper.cpp via Whisper.net here).
using Parrot.Core.Transcription;
using Whisper.net;

namespace Parrot.Audio;

/// On-device transcription with whisper.cpp. Audio never leaves the PC.
/// Not thread-safe: the recording session calls it from a single worker.
public sealed class WhisperNetTranscriber : ITranscriber
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Name { get; }
    public bool IsCloud => false;

    public WhisperNetTranscriber(string modelPath, string modelName, string? language, string? glossaryPrompt)
    {
        Name = $"On-device Whisper ({modelName})";
        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder()
            .WithThreads(Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1)));
        builder = string.IsNullOrEmpty(language) || language == "auto"
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(language);
        if (!string.IsNullOrEmpty(glossaryPrompt)) builder = builder.WithPrompt(glossaryPrompt);
        _processor = builder.Build();
    }

    public async Task<IReadOnlyList<TranscribedPiece>> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pieces = new List<TranscribedPiece>();
            await foreach (var segment in _processor.ProcessAsync(samples, ct).ConfigureAwait(false))
            {
                var text = TranscriptFilters.Cleaned(segment.Text);
                if (text.Length == 0) continue;
                pieces.Add(new TranscribedPiece(text, segment.Start.TotalSeconds, segment.End.TotalSeconds));
            }
            return pieces;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _gate.Dispose();
    }
}
