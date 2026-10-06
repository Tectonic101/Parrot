// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parrot.Core.Storage;

/// Atomic JSON read/write: write to a temp file, then replace, so a crash
/// mid-save can never leave a half-written store (the Mac's KB lesson:
/// a failed load followed by a save wipes the user's data).
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }
        File.Move(temp, path, overwrite: true);
    }
}
