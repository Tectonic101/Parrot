// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/RecordingManager.swift
// and the live loop in TranscriptionEngine.swift.
using System.Threading.Channels;
using Parrot.Core.Copilot;
using Parrot.Core.Models;
using Parrot.Core.Storage;
using Parrot.Core.Transcription;

namespace Parrot.Core.Recording;

/// A source of one audio track, already converted to 16 kHz mono float (-1…1).
/// Parrot.Audio implements it with WASAPI (loopback for "Them", microphone for "Me").
public interface IAudioTrackSource : IDisposable
{
    Speaker Speaker { get; }
    string DeviceName { get; }
    /// Raised from the capture thread with new 16 kHz mono samples.
    event Action<float[]>? SamplesAvailable;
    /// Raised once if capture fails (device unplugged, access denied).
    event Action<string>? Failed;
    void Start();
    void Stop();
}

/// One recording: two tracks → WAV files + utterance segmentation → transcription →
/// transcript lines (saved incrementally) → copilot. UI-agnostic and testable with fake
/// sources and a fake transcriber.
public sealed class RecordingSession : IAsyncDisposable
{
    private sealed class Track
    {
        public required IAudioTrackSource Source;
        public required WavFileWriter Writer;
        public readonly List<float> Buffer = new();
        /// Samples dropped or cut so far: Buffer[0] sits at this absolute sample offset.
        public long Consumed;
        public readonly object Lock = new();
        public float Level;
    }

    private sealed record Utterance(Speaker Speaker, float[] Samples, double Start);

    private readonly Meeting _meeting;
    private readonly ITranscriber _transcriber;
    private readonly CopilotEngine? _copilot;
    private readonly MeetingStore _store;
    private readonly bool _echoSuppression;
    private readonly List<Track> _tracks = new();
    private readonly Channel<Utterance> _queue = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly object _meetingLock = new();
    private Task? _worker;
    private Task? _poller;
    private DateTime _startedAt;
    private DateTime _lastSave = DateTime.MinValue;
    private int _pendingUtterances;

    public Meeting Meeting => _meeting;
    public bool IsRunning { get; private set; }
    public double Elapsed => IsRunning ? (DateTime.UtcNow - _startedAt).TotalSeconds : _meeting.Duration;
    /// Utterances waiting for (or in) transcription — a backlog indicator for slow CPUs.
    public int Backlog => Volatile.Read(ref _pendingUtterances);

    public event Action<TranscriptSegment>? SegmentAdded;
    public event Action<TranscriptSegment>? SegmentRemoved;
    public event Action<string>? Error;

    public RecordingSession(Meeting meeting, IEnumerable<IAudioTrackSource> sources, ITranscriber transcriber,
                            CopilotEngine? copilot, MeetingStore store, IAppPaths paths, bool echoSuppression = true)
    {
        _meeting = meeting;
        _transcriber = transcriber;
        _copilot = copilot;
        _store = store;
        _echoSuppression = echoSuppression;
        var audioDir = paths.AudioDir(meeting.Id);
        foreach (var source in sources)
        {
            var path = Path.Combine(audioDir, source.Speaker == Speaker.Me ? "me.wav" : "them.wav");
            var track = new Track { Source = source, Writer = new WavFileWriter(path) };
            if (source.Speaker == Speaker.Me) meeting.MicAudioPath = path; else meeting.SystemAudioPath = path;
            source.SamplesAvailable += samples => OnSamples(track, samples);
            source.Failed += message => Error?.Invoke($"{source.DeviceName}: {message}");
            _tracks.Add(track);
        }
        meeting.TranscriptionEngine = transcriber.Name;
    }

    public float LevelOf(Speaker speaker) => _tracks.FirstOrDefault(t => t.Source.Speaker == speaker)?.Level ?? 0;

    public void Start()
    {
        _startedAt = DateTime.UtcNow;
        _meeting.Status = MeetingStatus.Recording;
        _store.Save(_meeting);
        IsRunning = true;
        _worker = Task.Run(WorkerLoopAsync);
        _poller = Task.Run(PollLoopAsync);
        foreach (var t in _tracks)
        {
            try { t.Source.Start(); }
            catch (Exception e) { Error?.Invoke($"Could not start {t.Source.DeviceName}: {e.Message}"); }
        }
    }

    private void OnSamples(Track track, float[] samples)
    {
        if (samples.Length == 0) return;
        lock (track.Lock)
        {
            track.Buffer.AddRange(samples);
            track.Writer.Write(samples);
            // Smoothed level for the UI meters.
            var peak = 0f;
            foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
            track.Level = Math.Max(peak, track.Level * 0.85f);
        }
    }

    private async Task PollLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(250, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            foreach (var t in _tracks) Cut(t, draining: false);
        }
    }

    /// Applies the segmenter to a track's buffer, queueing every complete utterance.
    private void Cut(Track track, bool draining)
    {
        while (true)
        {
            Utterance? utterance = null;
            lock (track.Lock)
            {
                track.Level *= 0.9f;
                if (track.Buffer.Count == 0) return;
                var floor = Segmenter.AdaptiveFloor(track.Buffer);
                var cut = Segmenter.NextCut(track.Buffer, draining, floor);
                if (cut.DropLeading > 0)
                {
                    track.Buffer.RemoveRange(0, cut.DropLeading);
                    track.Consumed += cut.DropLeading;
                }
                if (cut.Take is not { } take || take <= 0)
                {
                    if (cut.DropLeading == 0 || track.Buffer.Count == 0) return;
                    continue;
                }
                take = Math.Min(take, track.Buffer.Count);
                var samples = track.Buffer.GetRange(0, take).ToArray();
                var start = track.Consumed / (double)WavEncoder.SampleRate;
                track.Buffer.RemoveRange(0, take);
                track.Consumed += take;
                utterance = new Utterance(track.Source.Speaker, samples, start);
            }
            Interlocked.Increment(ref _pendingUtterances);
            _queue.Writer.TryWrite(utterance);
        }
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var u in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await TranscribeAsync(u).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Error?.Invoke("Transcription failed: " + e.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingUtterances);
            }
        }
    }

    private async Task TranscribeAsync(Utterance u)
    {
        var energy = TranscriptFilters.MeanAbs(u.Samples);
        var input = _transcriber.IsCloud ? u.Samples : TranscriptFilters.NormalizedForDecode(u.Samples);
        var pieces = await _transcriber.TranscribeAsync(input).ConfigureAwait(false);
        var duration = u.Samples.Length / (double)WavEncoder.SampleRate;
        foreach (var piece in pieces)
        {
            var text = TranscriptFilters.Cleaned(piece.Text);
            if (TranscriptFilters.IsLikelyHallucination(text, energy)) continue;
            var start = u.Start + Math.Clamp(piece.Start, 0, duration);
            var end = u.Start + Math.Clamp(piece.End <= piece.Start ? duration : piece.End, 0, duration);
            AddSegment(new TranscriptSegment(start, Math.Max(end, start), text, u.Speaker));
        }
    }

    /// Adds a finalized line. With echo suppression, a "Me" line that repeats a nearby
    /// "Them" line (speakers bleeding into the mic) is dropped — "Them" is the true source.
    internal void AddSegment(TranscriptSegment segment)
    {
        var removed = new List<TranscriptSegment>();
        lock (_meetingLock)
        {
            if (_echoSuppression)
            {
                if (segment.Speaker == Speaker.Me &&
                    _meeting.Segments.Any(s => s.Speaker == Speaker.Them && IsEcho(segment, s)))
                    return;
                if (segment.Speaker == Speaker.Them)
                {
                    removed = _meeting.Segments.Where(s => s.Speaker == Speaker.Me && IsEcho(s, segment)).ToList();
                    foreach (var r in removed) _meeting.Segments.Remove(r);
                }
            }
            _meeting.Segments.Add(segment);
            if ((DateTime.UtcNow - _lastSave).TotalSeconds > 5)
            {
                _meeting.Duration = Elapsed;
                _store.Save(_meeting);
                _lastSave = DateTime.UtcNow;
            }
        }
        foreach (var r in removed) SegmentRemoved?.Invoke(r);
        SegmentAdded?.Invoke(segment);
        _copilot?.Ingest(segment.Text, segment.StartTime, segment.Speaker, segment.EndTime - segment.StartTime);
    }

    /// Same words within a few seconds of each other.
    public static bool IsEcho(TranscriptSegment me, TranscriptSegment them)
    {
        var close = me.StartTime <= them.EndTime + 4 && them.StartTime <= me.EndTime + 4;
        if (!close) return false;
        var a = Heuristics.SignificantTokens(me.Text);
        var b = Heuristics.SignificantTokens(them.Text);
        if (a.Count == 0 || b.Count == 0)
            return string.Equals(Normalize(me.Text), Normalize(them.Text), StringComparison.Ordinal) && Normalize(me.Text).Length > 0;
        return a.Intersect(b).Count() / (double)a.Count >= 0.6;
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// Stops capture, transcribes what is still buffered, closes the WAVs and saves the
    /// meeting in Processing state (the report runs next, see ReportGenerator).
    public async Task<Meeting> StopAsync()
    {
        if (!IsRunning) return _meeting;
        IsRunning = false;
        foreach (var t in _tracks)
        {
            try { t.Source.Stop(); } catch (Exception) { /* device already gone */ }
        }
        _cts.Cancel();
        if (_poller != null) await _poller.ConfigureAwait(false);
        foreach (var t in _tracks) Cut(t, draining: true);
        _queue.Writer.TryComplete();
        if (_worker != null) await _worker.ConfigureAwait(false);
        foreach (var t in _tracks) t.Writer.Dispose();

        lock (_meetingLock)
        {
            _meeting.Duration = Math.Max((DateTime.UtcNow - _startedAt).TotalSeconds,
                                         _meeting.Segments.Count == 0 ? 0 : _meeting.Segments.Max(s => s.EndTime));
            _meeting.Segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            if (_copilot != null)
            {
                var kept = _copilot.Insights.Where(i => i.KindKey != "doc_excerpt").ToList();
                _meeting.Insights = kept;
            }
            _meeting.Status = MeetingStatus.Processing;
            _store.Save(_meeting);
        }
        _copilot?.Stop();
        return _meeting;
    }

    public async ValueTask DisposeAsync()
    {
        if (IsRunning) await StopAsync().ConfigureAwait(false);
        foreach (var t in _tracks) t.Source.Dispose();
        _cts.Dispose();
    }
}
