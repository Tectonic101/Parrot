// Parrot for Windows. Derived from Parrot (GPL-3.0), ContentView.swift / SidebarView.swift and the
// start/stop flow of RecordingManager.swift.
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Parrot.App.Services;
using Parrot.Audio;
using Parrot.Core.Copilot;
using Parrot.Core.Diagnostics;
using Parrot.Core.Models;
using Parrot.Core.Recording;
using Parrot.Core.Reports;
using Parrot.Core.Transcription;

namespace Parrot.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private RecordingSession? _session;
    private ITranscriber? _transcriber;
    private CancellationTokenSource? _startCts;

    public ObservableCollection<MeetingListItem> Meetings { get; } = new();
    public ObservableCollection<CallProfile> Profiles { get; } = new();
    public LiveViewModel Live { get; } = new();

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private MeetingListItem? _selectedMeeting;
    [ObservableProperty] private CallProfile? _activeProfile;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isStarting;
    [ObservableProperty] private bool _isStopping;
    [ObservableProperty] private string? _startStatus;
    [ObservableProperty] private double _startProgress = -1;
    [ObservableProperty] private string _searchText = "";

    public string RecordButtonText => IsRecording ? "Stop recording" : IsStarting ? "Cancel" : "Record";

    public MainViewModel(AppServices services)
    {
        _services = services;
        foreach (var p in services.Profiles.Profiles) Profiles.Add(p);
        _activeProfile = Profiles.FirstOrDefault(p => p.Id == services.Settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
        ReloadMeetings();
        CurrentPage = Meetings.Count == 0 ? new WelcomeViewModel(OpenSettings) : null;
        if (Meetings.Count > 0) SelectedMeeting = Meetings[0];
        services.SettingsChanged += () =>
        {
            // Settings may have changed the active profile or edited profiles.
            var id = services.Settings.ActiveProfileId;
            Profiles.Clear();
            foreach (var p in services.Profiles.Profiles) Profiles.Add(p);
            _activeProfile = Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault();
            OnPropertyChanged(nameof(ActiveProfile));
        };
    }

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(RecordButtonText));
    partial void OnIsStartingChanged(bool value) => OnPropertyChanged(nameof(RecordButtonText));

    partial void OnActiveProfileChanged(CallProfile? value)
    {
        if (value == null || value.Id == _services.Settings.ActiveProfileId) return;
        _services.Settings.ActiveProfileId = value.Id;
        _services.SettingsStore.Save(_services.Settings);
    }

    partial void OnSearchTextChanged(string value) => ReloadMeetings();

    private void ReloadMeetings()
    {
        var keep = SelectedMeeting?.Id;
        Meetings.Clear();
        var query = SearchText.Trim();
        foreach (var m in _services.Meetings.LoadAll())
        {
            if (query.Length > 0 && !m.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !(m.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                && !m.Segments.Any(s => s.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
                continue;
            Meetings.Add(new MeetingListItem(m));
        }
        if (keep != null)
        {
            var again = Meetings.FirstOrDefault(m => m.Id == keep);
            if (again != null) { _selectedMeeting = again; OnPropertyChanged(nameof(SelectedMeeting)); }
        }
    }

    partial void OnSelectedMeetingChanged(MeetingListItem? value)
    {
        if (value == null) return;
        OpenMeeting(value.Id);
    }

    private void OpenMeeting(Guid id)
    {
        var meeting = _services.Meetings.Load(id);
        if (meeting == null) return;
        CurrentPage = new MeetingDetailViewModel(_services, meeting, OnMeetingChanged, OnMeetingDeleted);
        var item = Meetings.FirstOrDefault(m => m.Id == id);
        if (item != null && !ReferenceEquals(item, SelectedMeeting))
        {
            _selectedMeeting = item;
            OnPropertyChanged(nameof(SelectedMeeting));
        }
    }

    private void OnMeetingChanged(Meeting meeting)
    {
        Meetings.FirstOrDefault(m => m.Id == meeting.Id)?.Update(meeting);
    }

    private void OnMeetingDeleted(Guid id)
    {
        var item = Meetings.FirstOrDefault(m => m.Id == id);
        if (item != null) Meetings.Remove(item);
        _selectedMeeting = null;
        OnPropertyChanged(nameof(SelectedMeeting));
        CurrentPage = Meetings.Count > 0 ? null : new WelcomeViewModel(OpenSettings);
        if (Meetings.Count > 0) SelectedMeeting = Meetings[0];
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _selectedMeeting = null;
        OnPropertyChanged(nameof(SelectedMeeting));
        CurrentPage = new SettingsViewModel(_services);
    }

    [RelayCommand]
    private void OpenAsk()
    {
        _selectedMeeting = null;
        OnPropertyChanged(nameof(SelectedMeeting));
        CurrentPage = new AskViewModel(_services, OpenMeeting);
    }

    [RelayCommand]
    private void ShowLive()
    {
        if (!IsRecording) return;
        _selectedMeeting = null;
        OnPropertyChanged(nameof(SelectedMeeting));
        CurrentPage = Live;
    }

    [RelayCommand]
    private async Task ToggleRecordAsync()
    {
        if (IsStopping) return;
        if (IsStarting) { _startCts?.Cancel(); return; }
        if (IsRecording) await StopAsync();
        else await StartAsync();
    }

    private async Task StartAsync()
    {
        var settings = _services.Settings;
        var profile = ActiveProfile ?? _services.Profiles.Get(settings.ActiveProfileId);
        IsStarting = true;
        StartStatus = "Getting ready…";
        StartProgress = -1;
        _startCts = new CancellationTokenSource();
        try
        {
            // 1. Transcriber (local model downloads on first use).
            var progress = new Progress<double>(p =>
            {
                StartProgress = p * 100;
                StartStatus = p >= 1 ? "Loading the speech model…" : $"Downloading the speech model ({settings.WhisperModel})… {p:P0}";
            });
            _transcriber = await TranscriberFactory.CreateAsync(settings, _services.Secrets, _services.Http, _services.Paths,
                                                                 progress, _startCts.Token);
            StartStatus = "Starting audio…";

            // 2. Meeting record.
            var meeting = Meeting.Create(DateTime.Now);
            meeting.ProfileId = profile.Id;
            meeting.ProfileName = profile.Name;
            meeting.Counterpart = profile.Counterpart;
            meeting.SnapshotKinds = profile.Kinds.ToList();
            meeting.Brief = string.IsNullOrWhiteSpace(Live.Brief) ? null : Live.Brief.Trim();

            // 3. Copilot (opt-in; needs a configured provider to do anything).
            CopilotEngine? copilot = null;
            if (settings.CopilotEnabled)
            {
                copilot = new CopilotEngine(_services.LiveProvider(), _services.Knowledge,
                                            () => _services.Settings.CopilotPace, () => _services.Settings.CopilotWindowMinutes);
                copilot.Start(profile, Live.Brief);
            }

            // 4. Audio: system audio ("Them") is required; the mic ("Me") is best-effort.
            var sources = new List<IAudioTrackSource>();
            string? micError = null;
            sources.Add(WasapiTrackSource.ForSystemAudio(settings.OutputDeviceId));
            try { sources.Add(WasapiTrackSource.ForMicrophone(settings.MicrophoneDeviceId)); }
            catch (Exception e) { micError = e.Message; }

            _session = new RecordingSession(meeting, sources, _transcriber, copilot, _services.Meetings, _services.Paths,
                                            settings.EchoSuppression);
            Live.Attach(_session, copilot, profile);
            _session.Start();
            if (micError != null) Live.ErrorText = "Recording system audio only — " + micError;

            IsRecording = true;
            Meetings.Insert(0, new MeetingListItem(meeting));
            _selectedMeeting = null;
            OnPropertyChanged(nameof(SelectedMeeting));
            CurrentPage = Live;
            Log.Info($"Recording started ({_transcriber.Name}, mic: {micError == null})");
        }
        catch (OperationCanceledException)
        {
            _transcriber?.Dispose();
            _transcriber = null;
        }
        catch (Exception e)
        {
            Log.Error("Start failed", e);
            _transcriber?.Dispose();
            _transcriber = null;
            MessageBox.Show("Couldn't start recording: " + e.Message, "Parrot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsStarting = false;
            StartStatus = null;
            StartProgress = -1;
        }
    }

    private async Task StopAsync()
    {
        if (_session == null) return;
        IsStopping = true;
        StartStatus = "Finishing the transcript…";
        var session = _session;
        Meeting meeting;
        try
        {
            Live.Detach();
            meeting = await session.StopAsync();
            await session.DisposeAsync();
        }
        finally
        {
            _session = null;
            _transcriber?.Dispose();
            _transcriber = null;
            IsRecording = false;
            IsStopping = false;
            StartStatus = null;
        }
        Log.Info($"Recording stopped: {meeting.Segments.Count} lines, {meeting.Duration:F0}s");

        ReloadMeetings();
        OpenMeeting(meeting.Id);

        if (_services.Settings.GenerateReportAfterCall)
        {
            var profile = _services.Profiles.Get(meeting.ProfileId ?? _services.Settings.ActiveProfileId);
            var generator = new ReportGenerator(_services.ReportsProvider(), _services.Meetings);
            try { await generator.GenerateAsync(meeting, profile); }
            catch (Exception e) { Log.Error("Report failed", e); }
        }
        else
        {
            meeting.Status = MeetingStatus.Done;
            _services.Meetings.Save(meeting);
        }
        OnMeetingChanged(meeting);
        if (CurrentPage is MeetingDetailViewModel detail && detail.Meeting.Id == meeting.Id) detail.Load(meeting);
    }

    /// Called when the window closes mid-recording: stop and save, skip the report.
    public async Task ShutdownAsync()
    {
        if (_session == null) return;
        Live.Detach();
        var meeting = await _session.StopAsync();
        meeting.Status = MeetingStatus.Done;
        _services.Meetings.Save(meeting);
        await _session.DisposeAsync();
        _transcriber?.Dispose();
    }
}

/// First-run page: what Parrot does and what to set up.
public sealed class WelcomeViewModel
{
    public IRelayCommand OpenSettingsCommand { get; }
    public WelcomeViewModel(Action openSettings) => OpenSettingsCommand = new RelayCommand(openSettings);
}
