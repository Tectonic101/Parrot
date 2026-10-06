// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/CallAnalysisEngine.swift.
using Parrot.Core.AI;
using Parrot.Core.Knowledge;
using Parrot.Core.Models;

namespace Parrot.Core.Copilot;

public enum CopilotStatus
{
    Off,
    Listening,
    Analyzing,
    Paused,
    NeedsApiKey,
    Error,
}

/// Always-on copilot loop: watches the live transcript and pushes insights (suggested
/// answers, blockers, action items) as the conversation unfolds.
///
/// Triggering is event-driven, not a fixed poll: a question from the other side fires
/// analysis almost immediately; mid-flow speech waits for a natural pause; a minimum
/// interval between calls keeps cost and card churn under control. One call in flight
/// at a time; new speech during a call queues one rerun.
///
/// Thread-safe. Events are raised on thread-pool threads; UIs must marshal.
public sealed class CopilotEngine
{
    private readonly object _lock = new();
    private readonly IAnalysisProvider _provider;
    private readonly KnowledgeBase? _knowledgeBase;
    private readonly Func<CopilotPace> _pace;
    private readonly Func<int> _windowMinutes;
    private readonly Func<DateTime> _now;

    private readonly List<(double Time, string Text, Speaker Speaker)> _segments = new();
    private readonly List<Insight> _insights = new();
    private Dictionary<string, int> _sentiment = new();
    private double _meSeconds, _themSeconds;
    private int _lastAnalyzedCount;
    private DateTime? _oldestPendingSince;
    private DateTime _lastAnalysisEnd = DateTime.MinValue;
    private bool _pendingUrgent;
    private bool _analysisRunning;
    private bool _rerunRequested;
    private int _generation;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource _sessionCts = new();

    public CallProfile? Profile { get; private set; }
    public string CallBrief { get; private set; } = "";
    public bool IsActive { get; private set; }
    public bool IsPaused { get; private set; }
    public CopilotStatus Status { get; private set; } = CopilotStatus.Off;
    public string? StatusMessage { get; private set; }
    public string? CoachLine { get; private set; }
    public string? SentimentRead { get; private set; }
    public IAnalysisProvider Provider => _provider;

    /// Raised after the card list changes (new cards, resolved, dismissed).
    public event Action? InsightsChanged;
    public event Action? StatusChanged;
    /// Raised after every successful pass (coach line, sentiment).
    public event Action? PassCompleted;

    public CopilotEngine(IAnalysisProvider provider, KnowledgeBase? knowledgeBase,
                         Func<CopilotPace>? pace = null, Func<int>? windowMinutes = null, Func<DateTime>? now = null)
    {
        _provider = provider;
        _knowledgeBase = knowledgeBase;
        _pace = pace ?? (() => CopilotPace.Fast);
        _windowMinutes = windowMinutes ?? (() => 5);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<Insight> Insights { get { lock (_lock) return _insights.ToList(); } }
    public IReadOnlyDictionary<string, int> Sentiment { get { lock (_lock) return new Dictionary<string, int>(_sentiment); } }
    public int? CallScore { get { lock (_lock) return _sentiment.TryGetValue("score", out var s) ? s : null; } }

    /// Share of the conversation spoken by the user, once there's enough signal (30 s).
    public int? UserTalkPercent
    {
        get
        {
            lock (_lock)
            {
                var total = _meSeconds + _themSeconds;
                return total < 30 ? null : (int)Math.Round(_meSeconds / total * 100);
            }
        }
    }

    public void Start(CallProfile profile, string brief = "")
    {
        lock (_lock)
        {
            _generation++;
            _sessionCts.Cancel();
            _sessionCts = new CancellationTokenSource();
            _debounceCts?.Cancel();
            _insights.Clear();
            _segments.Clear();
            _sentiment = new Dictionary<string, int>();
            _meSeconds = _themSeconds = 0;
            _lastAnalyzedCount = 0;
            _oldestPendingSince = null;
            _pendingUrgent = _rerunRequested = _analysisRunning = false;
            _lastAnalysisEnd = DateTime.MinValue;
            CoachLine = SentimentRead = null;
            Profile = profile;
            CallBrief = brief.Trim();
            IsActive = true;
            IsPaused = false;
            SetStatusLocked(_provider.IsConfigured ? CopilotStatus.Listening : CopilotStatus.NeedsApiKey, null);
        }
        RaiseStatus();
        InsightsChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _generation++;
            IsActive = false;
            _debounceCts?.Cancel();
            _sessionCts.Cancel();
            _analysisRunning = false;
            SetStatusLocked(CopilotStatus.Off, null);
        }
        RaiseStatus();
    }

    public void UpdateBrief(string text)
    {
        lock (_lock) CallBrief = text.Trim();
    }

    /// Mid-call pause: speech keeps accumulating, nothing is sent. Resume analyzes the backlog.
    public void SetPaused(bool paused)
    {
        var resume = false;
        lock (_lock)
        {
            if (!IsActive || paused == IsPaused) return;
            IsPaused = paused;
            if (paused)
            {
                _debounceCts?.Cancel();
                _oldestPendingSince = null;
                SetStatusLocked(CopilotStatus.Paused, null);
            }
            else
            {
                SetStatusLocked(_provider.IsConfigured ? CopilotStatus.Listening : CopilotStatus.NeedsApiKey, null);
                if (_segments.Count > _lastAnalyzedCount)
                {
                    _oldestPendingSince = _now();
                    resume = true;
                }
            }
        }
        RaiseStatus();
        if (resume) TriggerAnalysis(CurrentGeneration);
    }

    private int CurrentGeneration { get { lock (_lock) return _generation; } }

    /// Feed every finalized transcript segment here; the engine decides when to analyze.
    public void Ingest(string text, double time, Speaker speaker, double? duration = null)
    {
        var raise = false;
        lock (_lock)
        {
            if (!IsActive || string.IsNullOrWhiteSpace(text)) return;
            if (!_provider.IsConfigured)
            {
                if (Status != CopilotStatus.NeedsApiKey) { SetStatusLocked(CopilotStatus.NeedsApiKey, null); raise = true; }
                goto done;
            }
            _segments.Add((time, text, speaker));
            var d = duration ?? text.Length / 15.0;
            if (speaker == Speaker.Me) _meSeconds += d; else _themSeconds += d;
            if (IsPaused) goto done;

            _oldestPendingSince ??= _now();
            // Only the other side's questions get the fast track.
            var urgent = speaker == Speaker.Them && Heuristics.IsSubstantiveQuestion(text);
            if (urgent) _pendingUrgent = true;

            var timing = _pace().Timing();
            var delay = urgent ? timing.Question : timing.Idle;
            var remaining = Math.Max(0, timing.Staleness - (_now() - _oldestPendingSince.Value).TotalSeconds);
            delay = Math.Min(delay, remaining);

            _debounceCts?.Cancel();
            _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
            var token = _debounceCts.Token;
            var gen = _generation;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                TriggerAnalysis(gen);
            });
        }
    done:
        if (raise) RaiseStatus();
    }

    private void TriggerAnalysis(int gen)
    {
        double wait;
        CancellationToken token;
        lock (_lock)
        {
            if (!IsActive || IsPaused || gen != _generation || _segments.Count <= _lastAnalyzedCount) return;
            if (_analysisRunning) { _rerunRequested = true; return; }
            var timing = _pace().Timing();
            var floor = _pendingUrgent ? timing.QuestionFloor : timing.Floor;
            _pendingUrgent = false;
            wait = _lastAnalysisEnd == DateTime.MinValue ? 0 : floor - (_now() - _lastAnalysisEnd).TotalSeconds;
            _analysisRunning = true;
            token = _sessionCts.Token;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), token).ConfigureAwait(false);
                await RunAnalysisAsync(gen, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        });
    }

    /// Runs one analysis pass right now over un-analyzed speech (also used by tests).
    public Task AnalyzeNowAsync()
    {
        int gen;
        CancellationToken token;
        lock (_lock)
        {
            if (_analysisRunning || !IsActive) return Task.CompletedTask;
            _analysisRunning = true;
            gen = _generation;
            token = _sessionCts.Token;
        }
        return RunAnalysisAsync(gen, token);
    }

    private async Task RunAnalysisAsync(int gen, CancellationToken token)
    {
        AnalysisRequest request;
        int previousAnalyzed;
        DateTime? previousPending;
        double anchorTime;
        CallProfile profile;
        lock (_lock)
        {
            if (!IsActive || gen != _generation || token.IsCancellationRequested) return;
            profile = Profile!;
            if (profile.Kinds.Count == 0)
            {
                SetStatusLocked(CopilotStatus.Error, "This profile has no insight kinds — add one in Settings → Profiles.");
                _analysisRunning = false;
                goto raiseOnly;
            }
            SetStatusLocked(CopilotStatus.Analyzing, null);
            previousAnalyzed = _lastAnalyzedCount;
            previousPending = _oldestPendingSince;
            _lastAnalyzedCount = _segments.Count;
            _oldestPendingSince = null;

            var take = Heuristics.WindowSuffixCount(_segments.Select(s => s.Time).ToList(), _windowMinutes() * 60.0);
            var window = _segments.Skip(_segments.Count - take).ToList();
            var transcript = string.Join("\n", window.Select(s => Heuristics.PromptLine(s.Text, s.Speaker)));
            var knownTitles = _insights.Take(20).Select(i => i.Title).ToList();
            anchorTime = window.Count > 0 ? window[^1].Time : 0;

            // Two searches: the latest question from the other side leads (sharp, and it
            // is what the next card must answer), the recent window fills the rest.
            var references = new List<KBReference>();
            if (_knowledgeBase != null && !_knowledgeBase.IsEmpty)
            {
                var recent = _segments.Skip(Math.Max(0, _segments.Count - 8)).ToList();
                var question = Heuristics.LatestQuestion(recent.Select(r => (r.Text, r.Speaker)));
                if (question != null) references.AddRange(_knowledgeBase.Search(question, profile.Id, topK: 2));
                foreach (var r in _knowledgeBase.Search(string.Join(" ", recent.Select(x => x.Text)), profile.Id))
                    if (!references.Any(x => x.Text == r.Text)) references.Add(r);
            }

            request = new AnalysisRequest
            {
                Transcript = transcript,
                KnownInsightTitles = knownTitles,
                References = references,
                Instructions = profile.Tone,
                CallBrief = CallBrief,
                AllowGeneralKnowledge = profile.AllowGeneralKnowledge,
                KnownDocumentNames = _knowledgeBase?.DocumentsInPlay(profile.Id) ?? new List<string>(),
                Persona = profile.Persona,
                Counterpart = profile.Counterpart,
                Kinds = profile.Kinds,
                Gauges = profile.Gauges,
            };
        }
        RaiseStatus();

        AnalysisResult? result = null;
        Exception? error = null;
        try
        {
            result = await _provider.AnalyzeAsync(request, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            error = e;
        }

        var insightsChanged = false;
        var rerun = false;
        lock (_lock)
        {
            if (!IsActive || gen != _generation) return;
            if (error != null)
            {
                // Re-arm the window so a transient error doesn't permanently skip this speech.
                _lastAnalyzedCount = previousAnalyzed;
                _oldestPendingSince = previousPending;
                var missing = error is AnalysisException { IsMissingKey: true };
                SetStatusLocked(missing ? CopilotStatus.NeedsApiKey : CopilotStatus.Error, error.Message);
            }
            else if (result != null)
            {
                insightsChanged = ApplyResultLocked(result, anchorTime, profile);
                SetStatusLocked(IsPaused ? CopilotStatus.Paused : CopilotStatus.Listening, null);
            }
            _lastAnalysisEnd = _now();
            _analysisRunning = false;
            if (_rerunRequested) { _rerunRequested = false; rerun = true; }
        }
        RaiseStatus();
        if (insightsChanged) InsightsChanged?.Invoke();
        if (result != null) PassCompleted?.Invoke();
        if (rerun) TriggerAnalysis(gen);
        return;

    raiseOnly:
        RaiseStatus();
    }

    /// Merges a pass into the card list. Returns true when the list changed.
    private bool ApplyResultLocked(AnalysisResult result, double anchorTime, CallProfile profile)
    {
        var changed = false;
        var merged = new Dictionary<string, int>(result.Sentiment);
        var total = _meSeconds + _themSeconds;
        if (total >= 30 && profile.Gauges.Any(g => g.Key == "my_dominance"))
            merged["my_dominance"] = (int)Math.Round(_meSeconds / total * 100);
        _sentiment = merged;
        SentimentRead = result.Read;
        if (result.Coach != null) CoachLine = result.Coach;

        // Items the model says the conversation has since dealt with.
        foreach (var title in result.Resolved)
        {
            var lowered = title.ToLowerInvariant();
            var hit = _insights.FirstOrDefault(i => i.Title.ToLowerInvariant() == lowered && !i.IsHandled);
            if (hit != null) { hit.IsHandled = true; changed = true; }
        }

        var existingTitles = new HashSet<string>(_insights.Select(i => i.Title.ToLowerInvariant()));
        var open = _insights.Where(i => !i.IsHandled).ToList();
        var openCards = open.Select(o => (o.Title, $"{o.Title} {o.Detail}")).ToList();
        var admitted = result.Insights
            // The model's own dedup verdict, honored only when corroborated.
            .Where(d => string.IsNullOrEmpty(d.Supersedes)
                        || !Heuristics.VerdictCorroborated(d.Supersedes!, $"{d.Title} {d.Detail}", openCards))
            .Where(d => !existingTitles.Contains(d.Title.ToLowerInvariant()))
            // Drop reworded re-flags of a still-open issue of the same kind.
            .Where(d => !open.Any(e => e.KindKey == d.KindKey
                                       && Heuristics.IsNearDuplicate($"{d.Title} {d.Detail}", $"{e.Title} {e.Detail}")))
            .ToList();

        var fresh = admitted.Select(d => new Insight
        {
            KindKey = d.KindKey, Title = d.Title, Detail = d.Detail, CallTime = anchorTime,
            Source = d.Source, Reply = d.Reply,
        }).ToList();
        if (fresh.Count > 0)
        {
            _insights.InsertRange(0, fresh);
            changed = true;
        }
        return changed;
    }

    public void MarkHandled(Guid insightId)
    {
        lock (_lock)
        {
            var i = _insights.FirstOrDefault(x => x.Id == insightId);
            if (i == null) return;
            i.IsHandled = true;
        }
        InsightsChanged?.Invoke();
    }

    public void Dismiss(Guid insightId)
    {
        lock (_lock) _insights.RemoveAll(x => x.Id == insightId);
        InsightsChanged?.Invoke();
    }

    private void SetStatusLocked(CopilotStatus status, string? message)
    {
        Status = status;
        StatusMessage = message;
    }

    private void RaiseStatus() => StatusChanged?.Invoke();
}
