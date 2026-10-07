// Parrot for Windows tests. Derived from Parrot (GPL-3.0).
using Parrot.Core.Models;
using Parrot.Core.Profiles;
using Parrot.Core.Storage;

namespace Parrot.Core.Tests;

public class StorageTests
{
    private static Meeting Sample()
    {
        var m = Meeting.Create(new DateTime(2026, 9, 25, 14, 30, 0));
        m.Duration = 1834;
        m.ThemName = "Dana";
        m.Notes = "Follow up on SSO";
        m.Summary = "Overview.\nKey points:\n- Budget approved [00:05]";
        m.ProfileId = ProfilePresets.SalesId;
        m.SnapshotKinds = ProfilePresets.MakeDefault().Kinds;
        m.Status = MeetingStatus.Done;
        m.Segments.Add(new TranscriptSegment(5, 8, "Budget is approved.", Speaker.Them));
        m.Segments.Add(new TranscriptSegment(1, 3, "Hi Dana!", Speaker.Me));
        m.Insights.Add(new Insight { KindKey = "blocker", Title = "Needs SSO", Detail = "SSO required", CallTime = 12, Source = "security.md", IsHandled = true });
        return m;
    }

    [Fact]
    public void MeetingStore_RoundTrip()
    {
        using var paths = new TempPaths();
        var store = new MeetingStore(paths);
        var m = Sample();
        store.Save(m);

        var loaded = store.Load(m.Id)!;
        Assert.Equal(m.Title, loaded.Title);
        Assert.Equal(m.Date, loaded.Date);
        Assert.Equal(1834, loaded.Duration);
        Assert.Equal("Dana", loaded.ThemName);
        Assert.Equal(MeetingStatus.Done, loaded.Status);
        Assert.Equal(ProfilePresets.SalesId, loaded.ProfileId);
        Assert.Equal(5, loaded.SnapshotKinds.Count);
        Assert.Equal(new[] { "Hi Dana!", "Budget is approved." }, loaded.SortedSegments.Select(s => s.Text));
        Assert.Equal(Speaker.Them, loaded.Segments[0].Speaker);
        var i = Assert.Single(loaded.Insights);
        Assert.True(i.IsHandled);
        Assert.Equal("security.md", i.Source);

        // Enums are stored as strings (readable, stable across reorderings).
        var json = File.ReadAllText(Path.Combine(paths.Root, "meetings", m.Id + ".json"));
        Assert.Contains("\"status\": \"done\"", json);
        Assert.Contains("\"speaker\": \"them\"", json);
    }

    [Fact]
    public void MeetingStore_LoadAllNewestFirstSkipsCorruptAndDeletes()
    {
        using var paths = new TempPaths();
        var store = new MeetingStore(paths);
        var older = Sample();
        var newer = Meeting.Create(new DateTime(2026, 10, 1, 9, 0, 0));
        store.Save(older);
        store.Save(newer);
        File.WriteAllText(Path.Combine(paths.MeetingsDir(), Guid.NewGuid() + ".json"), "{ not json");

        Assert.Equal(new[] { newer.Id, older.Id }, store.LoadAll().Select(m => m.Id));

        Directory.CreateDirectory(paths.AudioDir(older.Id));
        File.WriteAllText(Path.Combine(paths.AudioDir(older.Id), "me.wav"), "x");
        store.Delete(older.Id);
        Assert.Null(store.Load(older.Id));
        Assert.False(Directory.Exists(Path.Combine(paths.Root, "audio", older.Id.ToString())));
    }

    [Fact]
    public void MeetingStore_RecoversInterruptedRecordings()
    {
        using var paths = new TempPaths();
        var store = new MeetingStore(paths);
        var m = Meeting.Create(DateTime.Now);
        m.Status = MeetingStatus.Recording;
        m.Segments.Add(new TranscriptSegment(0, 42, "hello", Speaker.Me));
        store.Save(m);
        Assert.Equal(1, store.RecoverInterrupted());
        var r = store.Load(m.Id)!;
        Assert.Equal(MeetingStatus.Done, r.Status);
        Assert.Equal(42, r.Duration);
        Assert.Equal(0, store.RecoverInterrupted());
    }

    [Fact]
    public void Settings_RoundTripAndDefaults()
    {
        using var paths = new TempPaths();
        var store = new SettingsStore(paths);
        var s = store.Load();
        Assert.Equal(TranscriptionEngineKind.Local, s.TranscriptionEngine);
        s.WhisperModel = "small";
        s.CopilotEnabled = true;
        store.Save(s);
        var again = new SettingsStore(paths).Load();
        Assert.Equal("small", again.WhisperModel);
        Assert.True(again.CopilotEnabled);
    }

    [Fact]
    public void Profiles_SeedPresetsAndKeepUserEdits()
    {
        using var paths = new TempPaths();
        var store = new ProfileStore(paths);
        Assert.Equal(7, store.Profiles.Count);
        Assert.Equal(ProfilePresets.DefaultProfileId, store.Profiles[0].Id);

        var sales = store.Get(ProfilePresets.SalesId);
        sales.Tone = "Keep it short.";
        sales.IsUserModified = true;
        store.Upsert(sales);
        var custom = new CallProfile { Name = "Board meeting", SortOrder = 99 };
        store.Upsert(custom);

        var reloaded = new ProfileStore(paths);
        Assert.Equal(8, reloaded.Profiles.Count);
        Assert.Equal("Keep it short.", reloaded.Get(ProfilePresets.SalesId).Tone);
        Assert.True(reloaded.Delete(custom.Id));
        Assert.Equal(ProfilePresets.DefaultProfileId, reloaded.Get(Guid.NewGuid()).Id);
    }

    [Fact]
    public void InMemorySecretStore_SetAndClear()
    {
        var s = new InMemorySecretStore();
        Assert.True(s.Set(SecretAccounts.Anthropic, "abc"));
        Assert.Equal("abc", s.Get(SecretAccounts.Anthropic));
        s.Set(SecretAccounts.Anthropic, "");
        Assert.Null(s.Get(SecretAccounts.Anthropic));
    }

    [Fact]
    public void TalkBalance_BySeconds()
    {
        var segs = new List<TranscriptSegment>
        {
            new(0, 30, "long me", Speaker.Me),
            new(30, 40, "short them", Speaker.Them),
        };
        Assert.Equal(75, TalkBalance.PercentMe(segs));
        Assert.Null(TalkBalance.PercentMe(new[] { new TranscriptSegment(0, 2, "hi", Speaker.Me) }));
    }
}
