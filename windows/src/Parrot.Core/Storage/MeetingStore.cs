// Parrot for Windows. Derived from Parrot (GPL-3.0).
using Parrot.Core.Models;

namespace Parrot.Core.Storage;

/// One JSON file per meeting under &lt;root&gt;\meetings. Thread-safe.
public sealed class MeetingStore
{
    private readonly IAppPaths _paths;
    private readonly object _lock = new();

    public MeetingStore(IAppPaths paths) => _paths = paths;

    private string FileFor(Guid id) => Path.Combine(_paths.MeetingsDir(), id + ".json");

    public void Save(Meeting meeting)
    {
        lock (_lock) JsonFile.Write(FileFor(meeting.Id), meeting);
    }

    public Meeting? Load(Guid id)
    {
        lock (_lock)
        {
            try { return JsonFile.Read<Meeting>(FileFor(id)); }
            catch (Exception) { return null; }
        }
    }

    /// All meetings, newest first. Unreadable files are skipped, never deleted.
    public List<Meeting> LoadAll()
    {
        lock (_lock)
        {
            var list = new List<Meeting>();
            foreach (var file in Directory.EnumerateFiles(_paths.MeetingsDir(), "*.json"))
            {
                try
                {
                    var m = JsonFile.Read<Meeting>(file);
                    if (m != null) list.Add(m);
                }
                catch (Exception)
                {
                    // Corrupt file: leave it on disk for manual recovery.
                }
            }
            return list.OrderByDescending(m => m.Date).ToList();
        }
    }

    /// Deletes the meeting record and its audio folder.
    public void Delete(Guid id)
    {
        lock (_lock)
        {
            var file = FileFor(id);
            if (File.Exists(file)) File.Delete(file);
            var audio = Path.Combine(_paths.Root, "audio", id.ToString());
            if (Directory.Exists(audio)) Directory.Delete(audio, recursive: true);
        }
    }

    /// Meetings left in Recording state by a crash: mark them recovered/done so
    /// the transcript that was saved incrementally stays usable.
    public int RecoverInterrupted()
    {
        var n = 0;
        foreach (var m in LoadAll().Where(m => m.Status is MeetingStatus.Recording or MeetingStatus.Processing))
        {
            m.Status = MeetingStatus.Done;
            m.ErrorMessage = "Recovered after the app closed during recording.";
            if (m.Duration <= 0 && m.Segments.Count > 0) m.Duration = m.Segments.Max(s => s.EndTime);
            Save(m);
            n++;
        }
        return n;
    }
}
