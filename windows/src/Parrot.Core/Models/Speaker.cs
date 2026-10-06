// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/TranscriptSegment.swift.
namespace Parrot.Core.Models;

/// Which track a line came from. "Me" is the microphone (the user);
/// "Them" is system audio (everyone else on the call).
public enum Speaker
{
    Me,
    Them,
}

public static class SpeakerExtensions
{
    public static string Label(this Speaker speaker) => speaker == Speaker.Me ? "Me" : "Them";
}
