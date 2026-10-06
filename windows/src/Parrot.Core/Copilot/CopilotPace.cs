// Parrot for Windows. Derived from Parrot (GPL-3.0), CopilotPace in CallAnalysisEngine.swift.
namespace Parrot.Core.Copilot;

/// How eagerly the copilot calls the model. Fast is the original always-on behavior;
/// Relaxed spaces requests out so free-tier rate limits survive a whole meeting.
public enum CopilotPace
{
    Fast,
    Balanced,
    Relaxed,
}

/// question = fast-track debounce for the other side's questions; idle = wait after
/// mid-flow speech; floor = minimum between calls; staleness = never let speech wait
/// longer; questionFloor = the shorter floor a "Them" question gets. Seconds.
public readonly record struct PaceTiming(double Question, double Idle, double Floor, double Staleness, double QuestionFloor);

public static class CopilotPaceExtensions
{
    public static PaceTiming Timing(this CopilotPace pace) => pace switch
    {
        CopilotPace.Fast => new(0.3, 8, 5, 15, 2),
        CopilotPace.Balanced => new(1, 15, 20, 45, 5),
        _ => new(3, 30, 60, 120, 15),
    };

    public static string Caption(this CopilotPace pace) => pace switch
    {
        CopilotPace.Fast => "Tips arrive within seconds. Most requests, highest cost.",
        CopilotPace.Balanced => "A couple of requests per minute.",
        _ => "Fewest requests — fits free-model limits. Tips can arrive up to a minute late.",
    };
}
