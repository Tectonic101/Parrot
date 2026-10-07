// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/Insight.swift.
namespace Parrot.Core.Models;

/// A single piece of live call intelligence produced by the copilot.
public sealed class Insight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// Stable kind key from the active profile (e.g. "suggestion", "objection").
    public string KindKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public double CallTime { get; set; }
    /// KB document name the answer is grounded in, or "general knowledge".
    public string? Source { get; set; }
    /// For unresolved flags: one short line the user could say to address it.
    public string? Reply { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsHandled { get; set; }

    public string FormattedCallTime => TimeFormat.Stamp(CallTime);
}
