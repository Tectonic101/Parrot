// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/KnowledgeBaseService.swift.
using System.Text;

namespace Parrot.Core.Knowledge;

/// Chunking and lexical retrieval (BM25), ported from the Mac knowledge base.
public static class TextTools
{
    /// Splits text into ~900-character chunks along paragraph boundaries. Markdown headings
    /// are glued onto the paragraph that follows them and "---" separators are dropped, so a
    /// chunk is never a bare heading. A paragraph over the cap is split on its lines and every
    /// piece carries the section heading.
    public static List<string> ChunkText(string text, int cap = 900)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && p != "---")
            .ToList();

        var result = new List<string>();
        var current = new StringBuilder();
        string? heading = null;
        string? section = null;

        void Flush()
        {
            if (current.Length > 0) result.Add(current.ToString());
            current.Clear();
        }

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.StartsWith('#'))
            {
                var nl = paragraph.IndexOf('\n');
                section = nl < 0 ? paragraph : paragraph[..nl];
                if (section == paragraph)
                {
                    heading = paragraph;
                    continue;
                }
            }
            var piece = paragraph;
            if (heading != null)
            {
                piece = heading + "\n" + piece;
                heading = null;
            }
            if (piece.Length > cap)
            {
                Flush();
                var prefix = section != null ? section + "\n" : "";
                var part = new StringBuilder(prefix);
                foreach (var line in piece.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line == section) continue;
                    if (part.Length + line.Length > cap && part.ToString() != prefix)
                    {
                        result.Add(part.ToString().Trim());
                        part.Clear().Append(prefix);
                    }
                    part.Append(line).Append('\n');
                }
                if (part.ToString() != prefix) result.Add(part.ToString().Trim());
                continue;
            }
            if (current.Length + piece.Length > cap && current.Length > 0) Flush();
            if (current.Length > 0) current.Append("\n\n");
            current.Append(piece);
        }
        Flush();
        return result.Where(c => c.Length >= 40).ToList();
    }

    private static readonly HashSet<string> LexicalStopWords = new()
    {
        "the", "a", "an", "and", "or", "is", "are", "was", "were", "be", "been", "this", "that",
        "these", "those", "what", "which", "how", "much", "many", "do", "does", "did", "i", "you",
        "we", "they", "it", "he", "she", "me", "us", "them", "my", "your", "our", "their", "its",
        "for", "of", "to", "in", "on", "at", "by", "from", "with", "as", "so", "if", "but", "not",
        "no", "any", "some", "can", "could", "will", "would", "should", "shall", "may", "might",
        "about", "into", "over", "under", "than", "then", "there", "here", "when", "where", "who",
        "whom", "whose", "why", "okay", "ok", "yes", "just", "also", "very", "really", "one",
        "thing", "things",
    };

    /// Splits on anything that isn't a letter or digit, lowercased.
    public static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// Lowercased alphanumeric tokens (≥2 chars) minus function words; alphabetic tokens
    /// longer than five keep their first five letters (cheap stem). Numbers stay whole.
    public static List<string> LexicalTokens(string text) =>
        Words(text)
            .Where(t => t.Length >= 2 && !LexicalStopWords.Contains(t))
            .Select(t => t.Length > 5 && t.All(char.IsLetter) ? t[..5] : t)
            .ToList();

    /// Document indices ordered by BM25 (k1 = 1.2, b = 0.75); non-matching documents are left out.
    public static List<int> Bm25Order(IReadOnlyList<string> query, IReadOnlyList<IReadOnlyList<string>> documents)
    {
        if (query.Count == 0 || documents.Count == 0) return new List<int>();
        double n = documents.Count;
        var averageLength = documents.Sum(d => (double)d.Count) / n;
        if (averageLength <= 0) return new List<int>();
        var df = new Dictionary<string, int>();
        foreach (var doc in documents)
            foreach (var term in doc.Distinct())
                df[term] = df.GetValueOrDefault(term) + 1;

        const double k1 = 1.2, b = 0.75;
        var scored = new List<(int Index, double Score)>();
        for (var i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var counts = new Dictionary<string, int>();
            foreach (var t in doc) counts[t] = counts.GetValueOrDefault(t) + 1;
            double score = 0;
            foreach (var term in query)
            {
                if (!counts.TryGetValue(term, out var count)) continue;
                var d = (double)df.GetValueOrDefault(term);
                var idf = Math.Log(1 + (n - d + 0.5) / (d + 0.5));
                score += idf * count * (k1 + 1) / (count + k1 * (1 - b + b * doc.Count / averageLength));
            }
            if (score > 0) scored.Add((i, score));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Index).Select(s => s.Index).ToList();
    }
}
