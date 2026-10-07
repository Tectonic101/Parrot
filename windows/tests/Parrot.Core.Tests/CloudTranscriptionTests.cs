// Parrot for Windows tests. Derived from Parrot (GPL-3.0), CloudTranscription.swift.
using System.Net;
using Parrot.Core.Transcription;

namespace Parrot.Core.Tests;

public class CloudTranscriptionTests
{
    private static float[] OneSecond() => new float[16000];

    [Fact]
    public async Task WhisperApi_PostsMultipartWav()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, "{\"text\":\" We can start in March. \"}"));
        var t = WhisperApiTranscriber.Groq(new HttpClient(handler), () => "test-key-not-real", "en");
        var pieces = await t.TranscribeAsync(OneSecond());

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/audio/transcriptions", req.Uri.ToString());
        Assert.Equal("Bearer test-key-not-real", req.Headers["authorization"]);
        Assert.StartsWith("multipart/form-data", req.ContentType);
        Assert.Contains("name=model", req.Body);
        Assert.Contains("whisper-large-v3-turbo", req.Body);
        Assert.Contains("name=language", req.Body);
        Assert.Contains("filename=chunk.wav", req.Body);
        Assert.Contains("RIFF", req.Body);

        var p = Assert.Single(pieces);
        Assert.Equal("We can start in March.", p.Text);
        Assert.Equal(0, p.Start);
        Assert.Equal(1.0, p.End);
    }

    [Fact]
    public void WhisperApi_AutoLanguageOmitsField()
    {
        var t = WhisperApiTranscriber.OpenAI(new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException())), () => "k", "auto");
        Assert.Equal(new[] { ("model", "whisper-1"), ("response_format", "json") }, t.Fields());
    }

    [Fact]
    public async Task WhisperApi_ErrorsAndMissingKey()
    {
        var handler = FakeHandler.Json((HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API key\"}}"));
        var t = WhisperApiTranscriber.OpenAI(new HttpClient(handler), () => "bad", null);
        var e = await Assert.ThrowsAsync<TranscriptionException>(() => t.TranscribeAsync(OneSecond()));
        Assert.Contains("401", e.Message);

        var none = WhisperApiTranscriber.OpenAI(new HttpClient(handler), () => null, null);
        await Assert.ThrowsAsync<TranscriptionException>(() => none.TranscribeAsync(OneSecond()));
    }

    [Fact]
    public async Task Deepgram_ParsesTranscript()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK,
            "{\"results\":{\"channels\":[{\"alternatives\":[{\"transcript\":\"Hello from Deepgram.\",\"confidence\":0.98}]}]}}"));
        var t = new DeepgramTranscriber(new HttpClient(handler), () => "dg-test", "de");
        var pieces = await t.TranscribeAsync(OneSecond());
        Assert.Equal("Hello from Deepgram.", Assert.Single(pieces).Text);
        var req = handler.Requests[0];
        Assert.Equal("Token dg-test", req.Headers["authorization"]);
        Assert.Contains("model=nova-3", req.Uri.Query);
        Assert.Contains("language=de", req.Uri.Query);
        Assert.Equal("audio/wav", req.ContentType);
        Assert.Contains("language=multi", new DeepgramTranscriber(new HttpClient(handler), () => "x", "auto").RequestUri());
    }

    [Fact]
    public async Task Deepgram_EmptyTranscriptIsNoPiece()
    {
        var handler = FakeHandler.Json((HttpStatusCode.OK, "{\"results\":{\"channels\":[{\"alternatives\":[{\"transcript\":\"\"}]}]}}"));
        var t = new DeepgramTranscriber(new HttpClient(handler), () => "dg-test", null);
        Assert.Empty(await t.TranscribeAsync(OneSecond()));
    }

    [Fact]
    public async Task ModelDownloader_WritesFileAndReportsProgress()
    {
        var payload = new byte[1_200_000];
        new Random(1).NextBytes(payload);
        var handler = new FakeHandler((req, _) =>
        {
            Assert.Equal(WhisperModels.DownloadUri("tiny"), req.Uri);
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            r.Content.Headers.ContentLength = payload.Length;
            return r;
        });
        using var paths = new TempPaths();
        var dl = new ModelDownloader(new HttpClient(handler), paths.Root);
        var reports = new List<double>();
        var path = await dl.EnsureAsync("tiny", new SyncProgress(reports.Add));
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(1.0, reports[^1]);

        // Second call is a no-op.
        await dl.EnsureAsync("tiny", null);
        Assert.Single(handler.Requests);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
