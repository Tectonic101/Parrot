// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/Meeting.swift.
using System.Globalization;

namespace Parrot.Core.Models;

public enum MeetingStatus
{
    Recording,
    Processing,
    Done,
    Failed,
}

public sealed class Meeting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public double Duration { get; set; }
    public string? SystemAudioPath { get; set; }
    public string? MicAudioPath { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Recording;
    public string? ErrorMessage { get; set; }
    /// AI-generated post-call report.
    public string? Summary { get; set; }
    /// AI coaching + follow-ups report.
    public string? Coaching { get; set; }
    /// User-assigned name for the other party, replaces "Them" in display.
    public string? ThemName { get; set; }
    public string Notes { get; set; } = "";
    public Guid? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public string Counterpart { get; set; } = "the other person";
    public string? Brief { get; set; }
    /// Profile kinds at record time, so the report renders even if the profile changes.
    public List<ProfileKind> SnapshotKinds { get; set; } = new();
    public List<TranscriptSegment> Segments { get; set; } = new();
    public List<Insight> Insights { get; set; } = new();
    /// Transcription engine that produced the transcript ("local:base", "groq"...).
    public string? TranscriptionEngine { get; set; }

    public Meeting() { }

    public static Meeting Create(DateTime date) => new() { Date = date, Title = DefaultTitle(date) };

    public static string DefaultTitle(DateTime date) =>
        "Meeting " + date.ToString("MMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);

    public IEnumerable<TranscriptSegment> SortedSegments => Segments.OrderBy(s => s.StartTime);
    public IEnumerable<Insight> SortedInsights => Insights.OrderBy(i => i.CallTime);

    public string DisplayName(Speaker speaker) =>
        speaker == Speaker.Me ? "Me" : (string.IsNullOrWhiteSpace(ThemName) ? "Them" : ThemName!);

    /// "[mm:ss] Speaker: text" lines — what every report prompt receives.
    /// Prompts always use the internal "Me"/"Them" tags (the prompts explain them).
    public string PromptTranscript =>
        string.Join("\n", SortedSegments.Select(s => $"[{s.FormattedTimestamp}] {s.Speaker.Label()}: {s.Text}"));

    /// Share of speaking time that was the user's, by seconds of speech; null without enough signal.
    public int? TalkPercentMe => TalkBalance.PercentMe(Segments);

    public string FormattedDuration => TimeFormat.Duration(Duration);
}

public static class TalkBalance
{
    /// Seconds-based talk share (not characters, so languages aren't miscounted).
    public static int? PercentMe(IEnumerable<TranscriptSegment> segments, double minimumSeconds = 5)
    {
        double me = 0, them = 0;
        foreach (var s in segments)
        {
            var d = Math.Max(0, s.EndTime - s.StartTime);
            if (d <= 0) d = s.Text.Length / 15.0;
            if (s.Speaker == Speaker.Me) me += d; else them += d;
        }
        var total = me + them;
        if (total < minimumSeconds) return null;
        return (int)Math.Round(me / total * 100);
    }
}
