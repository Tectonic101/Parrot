// Parrot for Windows. Derived from Parrot (GPL-3.0).
namespace Parrot.Core.Storage;

/// Where Parrot keeps its data. The app uses %LOCALAPPDATA%\Parrot; tests use a temp dir.
public interface IAppPaths
{
    string Root { get; }
}

public sealed class AppPaths : IAppPaths
{
    public string Root { get; }

    public AppPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    /// %LOCALAPPDATA%\Parrot (on non-Windows, the platform's local app data folder).
    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parrot"));
}

public static class AppPathsExtensions
{
    public static string MeetingsDir(this IAppPaths p) => Ensure(Path.Combine(p.Root, "meetings"));
    public static string AudioDir(this IAppPaths p, Guid meetingId) => Ensure(Path.Combine(p.Root, "audio", meetingId.ToString()));
    public static string ModelsDir(this IAppPaths p) => Ensure(Path.Combine(p.Root, "models"));
    public static string ExportsDir(this IAppPaths p) => Ensure(Path.Combine(p.Root, "exports"));
    public static string SettingsFile(this IAppPaths p) => Path.Combine(p.Root, "settings.json");
    public static string ProfilesFile(this IAppPaths p) => Path.Combine(p.Root, "profiles.json");
    public static string KnowledgeFile(this IAppPaths p) => Path.Combine(p.Root, "knowledge.json");
    public static string SecretsFile(this IAppPaths p) => Path.Combine(p.Root, "secrets.dat");
    public static string LogFile(this IAppPaths p) => Path.Combine(p.Root, "parrot.log");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
