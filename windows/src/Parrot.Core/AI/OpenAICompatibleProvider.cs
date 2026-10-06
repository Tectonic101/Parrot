// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/OpenAICompatibleProvider.swift.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Parrot.Core.AI;

/// Talks to any OpenAI-compatible chat-completions server: OpenAI, Groq, Gemini's
/// OpenAI endpoint, Ollama on this PC, or a custom server. Prompts, schema and
/// validation are shared with the Claude provider — only the transport differs.
public sealed class OpenAICompatibleProvider : IAnalysisProvider
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    public ProviderKind Kind { get; }
    public string BaseUrl { get; }
    public string Model { get; }
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public OpenAICompatibleProvider(HttpClient http, ProviderKind kind, string baseUrl, string model, Func<string?> apiKey)
    {
        _http = http;
        Kind = kind;
        BaseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        Model = (model ?? "").Trim();
        _apiKey = apiKey;
    }

    /// Ollama and custom servers may run without a key; the clouds need one.
    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && Model.Length > 0
        && (Kind is ProviderKind.Ollama or ProviderKind.Custom || !string.IsNullOrEmpty(_apiKey()));

    public string DisplayName => Kind switch
    {
        ProviderKind.Ollama => $"{Model} · on this PC",
        ProviderKind.Custom => $"{Model} · your server",
        _ => $"{Model} · cloud",
    };

    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        var sys = Prompts.SystemPrompt(request.Persona, request.Kinds, request.Gauges, request.Counterpart);
        var user = Prompts.AnalysisUserContent(request);
        var schema = Prompts.Schema(request.Kinds, request.Gauges);
        var text = await StructuredChatAsync(sys, user, schema, 1024, ct).ConfigureAwait(false);
        return Prompts.Validate(Prompts.ParseAnalysisPayload(text), request);
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
        EnsureConfigured();
        if (Kind == ProviderKind.Ollama)
            return (await SendOllamaNativeAsync(system, user, maxTokens, ct).ConfigureAwait(false)).Trim();
        var body = ChatBody(system, user, maxTokens);
        return (await SendAsync(body, ct).ConfigureAwait(false)).Trim();
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new AnalysisException("Assistant model not set up. Check Settings → Assistant.",
                                        missingKey: Kind is not (ProviderKind.Ollama or ProviderKind.Custom));
    }

    internal JsonObject ChatBody(string system, string user, int maxTokens)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        // OpenAI's newer models reject "max_tokens"; everything else expects it.
        body[Kind == ProviderKind.OpenAI ? "max_completion_tokens" : "max_tokens"] = maxTokens;
        return body;
    }

    /// Tries strict json_schema first; falls back once to json_object with the
    /// schema inlined in the prompt when the server rejects strict schemas.
    private async Task<string> StructuredChatAsync(string system, string user, JsonObject schema, int maxTokens, CancellationToken ct)
    {
        var strict = ChatBody(system, user, maxTokens);
        strict["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = "analysis", ["strict"] = true, ["schema"] = schema.DeepClone() },
        };
        try
        {
            return await SendAsync(strict, ct).ConfigureAwait(false);
        }
        catch (AnalysisException e) when (e.Message.Contains("HTTP 4") || e.Message.Contains("response_format", StringComparison.OrdinalIgnoreCase)
                                          || e.Message.Contains("schema", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = ChatBody(system, user + "\n\n---\n\nRespond with ONLY a JSON object matching exactly this JSON Schema (no prose, no code fences):\n"
                                            + schema.ToJsonString(), maxTokens);
            fallback["response_format"] = new JsonObject { ["type"] = "json_object" };
            return await SendAsync(fallback, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> SendAsync(JsonObject body, CancellationToken ct)
    {
        var json = body.ToJsonString();
        var key = _apiKey();
        var (status, text) = await HttpRetry.SendAsync(_http, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return req;
        }, RetryDelay, ct).ConfigureAwait(false);

        if (status != HttpStatusCode.OK)
        {
            var msg = HttpRetry.ErrorMessage(text);
            throw new AnalysisException(msg != null ? $"HTTP {(int)status}: {msg}" : $"HTTP {(int)status}");
        }
        var root = JsonNode.Parse(text);
        var choice = root?["choices"]?[0];
        if (Str(choice?["finish_reason"]) == "length")
            throw new AnalysisException("Response truncated (hit max_tokens)");
        var content = Str(choice?["message"]?["content"]);
        if (string.IsNullOrEmpty(content)) throw new AnalysisException("Empty model response");
        return Prompts.StripCodeFence(content);
    }

    /// Ollama's OpenAI endpoint silently truncates prompts to the default context;
    /// the native /api/chat endpoint honors num_ctx, so report calls go there with the
    /// context sized to the prompt (power-of-two buckets so the model isn't reloaded).
    private async Task<string> SendOllamaNativeAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var estimated = (system.Length + user.Length) / 3 + maxTokens + 512;
        var numCtx = OllamaContext(estimated);
        var body = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            ["options"] = new JsonObject { ["num_ctx"] = numCtx, ["num_predict"] = maxTokens, ["temperature"] = 0.2 },
        };
        var root = BaseUrl.EndsWith("/v1") ? BaseUrl[..^3] : BaseUrl;
        var json = body.ToJsonString();
        var (status, text) = await HttpRetry.SendAsync(_http, () => new HttpRequestMessage(HttpMethod.Post, root + "/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }, RetryDelay, ct, retryOn429: false).ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
            throw new AnalysisException(HttpRetry.ErrorMessage(text) ?? $"HTTP {(int)status}");
        var node = JsonNode.Parse(text);
        if (Str(node?["done_reason"]) == "length") throw new AnalysisException("Response truncated (hit max_tokens)");
        var content = Str(node?["message"]?["content"]);
        if (string.IsNullOrEmpty(content)) throw new AnalysisException("Empty model response");
        return Prompts.StripCodeFence(content);
    }

    public static int OllamaContext(int estimatedTokens)
    {
        var n = 4096;
        while (n < estimatedTokens && n < 32768) n *= 2;
        return n;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
