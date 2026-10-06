// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/CloudTranscription.swift.
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Parrot.Core.Transcription;

/// OpenAI-compatible /audio/transcriptions upload, one utterance WAV at a time.
/// Covers OpenAI (whisper-1) and Groq (whisper-large-v3-turbo). Opt-in: audio leaves the PC.
public sealed class WhisperApiTranscriber : ITranscriber
{
    public const string OpenAIBaseUrl = "https://api.openai.com/v1";
    public const string GroqBaseUrl = "https://api.groq.com/openai/v1";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly Func<string?> _apiKey;
    private readonly string? _language;

    public string Name { get; }
    public bool IsCloud => true;

    public WhisperApiTranscriber(HttpClient http, string name, string baseUrl, string model, Func<string?> apiKey, string? language)
    {
        _http = http;
        Name = name;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
        _language = language is null or "auto" ? null : language;
    }

    public static WhisperApiTranscriber OpenAI(HttpClient http, Func<string?> key, string? language) =>
        new(http, "OpenAI Whisper", OpenAIBaseUrl, "whisper-1", key, language);

    public static WhisperApiTranscriber Groq(HttpClient http, Func<string?> key, string? language) =>
        new(http, "Groq Whisper", GroqBaseUrl, "whisper-large-v3-turbo", key, language);

    /// Multipart form fields, in order. No glossary prompt: sent as `prompt`, Whisper
    /// echoed it into quiet chunks on the Mac ("Glossary, Uygar") and this path has no guard.
    public IReadOnlyList<(string Name, string Value)> Fields()
    {
        var fields = new List<(string, string)> { ("model", _model), ("response_format", "json") };
        if (_language != null) fields.Add(("language", _language));
        return fields;
    }

    public async Task<IReadOnlyList<TranscribedPiece>> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        var key = _apiKey();
        if (string.IsNullOrEmpty(key)) throw new TranscriptionException($"No API key set for {Name}.");
        using var form = new MultipartFormDataContent("parrot-" + Guid.NewGuid().ToString("N"));
        foreach (var (name, value) in Fields()) form.Add(new StringContent(value), name);
        var file = new ByteArrayContent(WavEncoder.Encode(samples));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "chunk.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TranscriptionException($"{Name} HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");
        var text = JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? "";
        var duration = samples.Length / (double)WavEncoder.SampleRate;
        text = TranscriptFilters.Cleaned(text);
        return text.Length == 0 ? Array.Empty<TranscribedPiece>() : new[] { new TranscribedPiece(text, 0, duration) };
    }

    internal static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    public void Dispose() { }
}

/// Deepgram Nova-3 pre-recorded endpoint, one utterance at a time. (The Mac streams
/// over a websocket for word-by-word latency; v1 on Windows keeps the same
/// utterance cadence as the other engines.)
public sealed class DeepgramTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly string? _language;
    public string Name => "Deepgram Nova-3";
    public bool IsCloud => true;

    public DeepgramTranscriber(HttpClient http, Func<string?> apiKey, string? language)
    {
        _http = http;
        _apiKey = apiKey;
        _language = language is null or "auto" ? null : language;
    }

    public string RequestUri()
    {
        // "multi" = Nova-3 multilingual; a specific code pins it.
        var lang = _language ?? "multi";
        return $"https://api.deepgram.com/v1/listen?model=nova-3&smart_format=true&punctuate=true&language={Uri.EscapeDataString(lang)}";
    }

    public async Task<IReadOnlyList<TranscribedPiece>> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        var key = _apiKey();
        if (string.IsNullOrEmpty(key)) throw new TranscriptionException("No API key set for Deepgram.");
        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUri())
        {
            Content = new ByteArrayContent(WavEncoder.Encode(samples)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TranscriptionException($"Deepgram HTTP {(int)response.StatusCode}: {WhisperApiTranscriber.Truncate(body, 300)}");
        var text = JsonNode.Parse(body)?["results"]?["channels"]?[0]?["alternatives"]?[0]?["transcript"]?.GetValue<string>() ?? "";
        text = text.Trim();
        var duration = samples.Length / (double)WavEncoder.SampleRate;
        return text.Length == 0 ? Array.Empty<TranscribedPiece>() : new[] { new TranscribedPiece(text, 0, duration) };
    }

    public void Dispose() { }
}
