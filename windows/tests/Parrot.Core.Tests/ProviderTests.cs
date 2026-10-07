// Parrot for Windows tests. Derived from Parrot (GPL-3.0), AnalysisProvider.swift request/response shapes.
using System.Net;
using System.Text.Json.Nodes;
using Parrot.Core.AI;
using Parrot.Core.Profiles;

namespace Parrot.Core.Tests;

public class ProviderTests
{
    private const string FakeKey = "test-key-not-real";

    private static AnalysisRequest Request()
    {
        var p = ProfilePresets.MakeDefault();
        return new AnalysisRequest
        {
            Transcript = "Them: Do you integrate with Salesforce?",
            Persona = p.Persona, Counterpart = p.Counterpart, Kinds = p.Kinds, Gauges = p.Gauges,
            KnownDocumentNames = new[] { "integrations.md" },
        };
    }

    private const string Payload =
        "{\"insights\":[{\"kind\":\"suggestion\",\"title\":\"Salesforce sync\",\"detail\":\"Native two-way sync.\",\"source\":\"integrations.md\",\"reply\":\"\",\"supersedes\":\"\"}],"
        + "\"sentiment\":{\"coach\":\"Answer it directly.\",\"score\":70,\"read\":\"curious\",\"wrapping_up\":false,\"next_step_agreed\":false},\"resolved\":[]}";

    private static string ClaudeResponse(string text, string stop = "end_turn") =>
        new JsonObject
        {
            ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["stop_reason"] = stop,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        }.ToJsonString();

    private static string ChatResponse(string content, string finish = "stop") =>
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["finish_reason"] = finish,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
            }),
        }.ToJsonString();

    [Fact]
    public async Task Anthropic_AnalyzeSendsStructuredRequestAndParses()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ClaudeResponse(Payload)));
        var provider = new AnthropicProvider(new HttpClient(handler), () => FakeKey);
        var result = await provider.AnalyzeAsync(Request());

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", req.Uri.ToString());
        Assert.Equal(FakeKey, req.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", req.Headers["anthropic-version"]);
        var body = JsonNode.Parse(req.Body)!;
        Assert.Equal("claude-haiku-4-5", body["model"]!.GetValue<string>());
        Assert.Equal(1024, body["max_tokens"]!.GetValue<int>());
        Assert.Equal("json_schema", body["output_config"]!["format"]!["type"]!.GetValue<string>());
        Assert.NotNull(body["output_config"]!["format"]!["schema"]!["properties"]!["insights"]);
        Assert.Contains("<transcript>", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("user", body["messages"]![0]!["role"]!.GetValue<string>());
        // Keys are sorted (grammar cache key stability).
        Assert.Equal(Prompts.SortedJson(body), req.Body);

        var insight = Assert.Single(result.Insights);
        Assert.Equal("Salesforce sync", insight.Title);
        Assert.Equal("integrations.md", insight.Source);
        Assert.Equal("Answer it directly.", result.Coach);
        Assert.Equal(70, result.Sentiment["score"]);
    }

    [Fact]
    public async Task Anthropic_MissingKeyIsFlagged()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ClaudeResponse(Payload)));
        var provider = new AnthropicProvider(new HttpClient(handler), () => null);
        Assert.False(provider.IsConfigured);
        var e = await Assert.ThrowsAsync<AnalysisException>(() => provider.AnalyzeAsync(Request()));
        Assert.True(e.IsMissingKey);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Anthropic_TruncatedResponseThrows()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ClaudeResponse("{\"insights\":[", "max_tokens")));
        var provider = new AnthropicProvider(new HttpClient(handler), () => FakeKey);
        await Assert.ThrowsAsync<AnalysisException>(() => provider.AnalyzeAsync(Request()));
    }

    [Fact]
    public async Task Anthropic_RetriesOnceOn529ThenSurfacesError()
    {
        var handler = FakeHandler.Json(
            (HttpStatusCode.ServiceUnavailable, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}"),
            (HttpStatusCode.ServiceUnavailable, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}"));
        var provider = new AnthropicProvider(new HttpClient(handler), () => FakeKey) { RetryDelay = TimeSpan.Zero };
        var e = await Assert.ThrowsAsync<AnalysisException>(() => provider.CompleteAsync("sys", "user", 100));
        Assert.Equal("Overloaded", e.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Anthropic_CompleteReturnsText()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ClaudeResponse("  Overview line.\n- Point [00:12]  ")));
        var provider = new AnthropicProvider(new HttpClient(handler), () => FakeKey, model: "claude-sonnet-4-5");
        var text = await provider.SummarizeAsync("[00:12] Them: hi", Array.Empty<string>(), "", "the prospect");
        Assert.Equal("Overview line.\n- Point [00:12]", text);
        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal("claude-sonnet-4-5", body["model"]!.GetValue<string>());
        Assert.Equal(1700, body["max_tokens"]!.GetValue<int>());
        Assert.Contains("post-call reports", body["system"]!.GetValue<string>());
        Assert.Null(body["output_config"]);
    }

    [Fact]
    public async Task OpenAI_UsesStrictSchemaAndMaxCompletionTokens()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ChatResponse(Payload)));
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), ProviderKind.OpenAI,
            ProviderCatalog.DefaultBaseUrl(ProviderKind.OpenAI), "gpt-4o-mini", () => FakeKey);
        var result = await provider.AnalyzeAsync(Request());

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", req.Uri.ToString());
        Assert.Equal("Bearer " + FakeKey, req.Headers["authorization"]);
        var body = JsonNode.Parse(req.Body)!;
        Assert.Equal(1024, body["max_completion_tokens"]!.GetValue<int>());
        Assert.Null(body["max_tokens"]);
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("Salesforce sync", Assert.Single(result.Insights).Title);
    }

    [Fact]
    public async Task OpenAICompatible_FallsBackToJsonObjectWhenSchemaRejected()
    {
        var handler = FakeHandler.Json(
            (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"response_format json_schema is not supported\"}}"),
            (HttpStatusCode.OK, ChatResponse("```json\n" + Payload + "\n```")));
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), ProviderKind.Groq,
            ProviderCatalog.DefaultBaseUrl(ProviderKind.Groq), "llama-3.3-70b-versatile", () => FakeKey);
        var result = await provider.AnalyzeAsync(Request());

        Assert.Equal(2, handler.Requests.Count);
        var second = JsonNode.Parse(handler.Requests[1].Body)!;
        Assert.Equal("json_object", second["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(1024, second["max_tokens"]!.GetValue<int>());
        Assert.Contains("Respond with ONLY a JSON object", second["messages"]![1]!["content"]!.GetValue<string>());
        Assert.Single(result.Insights);
    }

    [Fact]
    public async Task OpenAICompatible_TruncationAndEmptyAreErrors()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, ChatResponse("partial", "length")));
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), ProviderKind.Custom, "http://localhost:1234/v1", "local", () => null);
        Assert.True(provider.IsConfigured);
        var e = await Assert.ThrowsAsync<AnalysisException>(() => provider.CompleteAsync("s", "u", 50));
        Assert.Contains("truncated", e.Message);
        Assert.False(handler.Requests[0].Headers.ContainsKey("authorization"));
    }

    [Fact]
    public void OpenAICompatible_CloudNeedsKey()
    {
        var provider = new OpenAICompatibleProvider(new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException())),
            ProviderKind.Gemini, ProviderCatalog.DefaultBaseUrl(ProviderKind.Gemini), "gemini-2.5-flash", () => "");
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task Ollama_ReportsUseNativeChatWithSizedContext()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, "{\"message\":{\"role\":\"assistant\",\"content\":\"Report text\"},\"done\":true,\"done_reason\":\"stop\"}"));
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), ProviderKind.Ollama, "http://localhost:11434/v1", "llama3.2:3b", () => null);
        var text = await provider.CompleteAsync(new string('s', 30000), "user", 1700);
        Assert.Equal("Report text", text);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/api/chat", req.Uri.ToString());
        var body = JsonNode.Parse(req.Body)!;
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Equal(16384, body["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal(1700, body["options"]!["num_predict"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(100, 4096)]
    [InlineData(4097, 8192)]
    [InlineData(20000, 32768)]
    [InlineData(999999, 32768)]
    public void OllamaContext_PowerOfTwoBuckets(int estimate, int expected) =>
        Assert.Equal(expected, OpenAICompatibleProvider.OllamaContext(estimate));
}
