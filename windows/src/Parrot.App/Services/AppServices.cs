// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Net.Http;
using Parrot.Core.AI;
using Parrot.Core.Diagnostics;
using Parrot.Core.Knowledge;
using Parrot.Core.Profiles;
using Parrot.Core.Storage;

namespace Parrot.App.Services;

/// The app's long-lived services, created once at startup and injected into view models.
public sealed class AppServices
{
    public IAppPaths Paths { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; private set; }
    public MeetingStore Meetings { get; }
    public ProfileStore Profiles { get; }
    public ISecretStore Secrets { get; }
    public KnowledgeBase Knowledge { get; }
    public HttpClient Http { get; }

    public event Action? SettingsChanged;

    public AppServices(IAppPaths paths)
    {
        Paths = paths;
        Log.Init(paths.LogFile());
        SettingsStore = new SettingsStore(paths);
        Settings = SettingsStore.Load();
        Meetings = new MeetingStore(paths);
        Profiles = new ProfileStore(paths);
        Secrets = new DpapiSecretStore(paths.SecretsFile());
        Knowledge = new KnowledgeBase(paths);
        Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Parrot-Windows/0.1");
    }

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
        SettingsChanged?.Invoke();
    }

    public IAnalysisProvider LiveProvider() => ProviderFactory.Live(Settings, Secrets, Http);
    public IAnalysisProvider ReportsProvider() => ProviderFactory.Reports(Settings, Secrets, Http);
}
