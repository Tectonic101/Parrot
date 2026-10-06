// Parrot for Windows. Derived from Parrot (GPL-3.0), SettingsView.swift, ProfilesSettingsView.swift,
// KnowledgeSettingsView.swift.
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Parrot.App.Services;
using Parrot.Audio;
using Parrot.Core.AI;
using Parrot.Core.Copilot;
using Parrot.Core.Diagnostics;
using Parrot.Core.Models;
using Parrot.Core.Storage;
using Parrot.Core.Transcription;

namespace Parrot.App.ViewModels;

public sealed partial class SecretFieldViewModel : ObservableObject
{
    private readonly ISecretStore _store;
    public string Account { get; }
    public string Label { get; }
    public string Hint { get; }
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private string? _message;

    public SecretFieldViewModel(ISecretStore store, string account, string label, string hint)
    {
        _store = store;
        Account = account;
        Label = label;
        Hint = hint;
        _hasKey = !string.IsNullOrEmpty(store.Get(account));
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;
        var ok = _store.Set(Account, Input.Trim());
        Message = ok ? "Saved (encrypted for your Windows account)." : "Couldn't save the key.";
        HasKey = ok || HasKey;
        Input = "";
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Set(Account, null);
        HasKey = false;
        Message = "Removed.";
    }
}

public sealed partial class KnowledgeDocItem : ObservableObject
{
    private readonly Action<KBDocument> _save;
    public KBDocument Document { get; }
    public string Name => Document.Name;
    public string Detail => $"{Document.ChunkCount} passages · added {Document.AddedAt:MMM d, yyyy}";
    [ObservableProperty] private string _note;
    [ObservableProperty] private bool _enabled;

    public KnowledgeDocItem(KBDocument doc, Action<KBDocument> save)
    {
        Document = doc;
        _save = save;
        _note = doc.Note;
        _enabled = !doc.Disabled;
    }

    partial void OnNoteChanged(string value) { Document.Note = value; _save(Document); }
    partial void OnEnabledChanged(bool value) { Document.Disabled = !value; _save(Document); }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private AppSettings S => _s.Settings;
    private bool _loading;

    public IReadOnlyList<Option<ProviderKind>> ProviderOptions { get; } =
        Enum.GetValues<ProviderKind>().Select(k => new Option<ProviderKind>(k, ProviderCatalog.Label(k))).ToList();
    public IReadOnlyList<Option<ProviderKind?>> ReportsProviderOptions { get; } =
        new[] { new Option<ProviderKind?>(null, "Same as live") }
            .Concat(Enum.GetValues<ProviderKind>().Select(k => new Option<ProviderKind?>(k, ProviderCatalog.Label(k)))).ToList();
    public IReadOnlyList<Option<CopilotPace>> PaceOptions { get; } =
        Enum.GetValues<CopilotPace>().Select(p => new Option<CopilotPace>(p, $"{p} — {p.Caption()}")).ToList();
    public IReadOnlyList<Option<int>> WindowOptions { get; } = new[]
    {
        new Option<int>(2, "Recent — last 2 minutes"),
        new Option<int>(5, "Standard — last 5 minutes"),
        new Option<int>(10, "Long — last 10 minutes"),
    };
    public IReadOnlyList<Option<TranscriptionEngineKind>> EngineOptions { get; } = new[]
    {
        new Option<TranscriptionEngineKind>(TranscriptionEngineKind.Local, "On-device Whisper — private, free (default)"),
        new Option<TranscriptionEngineKind>(TranscriptionEngineKind.Groq, "Groq Whisper large-v3-turbo — cloud, fast, needs a Groq key"),
        new Option<TranscriptionEngineKind>(TranscriptionEngineKind.OpenAI, "OpenAI Whisper — cloud, needs an OpenAI key"),
        new Option<TranscriptionEngineKind>(TranscriptionEngineKind.Deepgram, "Deepgram Nova-3 — cloud, needs a Deepgram key"),
    };
    public IReadOnlyList<Option<string>> WhisperModelOptions { get; } =
        WhisperModels.All.Select(m => new Option<string>(m.Name, m.Label)).ToList();
    public IReadOnlyList<Option<string>> LanguageOptions { get; } = new[]
    {
        ("auto", "Auto-detect"), ("en", "English"), ("tr", "Turkish"), ("es", "Spanish"), ("de", "German"),
        ("fr", "French"), ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("ru", "Russian"),
        ("ar", "Arabic"), ("zh", "Chinese"), ("ja", "Japanese"), ("ko", "Korean"), ("hi", "Hindi"),
    }.Select(x => new Option<string>(x.Item1, x.Item2)).ToList();
    public IReadOnlyList<Option<ThemePreference>> ThemeOptions { get; } = new[]
    {
        new Option<ThemePreference>(ThemePreference.System, "Match Windows"),
        new Option<ThemePreference>(ThemePreference.Light, "Light"),
        new Option<ThemePreference>(ThemePreference.Dark, "Dark"),
    };

    public ObservableCollection<Option<string?>> Microphones { get; } = new();
    public ObservableCollection<Option<string?>> Outputs { get; } = new();
    public ObservableCollection<SecretFieldViewModel> Keys { get; } = new();
    public ObservableCollection<CallProfile> Profiles { get; } = new();
    public ObservableCollection<KnowledgeDocItem> Documents { get; } = new();

    // Assistant
    [ObservableProperty] private Option<ProviderKind>? _liveProvider;
    [ObservableProperty] private Option<ProviderKind?>? _reportsProvider;
    [ObservableProperty] private string _anthropicModel = "";
    [ObservableProperty] private string _openAIModel = "";
    [ObservableProperty] private string _groqModel = "";
    [ObservableProperty] private string _geminiModel = "";
    [ObservableProperty] private string _ollamaModel = "";
    [ObservableProperty] private string _ollamaBaseUrl = "";
    [ObservableProperty] private string _customModel = "";
    [ObservableProperty] private string _customBaseUrl = "";
    [ObservableProperty] private bool _copilotEnabled;
    [ObservableProperty] private Option<CopilotPace>? _pace;
    [ObservableProperty] private Option<int>? _window;
    [ObservableProperty] private bool _generateReport;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _isTesting;

    // Transcription
    [ObservableProperty] private Option<TranscriptionEngineKind>? _engine;
    [ObservableProperty] private Option<string>? _whisperModel;
    [ObservableProperty] private Option<string>? _language;
    [ObservableProperty] private string _vocabulary = "";
    [ObservableProperty] private bool _echoSuppression;
    [ObservableProperty] private Option<string?>? _microphone;
    [ObservableProperty] private Option<string?>? _output;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string? _modelStatus;

    // Profiles
    [ObservableProperty] private CallProfile? _selectedProfile;
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _profileCounterpart = "";
    [ObservableProperty] private string _profilePersona = "";
    [ObservableProperty] private string _profileTone = "";
    [ObservableProperty] private bool _profileAllowGeneral;
    [ObservableProperty] private string _profileKinds = "";

    // Knowledge / appearance
    [ObservableProperty] private string? _knowledgeMessage;
    [ObservableProperty] private Option<ThemePreference>? _theme;

    public string DataFolder => _s.Paths.Root;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        _loading = true;
        LiveProvider = ProviderOptions.First(o => o.Value == S.LiveProvider);
        ReportsProvider = ReportsProviderOptions.First(o => o.Value == S.ReportsProvider);
        AnthropicModel = S.AnthropicModel;
        OpenAIModel = S.OpenAIModel;
        GroqModel = S.GroqModel;
        GeminiModel = S.GeminiModel;
        OllamaModel = S.OllamaModel;
        OllamaBaseUrl = S.OllamaBaseUrl;
        CustomModel = S.CustomModel;
        CustomBaseUrl = S.CustomBaseUrl;
        CopilotEnabled = S.CopilotEnabled;
        Pace = PaceOptions.First(o => o.Value == S.CopilotPace);
        Window = WindowOptions.FirstOrDefault(o => o.Value == S.CopilotWindowMinutes) ?? WindowOptions[1];
        GenerateReport = S.GenerateReportAfterCall;
        Engine = EngineOptions.First(o => o.Value == S.TranscriptionEngine);
        WhisperModel = WhisperModelOptions.FirstOrDefault(o => o.Value == S.WhisperModel) ?? WhisperModelOptions[1];
        Language = LanguageOptions.FirstOrDefault(o => o.Value == S.Language) ?? LanguageOptions[0];
        Vocabulary = S.Vocabulary;
        EchoSuppression = S.EchoSuppression;
        Theme = ThemeOptions.First(o => o.Value == S.Theme);

        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.Anthropic, "Anthropic (Claude)", "console.anthropic.com → API keys"));
        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.OpenAI, "OpenAI", "Used for OpenAI chat and OpenAI Whisper"));
        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.Groq, "Groq", "Used for Groq chat and Groq Whisper"));
        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.Gemini, "Google Gemini", "aistudio.google.com → API keys"));
        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.Deepgram, "Deepgram", "Transcription only"));
        Keys.Add(new SecretFieldViewModel(_s.Secrets, SecretAccounts.Custom, "Custom server", "Optional bearer token for your server"));

        LoadDevices();
        foreach (var p in _s.Profiles.Profiles) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == S.ActiveProfileId) ?? Profiles.FirstOrDefault();
        LoadDocuments();
        _s.Knowledge.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(LoadDocuments);
        RefreshModelStatus();
        _loading = false;
    }

    private void LoadDevices()
    {
        Microphones.Clear();
        Outputs.Clear();
        Microphones.Add(new Option<string?>(null, "Windows default microphone"));
        Outputs.Add(new Option<string?>(null, "Windows default output"));
        try
        {
            foreach (var d in AudioDevices.Microphones()) Microphones.Add(new Option<string?>(d.Id, d.Name));
            foreach (var d in AudioDevices.Outputs()) Outputs.Add(new Option<string?>(d.Id, d.Name));
        }
        catch (Exception e)
        {
            Log.Error("Device enumeration failed", e);
        }
        Microphone = Microphones.FirstOrDefault(m => m.Value == S.MicrophoneDeviceId) ?? Microphones[0];
        Output = Outputs.FirstOrDefault(m => m.Value == S.OutputDeviceId) ?? Outputs[0];
    }

    private void LoadDocuments()
    {
        Documents.Clear();
        foreach (var d in _s.Knowledge.Documents.OrderBy(d => d.Name))
            Documents.Add(new KnowledgeDocItem(d, doc => _s.Knowledge.Update(doc)));
    }

    private void Persist(Action<AppSettings> apply)
    {
        if (_loading) return;
        apply(S);
        _s.SaveSettings();
    }

    partial void OnLiveProviderChanged(Option<ProviderKind>? value) { if (value != null) Persist(s => s.LiveProvider = value.Value); }
    partial void OnReportsProviderChanged(Option<ProviderKind?>? value) { if (value != null) Persist(s => s.ReportsProvider = value.Value); }
    partial void OnAnthropicModelChanged(string value) => Persist(s => s.AnthropicModel = value.Trim());
    partial void OnOpenAIModelChanged(string value) => Persist(s => s.OpenAIModel = value.Trim());
    partial void OnGroqModelChanged(string value) => Persist(s => s.GroqModel = value.Trim());
    partial void OnGeminiModelChanged(string value) => Persist(s => s.GeminiModel = value.Trim());
    partial void OnOllamaModelChanged(string value) => Persist(s => s.OllamaModel = value.Trim());
    partial void OnOllamaBaseUrlChanged(string value) => Persist(s => s.OllamaBaseUrl = value.Trim());
    partial void OnCustomModelChanged(string value) => Persist(s => s.CustomModel = value.Trim());
    partial void OnCustomBaseUrlChanged(string value) => Persist(s => s.CustomBaseUrl = value.Trim());
    partial void OnCopilotEnabledChanged(bool value) => Persist(s => s.CopilotEnabled = value);
    partial void OnPaceChanged(Option<CopilotPace>? value) { if (value != null) Persist(s => s.CopilotPace = value.Value); }
    partial void OnWindowChanged(Option<int>? value) { if (value != null) Persist(s => s.CopilotWindowMinutes = value.Value); }
    partial void OnGenerateReportChanged(bool value) => Persist(s => s.GenerateReportAfterCall = value);
    partial void OnEngineChanged(Option<TranscriptionEngineKind>? value) { if (value != null) Persist(s => s.TranscriptionEngine = value.Value); }
    partial void OnWhisperModelChanged(Option<string>? value)
    {
        if (value == null) return;
        Persist(s => s.WhisperModel = value.Value);
        RefreshModelStatus();
    }
    partial void OnLanguageChanged(Option<string>? value) { if (value != null) Persist(s => s.Language = value.Value); }
    partial void OnVocabularyChanged(string value) => Persist(s => s.Vocabulary = value);
    partial void OnEchoSuppressionChanged(bool value) => Persist(s => s.EchoSuppression = value);
    partial void OnMicrophoneChanged(Option<string?>? value) { if (value != null) Persist(s => s.MicrophoneDeviceId = value.Value); }
    partial void OnOutputChanged(Option<string?>? value) { if (value != null) Persist(s => s.OutputDeviceId = value.Value); }
    partial void OnThemeChanged(Option<ThemePreference>? value)
    {
        if (value == null) return;
        Persist(s => s.Theme = value.Value);
        if (!_loading) ThemeManager.Apply(value.Value);
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        _loading = true;
        LoadDevices();
        _loading = false;
    }

    private void RefreshModelStatus()
    {
        var name = WhisperModel?.Value ?? "base";
        var downloaded = new ModelDownloader(_s.Http, _s.Paths.ModelsDir()).IsDownloaded(name);
        ModelStatus = downloaded ? $"ggml-{name}.bin is downloaded." : $"ggml-{name}.bin downloads on first recording, or now:";
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (IsDownloading) return;
        IsDownloading = true;
        try
        {
            var name = WhisperModel?.Value ?? "base";
            var progress = new Progress<double>(p => DownloadProgress = Math.Max(0, p) * 100);
            await new ModelDownloader(_s.Http, _s.Paths.ModelsDir()).EnsureAsync(name, progress);
        }
        catch (Exception e)
        {
            ModelStatus = "Download failed: " + e.Message;
            IsDownloading = false;
            return;
        }
        IsDownloading = false;
        RefreshModelStatus();
    }

    /// Sends a one-line prompt to the live provider so the user knows the key/model work.
    [RelayCommand]
    private async Task TestProviderAsync()
    {
        IsTesting = true;
        TestResult = "Testing…";
        try
        {
            var provider = _s.LiveProvider();
            if (!provider.IsConfigured) { TestResult = "Not set up yet: add the key / model above."; return; }
            var reply = await provider.CompleteAsync("Reply with the single word OK.", "Say OK.", 20);
            TestResult = $"Works: {provider.DisplayName} replied \"{reply.Trim()}\".";
        }
        catch (Exception e)
        {
            TestResult = "Failed: " + e.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    // MARK: Profiles

    partial void OnSelectedProfileChanged(CallProfile? value)
    {
        if (value == null) return;
        var wasLoading = _loading;
        _loading = true;
        ProfileName = value.Name;
        ProfileCounterpart = value.Counterpart;
        ProfilePersona = value.Persona;
        ProfileTone = value.Tone;
        ProfileAllowGeneral = value.AllowGeneralKnowledge;
        ProfileKinds = string.Join("\n", value.Kinds.Select(k => $"• {k.Label}{(k.IsPinned ? " (stays until handled)" : "")} — {k.TriggerDescription}"));
        _loading = wasLoading;
        if (!_loading) Persist(s => s.ActiveProfileId = value.Id);
    }

    private void EditProfile(Action<CallProfile> edit, bool behavior = true)
    {
        if (_loading || SelectedProfile == null) return;
        edit(SelectedProfile);
        if (behavior && SelectedProfile.IsBuiltIn) SelectedProfile.IsUserModified = true;
        _s.Profiles.Upsert(SelectedProfile);
    }

    partial void OnProfileNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || SelectedProfile?.IsBuiltIn == true) return;
        EditProfile(p => p.Name = value.Trim(), behavior: false);
    }
    partial void OnProfileCounterpartChanged(string value) => EditProfile(p => p.Counterpart = string.IsNullOrWhiteSpace(value) ? "the other person" : value.Trim());
    partial void OnProfilePersonaChanged(string value) => EditProfile(p => p.Persona = value);
    partial void OnProfileToneChanged(string value) => EditProfile(p => p.Tone = value, behavior: false);
    partial void OnProfileAllowGeneralChanged(bool value) => EditProfile(p => p.AllowGeneralKnowledge = value);

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(SelectedProfile);
        var copy = System.Text.Json.JsonSerializer.Deserialize<CallProfile>(json)!;
        copy.Id = Guid.NewGuid();
        copy.Name = SelectedProfile.Name + " copy";
        copy.IsBuiltIn = false;
        copy.IsUserModified = false;
        copy.SortOrder = (_s.Profiles.Profiles.Max(p => (int?)p.SortOrder) ?? 0) + 1;
        _s.Profiles.Upsert(copy);
        Profiles.Add(copy);
        SelectedProfile = copy;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || SelectedProfile.IsBuiltIn) return;
        var victim = SelectedProfile;
        if (_s.Profiles.Delete(victim.Id))
        {
            Profiles.Remove(victim);
            SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    // MARK: Knowledge

    [RelayCommand]
    private async Task AddDocumentsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Documents (*.txt;*.md;*.markdown;*.pdf)|*.txt;*.md;*.markdown;*.pdf|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        KnowledgeMessage = "Indexing…";
        var errors = await Task.Run(() => dialog.FileNames.Select(f => _s.Knowledge.AddDocument(f)).Where(e => e != null).ToList());
        KnowledgeMessage = errors.Count == 0 ? $"Added {dialog.FileNames.Length} document(s)." : string.Join("\n", errors);
        LoadDocuments();
    }

    [RelayCommand]
    private void RemoveDocument(KnowledgeDocItem? item)
    {
        if (item == null) return;
        _s.Knowledge.Remove(item.Document.Id);
        LoadDocuments();
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
}
