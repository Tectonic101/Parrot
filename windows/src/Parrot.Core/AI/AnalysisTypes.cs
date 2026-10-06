// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/AnalysisProvider.swift.
using Parrot.Core.Models;

namespace Parrot.Core.AI;

/// Raw insight returned by a provider; the engine attaches call timing.
public sealed record InsightDraft(
    string KindKey,
    string Title,
    string Detail,
    string? Source,
    string? Reply = null,
    /// The model's own dedup verdict: the EXACT already-shown title this overlaps, or null.
    string? Supersedes = null);

/// Everything a provider needs for one analysis pass.
public sealed class AnalysisRequest
{
    public string Transcript { get; init; } = "";
    public IReadOnlyList<string> KnownInsightTitles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<KBReference> References { get; init; } = Array.Empty<KBReference>();
    /// Standing rules (the profile's tone).
    public string Instructions { get; init; } = "";
    public string CallBrief { get; init; } = "";
    public bool AllowGeneralKnowledge { get; init; } = true;
    public IReadOnlyList<string> KnownDocumentNames { get; init; } = Array.Empty<string>();
    public string Persona { get; init; } = "";
    public string Counterpart { get; init; } = "the other person";
    public IReadOnlyList<ProfileKind> Kinds { get; init; } = Array.Empty<ProfileKind>();
    public IReadOnlyList<SentimentGauge> Gauges { get; init; } = Array.Empty<SentimentGauge>();
    public string PreviousCallContext { get; init; } = "";
}

/// Combined result from one analysis pass.
public sealed record AnalysisResult(
    IReadOnlyList<InsightDraft> Insights,
    IReadOnlyDictionary<string, int> Sentiment,
    string? Read,
    string? Coach,
    IReadOnlyList<string> Resolved);

public sealed class AnalysisException : Exception
{
    public bool IsMissingKey { get; }
    public AnalysisException(string message, bool missingKey = false) : base(message) => IsMissingKey = missingKey;
}

/// Backend that turns a transcript window into structured insights and writes reports.
public interface IAnalysisProvider
{
    bool IsConfigured { get; }
    /// "claude-haiku-4-5 · cloud" etc., for the UI.
    string DisplayName { get; }
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken ct = default);
    Task<string> SummarizeAsync(string transcript, IReadOnlyList<string> insightTitles, string instructions,
                                string counterpart, CancellationToken ct = default);
    Task<string> CoachingReportAsync(string transcript, int talkPercentMe, string instructions,
                                     string counterpart, CancellationToken ct = default);
    /// One plain-text answer for a system + user prompt (Ask Parrot).
    Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct = default);
}
