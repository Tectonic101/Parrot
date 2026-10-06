// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Views (sidebar rows, transcript lines, copilot cards).
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Parrot.Core.Export;
using Parrot.Core.Models;
using Parrot.Core.Reports;

namespace Parrot.App.ViewModels;

public sealed partial class MeetingListItem : ObservableObject
{
    public Guid Id { get; }
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _subtitle;
    [ObservableProperty] private bool _isProcessing;

    public MeetingListItem(Meeting m)
    {
        Id = m.Id;
        _title = m.Title;
        _subtitle = Describe(m);
        _isProcessing = m.Status is MeetingStatus.Processing or MeetingStatus.Recording;
    }

    public void Update(Meeting m)
    {
        Title = m.Title;
        Subtitle = Describe(m);
        IsProcessing = m.Status is MeetingStatus.Processing or MeetingStatus.Recording;
    }

    private static string Describe(Meeting m) =>
        m.Date.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture) + " · " + m.FormattedDuration;
}

public sealed class SegmentItem
{
    public TranscriptSegment Segment { get; }
    public string Speaker { get; }
    public string Time => Segment.FormattedTimestamp;
    public string Text => Segment.Text;
    public bool IsMe => Segment.Speaker == Core.Models.Speaker.Me;

    public SegmentItem(TranscriptSegment segment, string speakerName)
    {
        Segment = segment;
        Speaker = speakerName;
    }
}

public sealed partial class InsightItem : ObservableObject
{
    public Insight Insight { get; }
    public string KindLabel { get; }
    public string ColorHex { get; }
    public bool IsPinnedKind { get; }
    public string Title => Insight.Title;
    public string Detail => Insight.Detail;
    public string? Reply => Insight.Reply;
    public string? Source => Insight.Source;
    public string Time => Insight.FormattedCallTime;
    [ObservableProperty] private bool _isHandled;

    /// Pinned kinds stay at the top until handled (the Mac's "unresolved" zone).
    public bool IsOpenFlag => IsPinnedKind && !IsHandled;
    public string StateLabel => IsPinnedKind ? (IsHandled ? "Handled" : "Unresolved") : "";

    public InsightItem(Insight insight, ProfileKind? kind)
    {
        Insight = insight;
        KindLabel = kind?.Label ?? MarkdownExporter.KindLabelFallback(insight.KindKey);
        ColorHex = kind?.ColorHex ?? "5F6470";
        IsPinnedKind = kind?.IsPinned ?? false;
        _isHandled = insight.IsHandled;
    }

    partial void OnIsHandledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOpenFlag));
        OnPropertyChanged(nameof(StateLabel));
    }
}

public sealed class ReportLineItem
{
    public ReportLine Line { get; }
    public string Text => Line.Text;
    public bool IsHeading => Line.Kind == ReportLineKind.Heading;
    public bool IsBullet => Line.Kind == ReportLineKind.Bullet;
    public bool IsText => Line.Kind == ReportLineKind.Text;
    public IReadOnlyList<string> Receipts { get; }

    public ReportLineItem(ReportLine line)
    {
        Line = line;
        Receipts = line.Receipts.Select(TimeFormat.Stamp).ToList();
    }
}

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
