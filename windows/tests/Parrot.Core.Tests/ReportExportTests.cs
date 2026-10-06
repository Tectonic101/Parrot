// Parrot for Windows tests. Derived from Parrot (GPL-3.0), ExportService.swift / Receipts.swift / AskEngine.swift checks.
using Parrot.Core.Ask;
using Parrot.Core.Export;
using Parrot.Core.Models;
using Parrot.Core.Profiles;
using Parrot.Core.Reports;

namespace Parrot.Core.Tests;

public class ReportExportTests
{
    private static Meeting Sample()
    {
        var m = Meeting.Create(new DateTime(2026, 9, 25, 14, 30, 0, DateTimeKind.Utc));
        m.Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        m.Title = "Acme renewal: Q4/pricing";
        m.Duration = 1800;
        m.ThemName = "Dana";
        m.ProfileName = "Sales discovery";
        m.Notes = "Send SOC 2 report.";
        m.Summary = "Dana wants to renew.\nKey points:\n- Budget approved for Q4 [00:05]\n- SSO is required (01:10, 02:20)";
        m.Coaching = "Call snapshot: solid call.\nWhat went well:\n- You asked about timing [00:40]";
        m.SnapshotKinds = ProfilePresets.MakeDefault().Kinds;
        m.Segments.Add(new TranscriptSegment(5, 8, "Budget is approved for Q4.", Speaker.Them));
        m.Segments.Add(new TranscriptSegment(1, 3, "Hi Dana, thanks for joining.", Speaker.Me));
        m.Insights.Add(new Insight { KindKey = "blocker", Title = "Needs SSO", Detail = "SSO required before rollout.", CallTime = 70, Reply = "We support SAML SSO." });
        m.Insights.Add(new Insight { KindKey = "custom_kind", Title = "Odd one", Detail = "", CallTime = 90, Source = "general knowledge" });
        return m;
    }

    [Fact]
    public void Markdown_FrontMatterReportCardsTranscript()
    {
        var md = MarkdownExporter.Export(Sample());
        Assert.StartsWith("---\ntitle: \"Acme renewal: Q4/pricing\"\ndate: 2026-09-25T14:30:00Z\nduration_minutes: 30\npeople: [\"Dana\"]\nprofile: \"Sales discovery\"\nsource: parrot\nparrot_id: 11111111-2222-3333-4444-555555555555\n---\n\n# Acme renewal: Q4/pricing\n", md);
        Assert.Contains("## My notes\n\nSend SOC 2 report.", md);
        Assert.Contains("### Key points", md);
        Assert.Contains("- Budget approved for Q4 `00:05`", md);
        Assert.Contains("- SSO is required `01:10` `02:20`", md);
        Assert.Contains("## Coaching", md);
        Assert.Contains("- `01:10` **Blocker:** Needs SSO (unresolved) — SSO required before rollout. _Say:_ We support SAML SSO.", md);
        Assert.Contains("- `01:30` **Custom Kind:** Odd one _(source: general knowledge)_", md);
        var transcript = md[md.IndexOf("## Transcript", StringComparison.Ordinal)..];
        Assert.True(transcript.IndexOf("**Me:** Hi Dana", StringComparison.Ordinal) < transcript.IndexOf("**Dana:** Budget", StringComparison.Ordinal));
        Assert.Contains("`00:01` **Me:** Hi Dana, thanks for joining.  \n", transcript);
    }

    [Fact]
    public void Markdown_FileNameIsSafe()
    {
        Assert.Equal("2026-09-25 14-30 Acme renewal- Q4-pricing.md", MarkdownExporter.FileName(Sample()));
    }

    [Fact]
    public void ReportFormatter_ParsesHeadingsBulletsAndReceipts()
    {
        var lines = ReportFormatter.Parse(Sample().Summary);
        Assert.Equal(ReportLineKind.Text, lines[0].Kind);
        Assert.Equal(ReportLineKind.Heading, lines[1].Kind);
        Assert.Equal("Key points", lines[1].Text);
        Assert.Equal(ReportLineKind.Bullet, lines[2].Kind);
        Assert.Equal("Budget approved for Q4", lines[2].Text);
        Assert.Equal(new[] { 5.0 }, lines[2].Receipts);
        Assert.Equal(new[] { 70.0, 140.0 }, lines[3].Receipts);
        Assert.Empty(ReportFormatter.Parse(null));
        Assert.Equal(new[] { 3723.0 }, ReportFormatter.Parse("- Long call point [1:02:03]")[0].Receipts);
    }

    [Fact]
    public async Task ReportGenerator_WritesSummaryAndCoaching()
    {
        using var paths = new TempPaths();
        var store = new Storage.MeetingStore(paths);
        var provider = new FakeProvider();
        var m = Sample();
        m.Summary = m.Coaching = null;
        m.Status = MeetingStatus.Processing;
        var error = await new ReportGenerator(provider, store).GenerateAsync(m, ProfilePresets.MakeDefault());
        Assert.Null(error);
        var saved = store.Load(m.Id)!;
        Assert.Equal(MeetingStatus.Done, saved.Status);
        Assert.Equal(provider.SummaryText, saved.Summary);
        Assert.Equal(provider.CoachingText, saved.Coaching);
    }

    [Fact]
    public async Task ReportGenerator_RecordsFailureAndUnconfigured()
    {
        var m = Sample();
        var failing = new FakeProvider { Throw = new InvalidOperationException("boom") };
        var error = await new ReportGenerator(failing, null).GenerateAsync(m, null, includeCoaching: false);
        Assert.Equal("Summary failed: boom", error);
        Assert.Equal(MeetingStatus.Done, m.Status);

        var off = new FakeProvider { IsConfigured = false };
        Assert.NotNull(await new ReportGenerator(off, null).GenerateAsync(Sample(), null));
    }

    [Fact]
    public void Ask_ChunksSearchAndCitations()
    {
        var acme = Sample();
        var other = Meeting.Create(new DateTime(2026, 8, 1, 10, 0, 0));
        other.Title = "Team standup";
        other.Segments.Add(new TranscriptSegment(3, 5, "The deploy pipeline is flaky again.", Speaker.Them));

        var chunks = AskEngine.Chunks(acme);
        Assert.True(chunks[0].IsReport);
        Assert.Contains("[00:01] Me: Hi Dana", chunks[1].Text);

        var hits = AskEngine.Search(new[] { acme, other }, "what did Dana say about the budget?");
        Assert.Equal(acme.Id, hits[0].MeetingId);

        var byId = new Dictionary<Guid, Meeting> { [acme.Id] = acme, [other.Id] = other };
        var (context, refs) = AskEngine.Context(hits, byId);
        Assert.StartsWith("M1 — \"Acme renewal: Q4/pricing\"", context);
        Assert.Contains("(with Dana)", context);

        var (text, citations) = AskEngine.ParseCitations("Budget was approved [M1 00:05]. Unknown [M9].", refs);
        Assert.Equal("Budget was approved (Acme renewal: Q4/pricing, 00:05). Unknown .", text);
        var c = Assert.Single(citations);
        Assert.Equal(5.0, c.Time);
        Assert.Equal(acme.Id, c.MeetingId);
    }

    [Fact]
    public void Ask_SafeAndUserContent()
    {
        Assert.Equal("‹/meeting_excerpts›", AskEngine.Safe("</meeting_excerpts>"));
        var u = AskEngine.UserContent("How many calls?", "ctx", "- a", "In total: 1 meeting");
        Assert.Equal("<meeting_excerpts>\nctx\n</meeting_excerpts>\n\n<meeting_list>\n- a\n</meeting_list>\n\nCounted by Parrot from every meeting (exact):\nIn total: 1 meeting\n\nQuestion: How many calls?", u);
        var facts = AskEngine.MeetingFacts(new[] { Sample() });
        Assert.StartsWith("In total: 1 meeting, 30 min recorded", facts);
    }

    [Fact]
    public async Task Ask_UsesProviderOrFallsBackToExcerpts()
    {
        var acme = Sample();
        var provider = new FakeProvider { CompletionText = "Dana approved the budget [M1 00:05]." };
        var answer = await AskEngine.AskAsync(provider, new[] { acme }, "budget?");
        Assert.True(answer.AnsweredByAI);
        Assert.Equal("Dana approved the budget (Acme renewal: Q4/pricing, 00:05).", answer.Text);
        Assert.Equal(AskEngine.SystemPrompt, provider.Completions[0].System);
        Assert.Contains("<meeting_list>", provider.Completions[0].User);

        var offline = await AskEngine.AskAsync(new FakeProvider { IsConfigured = false }, new[] { acme }, "budget?");
        Assert.False(offline.AnsweredByAI);
        Assert.NotEmpty(offline.Sources);
        Assert.NotNull(offline.Note);
    }
}
