// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/TranscriptSegment.swift.
namespace Parrot.Core.Models;

/// One finalized line of the transcript. Times are seconds from the start of the call.
public sealed class TranscriptSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string Text { get; set; } = "";
    public Speaker Speaker { get; set; }

    public TranscriptSegment() { }

    public TranscriptSegment(double start, double end, string text, Speaker speaker)
    {
        StartTime = start;
        EndTime = end;
        Text = text;
        Speaker = speaker;
    }

    public string FormattedTimestamp => TimeFormat.Stamp(StartTime);
}

public static class TimeFormat
{
    /// "mm:ss" (minutes may exceed 59), the format report receipts cite.
    public static string Stamp(double seconds)
    {
        var t = Math.Max(0, (int)seconds);
        return $"{t / 60:00}:{t % 60:00}";
    }

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
