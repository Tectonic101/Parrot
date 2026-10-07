// Parrot for Windows. Derived from Parrot (GPL-3.0).
namespace Parrot.Core.Diagnostics;

/// Minimal append-only log (parrot.log). Never log key material or transcript text.
public static class Log
{
    private static readonly object Lock = new();
    private static string? _path;

    public static void Init(string path) => _path = path;

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e == null ? message : $"{message}: {e.GetType().Name}: {e.Message}");

    private static void Write(string level, string message)
    {
        if (_path == null) return;
        try
        {
            lock (Lock)
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 2_000_000) File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* logging must never crash the app */ }
    }
}
