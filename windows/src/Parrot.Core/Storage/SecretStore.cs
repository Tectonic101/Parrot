// Parrot for Windows. Derived from Parrot (GPL-3.0), APIKeyStore in AnalysisProvider.swift.
namespace Parrot.Core.Storage;

/// API key storage. The Mac uses the Keychain; Windows uses DPAPI (see Parrot.App).
/// Never log or persist key material in plain text.
public interface ISecretStore
{
    string? Get(string account);
    /// Empty/null removes the key. Returns false if the store rejected the write.
    bool Set(string account, string? value);
}

public static class SecretAccounts
{
    public const string Anthropic = "anthropic-api-key";
    public const string OpenAI = "openai-api-key";
    public const string Groq = "groq-api-key";
    public const string Gemini = "gemini-api-key";
    public const string Deepgram = "deepgram-api-key";
    public const string Custom = "custom-llm-api-key";
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new();
    public string? Get(string account) => _values.TryGetValue(account, out var v) ? v : null;
    public bool Set(string account, string? value)
    {
        if (string.IsNullOrEmpty(value)) _values.Remove(account); else _values[account] = value;
        return true;
    }
}
