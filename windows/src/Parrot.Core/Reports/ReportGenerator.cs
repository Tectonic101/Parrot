// Parrot for Windows. Derived from Parrot (GPL-3.0), RecordingManager.generateSummary in RecordingManager.swift.
using Parrot.Core.AI;
using Parrot.Core.Models;
using Parrot.Core.Storage;

namespace Parrot.Core.Reports;

/// Writes the post-call report: summary (overview, pain points, key points, next steps)
/// and coaching (snapshot, went well, to improve, objections, commitments), each bullet
/// with its [mm:ss] receipt.
public sealed class ReportGenerator
{
    private readonly IAnalysisProvider _provider;
    private readonly MeetingStore? _store;

    public ReportGenerator(IAnalysisProvider provider, MeetingStore? store)
    {
        _provider = provider;
        _store = store;
    }

    /// Best-effort, like the Mac: the transcript is already saved, so a failure only
    /// records the error. Returns the error message, or null on success.
    public async Task<string?> GenerateAsync(Meeting meeting, CallProfile? profile, bool includeCoaching = true,
                                             CancellationToken ct = default)
    {
        if (meeting.Segments.Count == 0)
        {
            meeting.Status = MeetingStatus.Done;
            _store?.Save(meeting);
            return null;
        }
        if (!_provider.IsConfigured)
        {
            meeting.Status = MeetingStatus.Done;
            meeting.ErrorMessage = "No report: the assistant isn't set up (Settings → Assistant).";
            _store?.Save(meeting);
            return meeting.ErrorMessage;
        }

        var transcript = meeting.PromptTranscript;
        var titles = meeting.SortedInsights
            .Select(i => $"{profile?.KindFor(i.KindKey)?.Label ?? meeting.SnapshotKinds.FirstOrDefault(k => k.Key == i.KindKey)?.Label ?? i.KindKey}: {i.Title}")
            .ToList();
        var instructions = profile?.Tone ?? "";
        var counterpart = profile?.Counterpart ?? meeting.Counterpart;
        string? error = null;

        meeting.Status = MeetingStatus.Processing;
        try
        {
            meeting.Summary = await _provider.SummarizeAsync(transcript, titles, instructions, counterpart, ct).ConfigureAwait(false);
            _store?.Save(meeting);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            error = "Summary failed: " + e.Message;
        }

        if (includeCoaching)
        {
            try
            {
                var talk = meeting.TalkPercentMe ?? 0;
                meeting.Coaching = await _provider.CoachingReportAsync(transcript, talk, instructions, counterpart, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error ??= "Coaching failed: " + e.Message;
            }
        }

        meeting.Status = MeetingStatus.Done;
        meeting.ErrorMessage = error;
        _store?.Save(meeting);
        return error;
    }
}
