// Parrot for Windows. Derived from Parrot (GPL-3.0).
namespace Parrot.Core.Transcription;

/// ggml Whisper models for whisper.cpp, downloaded on first use from the
/// whisper.cpp Hugging Face repository.
public static class WhisperModels
{
    public sealed record Info(string Name, string Label, long ApproxBytes);

    public static readonly IReadOnlyList<Info> All = new[]
    {
        new Info("tiny", "Tiny — fastest, lowest accuracy (75 MB)", 77_691_713),
        new Info("base", "Base — good default for most PCs (142 MB)", 147_951_465),
        new Info("base.en", "Base, English only (142 MB)", 147_964_211),
        new Info("small", "Small — better accuracy, needs a fast CPU (466 MB)", 487_601_967),
        new Info("small.en", "Small, English only (466 MB)", 487_614_201),
        new Info("medium", "Medium — high accuracy, slow without a GPU (1.5 GB)", 1_533_763_059),
        new Info("large-v3-turbo-q5_0", "Large v3 Turbo (quantized) — best accuracy per MB (547 MB)", 574_041_195),
        new Info("large-v3-turbo", "Large v3 Turbo — best accuracy (1.6 GB)", 1_624_555_275),
    };

    public static string FileName(string name) => $"ggml-{name}.bin";

    public static Uri DownloadUri(string name) =>
        new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName(name)}");

    public static bool IsKnown(string name) => All.Any(m => m.Name == name);
}

/// Downloads a model file with progress into the models folder. Writes to a .part
/// file and renames on success, so an interrupted download is never mistaken for a model.
public sealed class ModelDownloader
{
    private readonly HttpClient _http;
    private readonly string _modelsDir;

    public ModelDownloader(HttpClient http, string modelsDir)
    {
        _http = http;
        _modelsDir = modelsDir;
    }

    public string PathFor(string name) => Path.Combine(_modelsDir, WhisperModels.FileName(name));

    public bool IsDownloaded(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
    }

    /// Returns the local path. progress reports 0…1 (or -1 when the size is unknown).
    public async Task<string> EnsureAsync(string name, IProgress<double>? progress = null, CancellationToken ct = default,
                                          Uri? sourceOverride = null)
    {
        var path = PathFor(name);
        if (IsDownloaded(name)) { progress?.Report(1); return path; }
        Directory.CreateDirectory(_modelsDir);
        var part = path + ".part";
        var uri = sourceOverride ?? WhisperModels.DownloadUri(name);
        using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new TranscriptionException($"Model download failed: HTTP {(int)response.StatusCode}");
            var total = response.Content.Headers.ContentLength
                        ?? WhisperModels.All.FirstOrDefault(m => m.Name == name)?.ApproxBytes ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (var target = File.Create(part))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                var lastReport = DateTime.MinValue;
                int n;
                while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    read += n;
                    if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                    {
                        progress.Report(total > 0 ? Math.Min(1.0, read / (double)total) : -1);
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
        }
        File.Move(part, path, overwrite: true);
        progress?.Report(1);
        return path;
    }
}
