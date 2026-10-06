// Parrot for Windows. Derived from Parrot (GPL-3.0) — the Mac keeps these in @AppStorage.
using Parrot.Core.AI;
using Parrot.Core.Copilot;
using Parrot.Core.Profiles;

namespace Parrot.Core.Storage;

public enum TranscriptionEngineKind
{
    /// On-device whisper.cpp (Whisper.net) — private, free (default).
    Local,
    /// OpenAI Whisper API (whisper-1) — audio leaves the PC.
    OpenAI,
    /// Groq whisper-large-v3-turbo — audio leaves the PC.
    Groq,
    /// Deepgram Nova-3 (pre-recorded endpoint, per utterance) — audio leaves the PC.
    Deepgram,
}

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    // Assistant / LLM
    public ProviderKind LiveProvider { get; set; } = ProviderKind.Anthropic;
    /// null = same as live.
    public ProviderKind? ReportsProvider { get; set; }
    public string AnthropicModel { get; set; } = ProviderCatalog.DefaultModel(ProviderKind.Anthropic);
    public string OpenAIModel { get; set; } = ProviderCatalog.DefaultModel(ProviderKind.OpenAI);
    public string GroqModel { get; set; } = ProviderCatalog.DefaultModel(ProviderKind.Groq);
    public string GeminiModel { get; set; } = ProviderCatalog.DefaultModel(ProviderKind.Gemini);
    public string OllamaModel { get; set; } = ProviderCatalog.DefaultModel(ProviderKind.Ollama);
    public string OllamaBaseUrl { get; set; } = ProviderCatalog.DefaultBaseUrl(ProviderKind.Ollama);
    public string CustomModel { get; set; } = "";
    public string CustomBaseUrl { get; set; } = "";

    // Copilot
    public bool CopilotEnabled { get; set; } = true;
    public CopilotPace CopilotPace { get; set; } = CopilotPace.Fast;
    public int CopilotWindowMinutes { get; set; } = 5;
    public Guid ActiveProfileId { get; set; } = ProfilePresets.DefaultProfileId;
    public bool GenerateReportAfterCall { get; set; } = true;

    // Transcription
    public TranscriptionEngineKind TranscriptionEngine { get; set; } = TranscriptionEngineKind.Local;
    /// ggml model name for local Whisper: tiny, base, small, medium, large-v3-turbo (+ ".en" variants).
    public string WhisperModel { get; set; } = "base";
    /// ISO code or "auto".
    public string Language { get; set; } = "auto";
    /// Glossary of names/terms (comma separated) to prime local Whisper.
    public string Vocabulary { get; set; } = "";
    /// Drop "Me" lines that repeat what "Them" just said (speaker bleed without headphones).
    public bool EchoSuppression { get; set; } = true;

    // Audio devices (null = system default)
    public string? MicrophoneDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public string ModelFor(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => AnthropicModel,
        ProviderKind.OpenAI => OpenAIModel,
        ProviderKind.Groq => GroqModel,
        ProviderKind.Gemini => GeminiModel,
        ProviderKind.Ollama => OllamaModel,
        _ => CustomModel,
    };
}

public sealed class SettingsStore
{
    private readonly IAppPaths _paths;
    public SettingsStore(IAppPaths paths) => _paths = paths;

    public AppSettings Load()
    {
        try { return JsonFile.Read<AppSettings>(_paths.SettingsFile()) ?? new AppSettings(); }
        catch (Exception) { return new AppSettings(); }
    }

    public void Save(AppSettings settings) => JsonFile.Write(_paths.SettingsFile(), settings);
}
