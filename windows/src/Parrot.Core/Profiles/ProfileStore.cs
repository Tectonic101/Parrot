// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/ProfileStore.swift.
using Parrot.Core.Models;
using Parrot.Core.Storage;

namespace Parrot.Core.Profiles;

/// Persists call profiles. Seeds built-ins on first run and refreshes built-ins
/// whose preset version is older — unless the user modified them.
public sealed class ProfileStore
{
    private readonly IAppPaths _paths;
    public List<CallProfile> Profiles { get; private set; } = new();

    public ProfileStore(IAppPaths paths)
    {
        _paths = paths;
        Load();
    }

    private void Load()
    {
        List<CallProfile>? stored = null;
        try { stored = JsonFile.Read<List<CallProfile>>(_paths.ProfilesFile()); }
        catch (Exception) { /* fall through to presets; never overwrite unreadable file below */ }

        var presets = ProfilePresets.All();
        if (stored == null || stored.Count == 0)
        {
            Profiles = presets;
            if (!File.Exists(_paths.ProfilesFile())) Save();
            return;
        }

        foreach (var preset in presets)
        {
            var existing = stored.FirstOrDefault(p => p.Id == preset.Id);
            if (existing == null)
            {
                stored.Add(preset);
            }
            else if (existing.IsBuiltIn && !existing.IsUserModified && existing.PresetVersion < preset.PresetVersion)
            {
                preset.Tone = existing.Tone; // user-owned
                stored[stored.IndexOf(existing)] = preset;
            }
        }
        Profiles = stored.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList();
        Save();
    }

    public void Save() => JsonFile.Write(_paths.ProfilesFile(), Profiles);

    public CallProfile Get(Guid id) =>
        Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault() ?? ProfilePresets.MakeDefault();

    public void Upsert(CallProfile profile)
    {
        var i = Profiles.FindIndex(p => p.Id == profile.Id);
        if (i >= 0) Profiles[i] = profile; else Profiles.Add(profile);
        Save();
    }

    public bool Delete(Guid id)
    {
        var p = Profiles.FirstOrDefault(x => x.Id == id);
        if (p == null || p.IsBuiltIn) return false;
        Profiles.Remove(p);
        Save();
        return true;
    }
}
