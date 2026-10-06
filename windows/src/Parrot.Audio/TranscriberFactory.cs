// Parrot for Windows. Derived from Parrot (GPL-3.0), TranscriptionBackend in CloudTranscription.swift.
using Parrot.Core.Storage;
using Parrot.Core.Transcription;

namespace Parrot.Audio;

public static class TranscriberFactory
{
    /// Builds the transcriber the settings ask for. Local downloads its model on first use
    /// (progress 0…1). Cloud engines need their key; they are opt-in.
    public static async Task<ITranscriber> CreateAsync(AppSettings settings, ISecretStore secrets, HttpClient http,
                                                       IAppPaths paths, IProgress<double>? downloadProgress,
                                                       CancellationToken ct = default)
    {
        var language = settings.Language is null or "auto" ? null : settings.Language;
        switch (settings.TranscriptionEngine)
        {
            case TranscriptionEngineKind.OpenAI:
                return WhisperApiTranscriber.OpenAI(http, () => secrets.Get(SecretAccounts.OpenAI), language);
            case TranscriptionEngineKind.Groq:
                return WhisperApiTranscriber.Groq(http, () => secrets.Get(SecretAccounts.Groq), language);
            case TranscriptionEngineKind.Deepgram:
                return new DeepgramTranscriber(http, () => secrets.Get(SecretAccounts.Deepgram), language);
            default:
                var model = WhisperModels.IsKnown(settings.WhisperModel) ? settings.WhisperModel : "base";
                var downloader = new ModelDownloader(http, paths.ModelsDir());
                var path = await downloader.EnsureAsync(model, downloadProgress, ct).ConfigureAwait(false);
                return await Task.Run(() => new WhisperNetTranscriber(path, model, language,
                    TranscriptFilters.GlossaryPrompt(settings.Vocabulary)), ct).ConfigureAwait(false);
        }
    }
}
