// Parrot for Windows. Derived from Parrot (GPL-3.0), CallAnalysisEngine.swift (Heuristics section).
using Parrot.Core.Knowledge;
using Parrot.Core.Models;

namespace Parrot.Core.Copilot;

/// Pure, network-free helpers the copilot uses to decide when to call the model
/// and which cards to keep.
public static class Heuristics
{
    private static readonly string[] QuestionOpeners =
    {
        "how much", "how many", "how do", "how does", "how long", "how soon",
        "can you", "could you", "can we", "could we", "can i", "could i",
        "what about", "what is", "what's", "what if", "what do", "what would",
        "do you", "would you", "will you", "did you", "are you", "have you",
        "is there", "are there", "is it", "does it", "will it",
        "when can", "when do", "when will", "where do", "who is", "who's",
        "why ", "tell me about",
    };

    /// Cheap detector that fast-tracks analysis when someone asks something.
    public static bool LooksLikeQuestion(string text)
    {
        if (text.Contains('?')) return true;
        var lowered = text.ToLowerInvariant();
        return QuestionOpeners.Any(lowered.Contains);
    }

    /// A question worth the fast lane: reads like a question AND carries at least two
    /// content words. "Really?", "How are you?" do not; "Do you take cards?" does.
    public static bool IsSubstantiveQuestion(string text) =>
        LooksLikeQuestion(text) && SignificantTokens(text).Count >= 2;

    /// The newest thing the other side asked in a window, if anything.
    public static string? LatestQuestion(IEnumerable<(string Text, Speaker Speaker)> window) =>
        window.LastOrDefault(w => w.Speaker == Speaker.Them && LooksLikeQuestion(w.Text)).Text;

    private static readonly HashSet<string> StopWords = new()
    {
        "the", "and", "for", "you", "your", "they", "their", "them",
        "what", "whats", "how", "does", "still", "with", "about",
        "from", "that", "this", "are", "isnt", "not", "have", "has",
        "prospect", "prospects", "asked", "asking", "asks", "whether", "said", "user",
    };

    public static HashSet<string> SignificantTokens(string s) =>
        new(TextTools.Words(s).Where(t => t.Length > 2 && !StopWords.Contains(t)));

    /// Cheap "same issue, different words": significant-word overlap over the smaller set.
    public static bool IsNearDuplicate(string a, string b, double threshold = 0.6)
    {
        var ta = SignificantTokens(a);
        var tb = SignificantTokens(b);
        if (ta.Count == 0 || tb.Count == 0) return false;
        var overlap = ta.Intersect(tb).Count();
        return overlap / (double)Math.Min(ta.Count, tb.Count) >= threshold;
    }

    /// True when any pair of significant tokens shares a ≥4-character prefix
    /// ("banking"/"bank", "pricing"/"price").
    public static bool SharesTopicStem(string a, string b)
    {
        var ta = SignificantTokens(a);
        var tb = SignificantTokens(b);
        return ta.Any(wa => tb.Any(wb =>
        {
            var n = Math.Min(4, Math.Min(wa.Length, wb.Length));
            return n >= 4 && string.CompareOrdinal(wa, 0, wb, 0, n) == 0;
        }));
    }

    /// A model "supersedes" claim holds only if it cites a real open card that shares a topic stem.
    public static bool VerdictCorroborated(string supersedes, string draftText, IEnumerable<(string Title, string Text)> openCards)
    {
        var claimed = supersedes.ToLowerInvariant();
        var cited = openCards.FirstOrDefault(c => c.Title.ToLowerInvariant() == claimed);
        if (cited.Title == null) return false;
        return SharesTopicStem(draftText, cited.Text);
    }

    /// How many trailing segments fall inside the live context window, floored at minCount
    /// and capped at maxCount.
    public static int WindowSuffixCount(IReadOnlyList<double> times, double seconds, int minCount = 10, int maxCount = 200)
    {
        if (times.Count == 0) return 0;
        var cutoff = times[^1] - seconds;
        var count = 0;
        for (var i = times.Count - 1; i >= 0; i--)
        {
            if (times[i] < cutoff) break;
            count++;
        }
        return Math.Min(Math.Max(count, Math.Min(minCount, times.Count)), maxCount);
    }

    /// One transcript line as the copilot sees it.
    public static string PromptLine(string text, Speaker speaker) => $"{speaker.Label()}: {text}";
}
