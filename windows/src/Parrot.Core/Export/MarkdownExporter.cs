// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/ExportService.swift.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Parrot.Core.Models;

namespace Parrot.Core.Export;

/// Obsidian/Notion-friendly Markdown: YAML front matter, notes, report (receipts kept
/// as `12:34`), live cards, and the transcript. `parrot_id` lets a re-export overwrite the same note.
public static class MarkdownExporter
{
    public static string Export(Meeting meeting, CallProfile? profile = null)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append($"title: {Yaml(meeting.Title)}\n");
        sb.Append($"date: {meeting.Date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}\n");
        sb.Append($"duration_minutes: {(int)Math.Round(meeting.Duration / 60)}\n");
        if (!string.IsNullOrWhiteSpace(meeting.ThemName)) sb.Append($"people: [{Yaml(meeting.ThemName!)}]\n");
        var profileName = profile?.Name ?? meeting.ProfileName;
        if (!string.IsNullOrEmpty(profileName)) sb.Append($"profile: {Yaml(profileName!)}\n");
        sb.Append("source: parrot\n");
        sb.Append($"parrot_id: {meeting.Id.ToString().ToUpperInvariant()}\n");
        sb.Append("---\n\n");
        sb.Append($"# {meeting.Title}\n\n");

        if (!string.IsNullOrWhiteSpace(meeting.Notes)) sb.Append($"## My notes\n\n{meeting.Notes.Trim()}\n\n");
        if (!string.IsNullOrWhiteSpace(meeting.Summary)) sb.Append($"## Summary\n\n{MarkdownReport(meeting.Summary!)}\n\n");
        if (!string.IsNullOrWhiteSpace(meeting.Coaching)) sb.Append($"## Coaching\n\n{MarkdownReport(meeting.Coaching!)}\n\n");

        var insights = meeting.SortedInsights.ToList();
        if (insights.Count > 0)
        {
            sb.Append("## Assistant cards\n\n");
            foreach (var i in insights)
            {
                var kind = profile?.KindFor(i.KindKey) ?? meeting.SnapshotKinds.FirstOrDefault(k => k.Key == i.KindKey);
                var label = kind?.Label ?? KindLabelFallback(i.KindKey);
                var state = kind?.IsPinned == true ? (i.IsHandled ? " (handled)" : " (unresolved)") : "";
                sb.Append($"- `{i.FormattedCallTime}` **{label}:** {i.Title}{state}");
                if (!string.IsNullOrWhiteSpace(i.Detail)) sb.Append($" — {i.Detail}");
                if (!string.IsNullOrWhiteSpace(i.Reply)) sb.Append($" _Say:_ {i.Reply}");
                if (!string.IsNullOrWhiteSpace(i.Source)) sb.Append($" _(source: {i.Source})_");
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("## Transcript\n\n");
        foreach (var s in meeting.SortedSegments)
            sb.Append($"`{s.FormattedTimestamp}` **{meeting.DisplayName(s.Speaker)}:** {s.Text}  \n");
        return sb.ToString();
    }

    private static readonly Regex Receipt = new(@"\s*[\[(]((?:\d{1,3}:\d{2}(?::\d{2})?)(?:\s*(?:,|;|–|—|-|and|&)\s*\d{1,3}:\d{2}(?::\d{2})?)*)[\])]", RegexOptions.Compiled);
    private static readonly Regex Stamp = new(@"\d{1,3}:\d{2}(?::\d{2})?", RegexOptions.Compiled);

    /// Report text with [12:34] receipts as inline code and section labels ("Key points:") as ### headings.
    public static string MarkdownReport(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith(':') && trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 7 && !trimmed.StartsWith('-'))
                return "### " + trimmed[..^1];
            var matches = Receipt.Matches(line);
            if (matches.Count == 0) return line;
            var stamps = matches.SelectMany(m => Stamp.Matches(m.Groups[1].Value).Select(s => s.Value)).ToList();
            var stripped = Receipt.Replace(line, "").TrimEnd();
            return stripped + " " + string.Join(" ", stamps.Select(s => $"`{s}`"));
        });
        return string.Join("\n", lines).Trim();
    }

    /// "2026-09-25 14-30 Acme renewal.md": sorts by date, safe on every file system.
    public static string FileName(Meeting meeting)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', ':', '\\', '?', '%', '*', '|', '"', '<', '>', '\n', '\r' }).ToHashSet();
        var title = new string(meeting.Title.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (title.Length > 80) title = title[..80];
        if (title.Length == 0) title = "Meeting";
        return $"{meeting.Date.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)} {title}.md";
    }

    public static string KindLabelFallback(string key)
    {
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        var label = string.Join(" ", words);
        return label.Length == 0 ? "Insight" : label;
    }

    private static string Yaml(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
}
