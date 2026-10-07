// Parrot for Windows. Derived from Parrot (GPL-3.0), Receipts.swift / ReportContentView.swift.
using System.Text.RegularExpressions;

namespace Parrot.Core.Reports;

public enum ReportLineKind
{
    Heading,
    Bullet,
    Text,
}

/// One displayable line of a report with its receipts ([12:34]) lifted out.
public sealed record ReportLine(ReportLineKind Kind, string Text, IReadOnlyList<double> Receipts);

public static class ReportFormatter
{
    private static readonly Regex Group = new(@"\s*[\[(]((?:\d{1,3}:\d{2}(?::\d{2})?)(?:\s*(?:,|;|–|—|-|and|&)\s*\d{1,3}:\d{2}(?::\d{2})?)*)[\])]", RegexOptions.Compiled);
    private static readonly Regex Stamp = new(@"(\d{1,3}):(\d{2})(?::(\d{2}))?", RegexOptions.Compiled);

    public static List<ReportLine> Parse(string? report)
    {
        var lines = new List<ReportLine>();
        if (string.IsNullOrWhiteSpace(report)) return lines;
        foreach (var raw in report.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // "Key points:" / "What went well:" section labels (also "Call snapshot: …" lead-ins stay text).
            var stripped = line.TrimStart('#', ' ').Replace("**", "");
            if (stripped.EndsWith(':') && !stripped.StartsWith('-') && stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 7)
            {
                lines.Add(new ReportLine(ReportLineKind.Heading, stripped[..^1], Array.Empty<double>()));
                continue;
            }
            var receipts = new List<double>();
            foreach (Match g in Group.Matches(stripped))
                foreach (Match s in Stamp.Matches(g.Groups[1].Value))
                    receipts.Add(ToSeconds(s));
            var text = Group.Replace(stripped, "").Trim();
            var isBullet = text.StartsWith("- ") || text.StartsWith("• ") || text.StartsWith("* ");
            if (isBullet) text = text[2..].Trim();
            lines.Add(new ReportLine(isBullet ? ReportLineKind.Bullet : ReportLineKind.Text, text, receipts));
        }
        return lines;
    }

    private static double ToSeconds(Match s)
    {
        var a = int.Parse(s.Groups[1].Value);
        var b = int.Parse(s.Groups[2].Value);
        return s.Groups[3].Success ? a * 3600 + b * 60 + int.Parse(s.Groups[3].Value) : a * 60 + b;
    }
}
