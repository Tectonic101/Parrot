// Parrot for Windows. Derived from Parrot (GPL-3.0), APIKeyStore (Keychain on the Mac).
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Parrot.Core.Storage;

namespace Parrot.App.Services;

/// API keys encrypted with Windows DPAPI (CurrentUser scope): only this Windows user
/// on this PC can decrypt secrets.dat. Keys are never logged or written in plain text.
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Parrot.Windows.secrets.v1");
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, string> _values;

    public DpapiSecretStore(string path)
    {
        _path = path;
        _values = Load();
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch (Exception)
        {
            // Wrong user / corrupted: start empty, never overwrite until the user saves a key.
            return new();
        }
    }

    public string? Get(string account)
    {
        lock (_lock) return _values.TryGetValue(account, out var v) ? v : null;
    }

    public bool Set(string account, string? value)
    {
        lock (_lock)
        {
            var next = new Dictionary<string, string>(_values);
            if (string.IsNullOrEmpty(value)) next.Remove(account); else next[account] = value.Trim();
            try
            {
                var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(next), Entropy, DataProtectionScope.CurrentUser);
                var temp = _path + ".tmp";
                File.WriteAllBytes(temp, cipher);
                File.Move(temp, _path, overwrite: true);
                _values = next;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
