// Parrot for Windows. Derived from Parrot (GPL-3.0), ClaudeAnalysisProvider in AnalysisProvider.swift.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Parrot.Core.AI;

/// Calls the Claude Messages API directly over HTTP (same wire shape as the Mac app).
public sealed class AnthropicProvider : IAnalysisProvider
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly string _baseUrl;
    public string Model { get; }
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public AnthropicProvider(HttpClient http, Func<string?> apiKey, string? model = null, string? baseUrl = null)
    {
        _http = http;
        _apiKey = apiKey;
        Model = string.IsNullOrWhiteSpace(model) ? ProviderCatalog.DefaultModel(ProviderKind.Anthropic) : model!;
        _baseUrl = (baseUrl ?? ProviderCatalog.DefaultBaseUrl(ProviderKind.Anthropic)).TrimEnd('/');
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey());
    public string DisplayName => $"{Model} · cloud";

    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 1024,
            ["system"] = Prompts.SystemPrompt(request.Persona, request.Kinds, request.Gauges, request.Counterpart),
            ["messages"] = Messages(Prompts.AnalysisUserContent(request)),
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = Prompts.Schema(request.Kinds, request.Gauges) },
            },
        };
        var response = await SendAsync(body, ct).ConfigureAwait(false);
        // Truncated structured output is unparseable half-JSON: throw so the engine retries.
        if (Str(response["stop_reason"]) == "max_tokens")
            throw new AnalysisException("Response truncated (hit max_tokens)");
        var parsed = Prompts.ParseAnalysisPayload(TextOf(response));
        return Prompts.Validate(parsed, request);
    }

    public Task<string> SummarizeAsync(string transcript, IReadOnlyList<string> insightTitles, string instructions,
                                       string counterpart, CancellationToken ct = default) =>
        CompleteAsync(Prompts.SummarySystemPrompt(counterpart),
                      Prompts.SummaryUserContent(transcript, insightTitles, instructions), 1700, ct);

    public Task<string> CoachingReportAsync(string transcript, int talkPercentMe, string instructions,
                                            string counterpart, CancellationToken ct = default) =>
        CompleteAsync(Prompts.CoachingSystemPrompt(counterpart),
                      Prompts.CoachingUserContent(transcript, talkPercentMe, instructions, counterpart), 1400, ct);

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = Messages(user),
        };
        var response = await SendAsync(body, ct).ConfigureAwait(false);
        return TextOf(response).Trim();
    }

    private static JsonArray Messages(string user) =>
        new(new JsonObject { ["role"] = "user", ["content"] = user });

    private async Task<JsonObject> SendAsync(JsonObject body, CancellationToken ct)
    {
        var key = _apiKey();
        if (string.IsNullOrEmpty(key))
            throw new AnalysisException("No Claude API key set. Add one in Settings → Assistant.", missingKey: true);
        var json = Prompts.SortedJson(body);
        var (status, text) = await HttpRetry.SendAsync(_http, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/messages")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return req;
        }, RetryDelay, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
            throw new AnalysisException(HttpRetry.ErrorMessage(text) ?? $"HTTP {(int)status}");
        return JsonNode.Parse(text) as JsonObject ?? throw new AnalysisException("No response body");
    }

    private static string TextOf(JsonObject response)
    {
        if (response["content"] is JsonArray blocks)
            foreach (var b in blocks.OfType<JsonObject>())
                if (Str(b["type"]) == "text" && Str(b["text"]) is { } t) return t;
        throw new AnalysisException("Empty model response");
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
