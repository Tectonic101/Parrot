// Parrot for Windows. Derived from Parrot (GPL-3.0), SwitchingAnalysisProvider in OpenAICompatibleProvider.swift.
using Parrot.Core.Storage;

namespace Parrot.Core.AI;

public static class ProviderFactory
{
    public static IAnalysisProvider Create(ProviderKind kind, AppSettings settings, ISecretStore secrets, HttpClient http)
    {
        var account = ProviderCatalog.SecretAccount(kind);
        Func<string?> key = () => account == null ? null : secrets.Get(account);
        return kind switch
        {
            ProviderKind.Anthropic => new AnthropicProvider(http, key, settings.AnthropicModel),
            ProviderKind.Ollama => new OpenAICompatibleProvider(http, kind, settings.OllamaBaseUrl, settings.OllamaModel, key),
            ProviderKind.Custom => new OpenAICompatibleProvider(http, kind, settings.CustomBaseUrl, settings.CustomModel, key),
            _ => new OpenAICompatibleProvider(http, kind, ProviderCatalog.DefaultBaseUrl(kind), settings.ModelFor(kind), key),
        };
    }

    public static IAnalysisProvider Live(AppSettings s, ISecretStore secrets, HttpClient http) =>
        Create(s.LiveProvider, s, secrets, http);

    /// The reports provider, falling back to the live one when the chosen one isn't
    /// configured — a report must never be lost to a half-set-up secondary backend.
    public static IAnalysisProvider Reports(AppSettings s, ISecretStore secrets, HttpClient http)
    {
        var live = Live(s, secrets, http);
        if (s.ReportsProvider is not { } kind || kind == s.LiveProvider) return live;
        var candidate = Create(kind, s, secrets, http);
        return candidate.IsConfigured ? candidate : live;
    }
}
