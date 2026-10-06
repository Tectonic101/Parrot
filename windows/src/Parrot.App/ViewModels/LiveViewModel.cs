// Parrot for Windows. Derived from Parrot (GPL-3.0), LiveRecordingView.swift and CopilotPanelView.swift.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Parrot.Core.Copilot;
using Parrot.Core.Models;
using Parrot.Core.Recording;

namespace Parrot.App.ViewModels;

/// The call screen: live transcript (Me/Them), copilot cards, coach line, levels.
public sealed partial class LiveViewModel : ObservableObject
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private RecordingSession? _session;
    private CopilotEngine? _copilot;
    private CallProfile? _profile;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<SegmentItem> Lines { get; } = new();
    public ObservableCollection<InsightItem> Insights { get; } = new();

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _elapsed = "0:00";
    [ObservableProperty] private double _meLevel;
    [ObservableProperty] private double _themLevel;
    [ObservableProperty] private string? _coachLine;
    [ObservableProperty] private string _copilotStatus = "Assistant off";
    [ObservableProperty] private bool _copilotHasProblem;
    [ObservableProperty] private bool _copilotEnabled;
    [ObservableProperty] private bool _copilotPaused;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string? _backlogText;
    [ObservableProperty] private string _brief = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _engineName = "";
    [ObservableProperty] private int? _callScore;

    public LiveViewModel()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Tick(), _ui);
    }

    public void Attach(RecordingSession session, CopilotEngine? copilot, CallProfile profile)
    {
        Lines.Clear();
        Insights.Clear();
        ErrorText = null;
        CoachLine = null;
        CallScore = null;
        Notes = "";
        _session = session;
        _copilot = copilot;
        _profile = profile;
        ProfileName = profile.Name;
        EngineName = session.Meeting.TranscriptionEngine ?? "";
        CopilotEnabled = copilot != null;
        CopilotPaused = false;
        session.SegmentAdded += s => _ui.BeginInvoke(() => Lines.Add(new SegmentItem(s, session.Meeting.DisplayName(s.Speaker))));
        session.SegmentRemoved += s => _ui.BeginInvoke(() =>
        {
            var item = Lines.FirstOrDefault(l => l.Segment.Id == s.Id);
            if (item != null) Lines.Remove(item);
        });
        session.Error += msg => _ui.BeginInvoke(() => ErrorText = msg);
        if (copilot != null)
        {
            copilot.InsightsChanged += () => _ui.BeginInvoke(RefreshInsights);
            copilot.StatusChanged += () => _ui.BeginInvoke(RefreshStatus);
            copilot.PassCompleted += () => _ui.BeginInvoke(() =>
            {
                CoachLine = copilot.CoachLine;
                CallScore = copilot.CallScore;
            });
            RefreshStatus();
        }
        else
        {
            CopilotStatus = "Assistant off — turn it on in Settings → Assistant.";
        }
        IsRecording = true;
        _timer.Start();
    }

    public void Detach()
    {
        _timer.Stop();
        IsRecording = false;
        MeLevel = ThemLevel = 0;
        BacklogText = null;
        if (_session != null) _session.Meeting.Notes = Notes;
    }

    private void Tick()
    {
        if (_session == null) return;
        Elapsed = Core.Models.TimeFormat.Duration(_session.Elapsed);
        MeLevel = Math.Min(1, Math.Sqrt(_session.LevelOf(Speaker.Me)) * 1.4);
        ThemLevel = Math.Min(1, Math.Sqrt(_session.LevelOf(Speaker.Them)) * 1.4);
        var backlog = _session.Backlog;
        BacklogText = backlog > 2 ? $"Transcription is {backlog} lines behind — a smaller Whisper model or a cloud engine keeps up better." : null;
    }

    private void RefreshInsights()
    {
        if (_copilot == null) return;
        Insights.Clear();
        var all = _copilot.Insights.Select(i => new InsightItem(i, _profile?.KindFor(i.KindKey))).ToList();
        // Unresolved pinned flags first, then newest first.
        foreach (var item in all.Where(i => i.IsOpenFlag).Concat(all.Where(i => !i.IsOpenFlag))) Insights.Add(item);
    }

    private void RefreshStatus()
    {
        if (_copilot == null) return;
        CopilotPaused = _copilot.IsPaused;
        CopilotHasProblem = _copilot.Status is Core.Copilot.CopilotStatus.Error or Core.Copilot.CopilotStatus.NeedsApiKey;
        CopilotStatus = _copilot.Status switch
        {
            Core.Copilot.CopilotStatus.Listening => $"Listening · {_copilot.Provider.DisplayName}",
            Core.Copilot.CopilotStatus.Analyzing => "Thinking…",
            Core.Copilot.CopilotStatus.Paused => "Assistant paused",
            Core.Copilot.CopilotStatus.NeedsApiKey => "Add an API key in Settings → Assistant to get live help.",
            Core.Copilot.CopilotStatus.Error => "Assistant error: " + (_copilot.StatusMessage ?? "unknown"),
            _ => "Assistant off",
        };
    }

    partial void OnBriefChanged(string value) => _copilot?.UpdateBrief(value);

    [RelayCommand]
    private void MarkHandled(InsightItem? item)
    {
        if (item == null || _copilot == null) return;
        _copilot.MarkHandled(item.Insight.Id);
    }

    [RelayCommand]
    private void Dismiss(InsightItem? item)
    {
        if (item == null || _copilot == null) return;
        _copilot.Dismiss(item.Insight.Id);
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_copilot == null) return;
        _copilot.SetPaused(!_copilot.IsPaused);
    }

    [RelayCommand]
    private void CopyReply(InsightItem? item)
    {
        var text = item?.Reply ?? item?.Detail;
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }
}
