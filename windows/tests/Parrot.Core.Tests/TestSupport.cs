// Parrot for Windows tests. Derived from Parrot (GPL-3.0) test harnesses (ProfileTest.swift).
using System.Net;
using System.Text;
using Parrot.Core.AI;
using Parrot.Core.Storage;

namespace Parrot.Core.Tests;

/// Temp data root, deleted on dispose.
public sealed class TempPaths : IAppPaths, IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "parrot-tests-" + Guid.NewGuid().ToString("N"));
    public TempPaths() => Directory.CreateDirectory(Root);
    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

/// Records every request (with its body read eagerly) and answers from a script. No network.
public sealed class FakeHandler : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? ContentType, string Body);

    private readonly Func<Captured, int, HttpResponseMessage> _respond;
    public List<Captured> Requests { get; } = new();

    public FakeHandler(Func<Captured, int, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHandler Json(params (HttpStatusCode Status, string Body)[] script) =>
        new((_, i) => Response(script[Math.Min(i, script.Length - 1)].Status, script[Math.Min(i, script.Length - 1)].Body));

    public static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        var captured = new Captured(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body);
        Requests.Add(captured);
        return _respond(captured, Requests.Count - 1);
    }
}

/// Scripted provider for engine tests.
public sealed class FakeProvider : IAnalysisProvider
{
    public bool IsConfigured { get; set; } = true;
    public string DisplayName => "fake";
    public List<AnalysisRequest> Requests { get; } = new();
    public Queue<AnalysisResult> Results { get; } = new();
    public Exception? Throw { get; set; }
    public string SummaryText { get; set; } = "Key points:\n- Budget approved [00:05]";
    public string CoachingText { get; set; } = "What went well:\n- Asked good questions [00:10]";
    public List<(string System, string User)> Completions { get; } = new();
    public string CompletionText { get; set; } = "";

    public Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken ct = default)
    {
        lock (Requests) Requests.Add(request);
        if (Throw != null) throw Throw;
        lock (Results)
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Empty);
    }

    public static AnalysisResult Empty => new(Array.Empty<InsightDraft>(), new Dictionary<string, int>(), null, null, Array.Empty<string>());

    public Task<string> SummarizeAsync(string transcript, IReadOnlyList<string> insightTitles, string instructions, string counterpart, CancellationToken ct = default)
    {
        if (Throw != null) throw Throw;
        return Task.FromResult(SummaryText);
    }

    public Task<string> CoachingReportAsync(string transcript, int talkPercentMe, string instructions, string counterpart, CancellationToken ct = default) =>
        Task.FromResult(CoachingText);

    public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct = default)
    {
        Completions.Add((system, user));
        return Task.FromResult(CompletionText);
    }
}

public static class TestWait
{
    /// Polls a condition (for work the code under test runs on the thread pool).
    public static async Task Until(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            await Task.Delay(20);
        }
    }
}
