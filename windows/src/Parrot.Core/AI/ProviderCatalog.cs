// Parrot for Windows. Derived from Parrot (GPL-3.0), CopilotProviderKind in OpenAICompatibleProvider.swift.
namespace Parrot.Core.AI;

/// Which backend powers the assistant. The Mac has Claude / Ollama / Custom;
/// Windows adds first-class presets for the common OpenAI-compatible clouds.
public enum ProviderKind
{
    Anthropic,
    OpenAI,
    Groq,
    Gemini,
    Ollama,
    Custom,
}

public static class ProviderCatalog
{
    public static string Label(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "Claude (Anthropic, cloud)",
        ProviderKind.OpenAI => "OpenAI (cloud)",
        ProviderKind.Groq => "Groq (cloud)",
        ProviderKind.Gemini => "Google Gemini (cloud)",
        ProviderKind.Ollama => "Ollama (local)",
        _ => "Custom OpenAI-compatible server",
    };

    /// Defaults the user can override in Settings. Claude keeps the Mac's choice
    /// (Haiku: the fastest model, for low-latency live cards).
    public static string DefaultModel(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "claude-haiku-4-5",
        ProviderKind.OpenAI => "gpt-4o-mini",
        ProviderKind.Groq => "llama-3.3-70b-versatile",
        ProviderKind.Gemini => "gemini-2.5-flash",
        ProviderKind.Ollama => "llama3.2:3b",
        _ => "",
    };

    public static string DefaultBaseUrl(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "https://api.anthropic.com/v1",
        ProviderKind.OpenAI => "https://api.openai.com/v1",
        ProviderKind.Groq => "https://api.groq.com/openai/v1",
        ProviderKind.Gemini => "https://generativelanguage.googleapis.com/v1beta/openai",
        ProviderKind.Ollama => "http://localhost:11434/v1",
        _ => "",
    };

    public static string? SecretAccount(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => Storage.SecretAccounts.Anthropic,
        ProviderKind.OpenAI => Storage.SecretAccounts.OpenAI,
        ProviderKind.Groq => Storage.SecretAccounts.Groq,
        ProviderKind.Gemini => Storage.SecretAccounts.Gemini,
        ProviderKind.Custom => Storage.SecretAccounts.Custom,
        _ => null,
    };

    public static bool IsLocal(ProviderKind kind) => kind == ProviderKind.Ollama;
}
