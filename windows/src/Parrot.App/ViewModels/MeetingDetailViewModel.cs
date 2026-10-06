// Parrot for Windows. Derived from Parrot (GPL-3.0), MeetingDetailView.swift and ReportContentView.swift.
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Parrot.App.Services;
using Parrot.Core.Diagnostics;
using Parrot.Core.Export;
using Parrot.Core.Models;
using Parrot.Core.Reports;

namespace Parrot.App.ViewModels;

public sealed partial class MeetingDetailViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<Meeting> _changed;
    private readonly Action<Guid> _deleted;
    public Meeting Meeting { get; private set; }

    public ObservableCollection<ReportLineItem> SummaryLines { get; } = new();
    public ObservableCollection<ReportLineItem> CoachingLines { get; } = new();
    public ObservableCollection<SegmentItem> Segments { get; } = new();
    public ObservableCollection<InsightItem> Insights { get; } = new();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _themName = "";
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _talkBalance;
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string? _statusMessage;

    public MeetingDetailViewModel(AppServices services, Meeting meeting, Action<Meeting> changed, Action<Guid> deleted)
    {
        _services = services;
        _changed = changed;
        _deleted = deleted;
        Meeting = meeting;
        Load(meeting);
    }

    public void Load(Meeting meeting)
    {
        Meeting = meeting;
        _title = meeting.Title;
        OnPropertyChanged(nameof(Title));
        _notes = meeting.Notes;
        OnPropertyChanged(nameof(Notes));
        _themName = meeting.ThemName ?? "";
        OnPropertyChanged(nameof(ThemName));
        Subtitle = $"{meeting.Date:dddd, MMMM d, yyyy h:mm tt} · {meeting.FormattedDuration}"
                   + (string.IsNullOrEmpty(meeting.ProfileName) ? "" : $" · {meeting.ProfileName}")
                   + (string.IsNullOrEmpty(meeting.TranscriptionEngine) ? "" : $" · {meeting.TranscriptionEngine}");
        ErrorMessage = meeting.ErrorMessage;
        IsGenerating = meeting.Status == MeetingStatus.Processing;
        var talk = meeting.TalkPercentMe;
        TalkBalance = talk is { } t ? $"You spoke {t}% · {meeting.DisplayName(Speaker.Them)} {100 - t}%" : null;

        SummaryLines.Clear();
        foreach (var l in ReportFormatter.Parse(meeting.Summary)) SummaryLines.Add(new ReportLineItem(l));
        CoachingLines.Clear();
        foreach (var l in ReportFormatter.Parse(meeting.Coaching)) CoachingLines.Add(new ReportLineItem(l));
        HasReport = SummaryLines.Count > 0 || CoachingLines.Count > 0;

        Segments.Clear();
        foreach (var s in meeting.SortedSegments) Segments.Add(new SegmentItem(s, meeting.DisplayName(s.Speaker)));
        var profile = meeting.ProfileId is { } pid ? _services.Profiles.Profiles.FirstOrDefault(p => p.Id == pid) : null;
        Insights.Clear();
        foreach (var i in meeting.SortedInsights)
            Insights.Add(new InsightItem(i, profile?.KindFor(i.KindKey) ?? meeting.SnapshotKinds.FirstOrDefault(k => k.Key == i.KindKey)));
    }

    private void Save()
    {
        _services.Meetings.Save(Meeting);
        _changed(Meeting);
    }

    partial void OnTitleChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Meeting.Title = value.Trim();
        Save();
    }

    partial void OnNotesChanged(string value)
    {
        Meeting.Notes = value;
        _services.Meetings.Save(Meeting);
    }

    partial void OnThemNameChanged(string value)
    {
        Meeting.ThemName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Save();
        Segments.Clear();
        foreach (var s in Meeting.SortedSegments) Segments.Add(new SegmentItem(s, Meeting.DisplayName(s.Speaker)));
    }

    [RelayCommand]
    private async Task RegenerateReportAsync()
    {
        if (IsGenerating) return;
        IsGenerating = true;
        ErrorMessage = null;
        try
        {
            var profile = Meeting.ProfileId is { } id ? _services.Profiles.Get(id) : null;
            var generator = new ReportGenerator(_services.ReportsProvider(), _services.Meetings);
            await generator.GenerateAsync(Meeting, profile);
        }
        catch (Exception e)
        {
            Log.Error("Report failed", e);
            Meeting.ErrorMessage = e.Message;
        }
        Load(Meeting);
        _changed(Meeting);
    }

    [RelayCommand]
    private void ExportMarkdown()
    {
        var dialog = new SaveFileDialog
        {
            FileName = MarkdownExporter.FileName(Meeting),
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;
        var profile = Meeting.ProfileId is { } id ? _services.Profiles.Profiles.FirstOrDefault(p => p.Id == id) : null;
        File.WriteAllText(dialog.FileName, MarkdownExporter.Export(Meeting, profile));
        StatusMessage = "Exported to " + dialog.FileName;
    }

    [RelayCommand]
    private void CopyTranscript()
    {
        var text = string.Join(Environment.NewLine,
            Meeting.SortedSegments.Select(s => $"[{s.FormattedTimestamp}] {Meeting.DisplayName(s.Speaker)}: {s.Text}"));
        if (text.Length > 0) Clipboard.SetText(text);
        StatusMessage = "Transcript copied";
    }

    [RelayCommand]
    private void CopyReport()
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { Meeting.Summary, Meeting.Coaching }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (text.Length > 0) Clipboard.SetText(text);
        StatusMessage = "Report copied";
    }

    [RelayCommand]
    private void OpenAudioFolder()
    {
        var path = Meeting.SystemAudioPath ?? Meeting.MicAudioPath;
        var dir = path != null ? Path.GetDirectoryName(path) : null;
        if (dir != null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void Delete()
    {
        var answer = MessageBox.Show($"Delete \"{Meeting.Title}\" and its audio? This can't be undone.", "Delete meeting",
                                     MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        _services.Meetings.Delete(Meeting.Id);
        _deleted(Meeting.Id);
    }
}
