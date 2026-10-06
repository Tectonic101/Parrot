// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/CallProfile.swift.
namespace Parrot.Core.Models;

public sealed class ProfileKind
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "5F6470";
    public string Icon { get; set; } = "";
    public string TriggerDescription { get; set; } = "";
    public bool IsPinned { get; set; }
    public int Priority { get; set; }
}

public sealed class SentimentGauge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string LowLabel { get; set; } = "";
    public string HighLabel { get; set; } = "";
    public string ColorHex { get; set; } = "5F6470";
}

/// A call type: persona, the insight kinds the copilot may emit, and gauges.
public sealed class CallProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
    public string Persona { get; set; } = "";
    /// Standing rules from the user ("tone"); sent as instructions.
    public string Tone { get; set; } = "";
    /// What the AI calls the other party ("the prospect").
    public string Counterpart { get; set; } = "the other person";
    public bool AllowGeneralKnowledge { get; set; } = true;
    public int PresetVersion { get; set; }
    public bool IsUserModified { get; set; }
    public List<ProfileKind> Kinds { get; set; } = new();
    public List<SentimentGauge> Gauges { get; set; } = new();

    public ProfileKind? KindFor(string key) => Kinds.FirstOrDefault(k => k.Key == key);
}
