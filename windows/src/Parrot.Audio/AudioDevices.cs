// Parrot for Windows. Derived from Parrot (GPL-3.0).
using NAudio.CoreAudioApi;

namespace Parrot.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public static class AudioDevices
{
    public static List<AudioDeviceInfo> Microphones() => List(DataFlow.Capture, Role.Communications);
    public static List<AudioDeviceInfo> Outputs() => List(DataFlow.Render, Role.Multimedia);

    private static List<AudioDeviceInfo> List(DataFlow flow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try { defaultId = enumerator.GetDefaultAudioEndpoint(flow, role).ID; } catch (Exception) { }
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))
            .ToList();
    }

    internal static MMDevice Find(string? id, DataFlow flow)
    {
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(id))
        {
            try { return enumerator.GetDevice(id); }
            catch (Exception) { /* unplugged: fall back to the default */ }
        }
        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
        }
        catch (Exception)
        {
            throw new InvalidOperationException(flow == DataFlow.Capture
                ? "No microphone found. Connect one, or allow microphone access in Windows Settings → Privacy & security → Microphone."
                : "No audio output device found.");
        }
    }
}
