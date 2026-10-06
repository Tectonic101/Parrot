// Parrot for Windows tests. Derived from Parrot (GPL-3.0), RecordingManager.swift / --echo-replay checks.
using Parrot.Core.Models;
using Parrot.Core.Recording;
using Parrot.Core.Storage;
using Parrot.Core.Transcription;

namespace Parrot.Core.Tests;

public class RecordingSessionTests
{
    private sealed class FakeSource(Speaker speaker) : IAudioTrackSource
    {
        public Speaker Speaker { get; } = speaker;
        public string DeviceName => Speaker == Speaker.Me ? "Fake mic" : "Fake speakers";
        public event Action<float[]>? SamplesAvailable;
#pragma warning disable CS0067 // never fails in tests
        public event Action<string>? Failed;
#pragma warning restore CS0067
        public bool Started, Stopped, Disposed;
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Dispose() => Disposed = true;
        public void Push(float[] samples) => SamplesAvailable?.Invoke(samples);
    }

    /// Returns a fixed line per speaker, judged by the chunk's loudness (Me = quiet tone, Them = loud tone).
    private sealed class FakeTranscriber : ITranscriber
    {
        public string Name => "fake";
        public bool IsCloud => true;
        public int Calls;
        public Task<IReadOnlyList<TranscribedPiece>> TranscribeAsync(float[] samples, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            var loud = TranscriptFilters.MeanAbs(samples) > 0.1f;
            var dur = samples.Length / 16000.0;
            IReadOnlyList<TranscribedPiece> r = new[] { new TranscribedPiece(loud ? "Can you share the pricing sheet?" : "Sure, sending it now.", 0, dur) };
            return Task.FromResult(r);
        }
        public void Dispose() { }
    }

    private static float[] Tone(double seconds, float amp) =>
        Enumerable.Range(0, (int)(16000 * seconds)).Select(i => (float)(amp * Math.Sin(2 * Math.PI * 220 * i / 16000))).ToArray();

    [Fact]
    public async Task Session_SegmentsTranscribesSavesAndWritesWavs()
    {
        using var paths = new TempPaths();
        var store = new MeetingStore(paths);
        var them = new FakeSource(Speaker.Them);
        var me = new FakeSource(Speaker.Me);
        var meeting = Meeting.Create(DateTime.Now);
        var transcriber = new FakeTranscriber();
        var session = new RecordingSession(meeting, new IAudioTrackSource[] { them, me }, transcriber, null, store, paths);
        var added = new List<TranscriptSegment>();
        session.SegmentAdded += s => { lock (added) added.Add(s); };
        session.Start();
        Assert.True(them.Started && me.Started);

        // Them: 1 s silence, 1.5 s question, 1 s pause. Me: 3 s silence, 1 s answer (then drained at stop).
        them.Push(new float[16000]);
        them.Push(Tone(1.5, 0.4f));
        them.Push(new float[16000]);
        me.Push(new float[48000]);
        me.Push(Tone(1.0, 0.05f));

        await TestWait.Until(() => { lock (added) return added.Count >= 1; });
        var result = await session.StopAsync();
        await session.DisposeAsync();

        Assert.True(them.Stopped && me.Disposed);
        Assert.Equal(2, result.Segments.Count);
        var q = result.Segments[0];
        Assert.Equal(Speaker.Them, q.Speaker);
        Assert.Equal("Can you share the pricing sheet?", q.Text);
        Assert.InRange(q.StartTime, 0.99, 1.01);
        Assert.InRange(q.EndTime, 2.5, 2.7);
        var a = result.Segments[1];
        Assert.Equal(Speaker.Me, a.Speaker);
        Assert.InRange(a.StartTime, 2.99, 3.01);

        Assert.Equal(MeetingStatus.Processing, result.Status);
        Assert.Equal("fake", result.TranscriptionEngine);
        var saved = store.Load(meeting.Id)!;
        Assert.Equal(2, saved.Segments.Count);
        Assert.Equal(56000, WavEncoder.ReadPcm16Mono(result.SystemAudioPath!).Length);
        Assert.Equal(64000, WavEncoder.ReadPcm16Mono(result.MicAudioPath!).Length);
    }

    [Fact]
    public async Task EchoSuppression_DropsMeLinesRepeatingThem()
    {
        using var paths = new TempPaths();
        var meeting = Meeting.Create(DateTime.Now);
        await using var session = new RecordingSession(meeting, Array.Empty<IAudioTrackSource>(), new FakeTranscriber(), null,
                                                       new MeetingStore(paths), paths, echoSuppression: true);
        var removed = new List<TranscriptSegment>();
        session.SegmentRemoved += removed.Add;

        // Me line arrives first (mic transcribed faster), the true Them line second: the echo is removed.
        session.AddSegment(new TranscriptSegment(10.2, 12, "We can offer twenty percent discount annual plans", Speaker.Me));
        session.AddSegment(new TranscriptSegment(10, 12, "We can offer a twenty percent discount on annual plans.", Speaker.Them));
        Assert.Single(removed);
        // A later echo is dropped on arrival; a real reply is kept.
        session.AddSegment(new TranscriptSegment(11, 13, "twenty percent discount on annual plans", Speaker.Me));
        session.AddSegment(new TranscriptSegment(13, 15, "That works, let's sign this week.", Speaker.Me));
        Assert.Equal(new[] { Speaker.Them, Speaker.Me }, meeting.Segments.Select(s => s.Speaker));

        // Far apart in time: not an echo.
        Assert.False(RecordingSession.IsEcho(new TranscriptSegment(60, 62, "twenty percent discount on annual plans", Speaker.Me),
                                             meeting.Segments[0]));
    }

    [Fact]
    public async Task EchoSuppression_OffKeepsEverything()
    {
        using var paths = new TempPaths();
        var meeting = Meeting.Create(DateTime.Now);
        await using var session = new RecordingSession(meeting, Array.Empty<IAudioTrackSource>(), new FakeTranscriber(), null,
                                                       new MeetingStore(paths), paths, echoSuppression: false);
        session.AddSegment(new TranscriptSegment(10, 12, "Twenty percent discount on annual plans.", Speaker.Them));
        session.AddSegment(new TranscriptSegment(10, 12, "Twenty percent discount on annual plans.", Speaker.Me));
        Assert.Equal(2, meeting.Segments.Count);
    }
}
