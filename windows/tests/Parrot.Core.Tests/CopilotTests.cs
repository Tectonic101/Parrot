// Parrot for Windows tests. Derived from Parrot (GPL-3.0), CopilotHarness.swift / CallAnalysisEngine checks.
using Parrot.Core.AI;
using Parrot.Core.Copilot;
using Parrot.Core.Knowledge;
using Parrot.Core.Models;
using Parrot.Core.Profiles;

namespace Parrot.Core.Tests;

public class CopilotTests
{
    private static AnalysisResult Result(params InsightDraft[] drafts) =>
        new(drafts, new Dictionary<string, int> { ["score"] = 72 }, "warm", "Ask who signs off.", Array.Empty<string>());

    private static CopilotEngine Engine(FakeProvider provider, KnowledgeBase? kb = null) =>
        // Slow pace keeps the background debounce out of the way; tests drive passes with AnalyzeNowAsync.
        new(provider, kb, () => CopilotPace.Relaxed);

    [Fact]
    public async Task Pass_SendsWindowAndKbAndAddsCards()
    {
        var kb = new KnowledgeBase(null);
        kb.AddText("integrations.md", "# Integrations\n\nWe offer a native two-way Salesforce integration on every paid plan.");
        var provider = new FakeProvider();
        provider.Results.Enqueue(Result(new InsightDraft("suggestion", "Salesforce sync", "Native two-way sync.", "integrations.md")));
        var engine = Engine(provider, kb);
        var profile = ProfilePresets.MakeDefault();
        profile.Tone = "Be brief.";
        engine.Start(profile, "Renewal call");
        Assert.Equal(CopilotStatus.Listening, engine.Status);

        engine.Ingest("Thanks for having me.", 1, Speaker.Me);
        engine.Ingest("Do you integrate with Salesforce?", 4, Speaker.Them);
        await engine.AnalyzeNowAsync();

        var req = Assert.Single(provider.Requests);
        Assert.Equal("Me: Thanks for having me.\nThem: Do you integrate with Salesforce?", req.Transcript);
        Assert.Equal("Be brief.", req.Instructions);
        Assert.Equal("Renewal call", req.CallBrief);
        Assert.Equal("integrations.md", req.References[0].DocumentName);
        Assert.Equal(new[] { "integrations.md" }, req.KnownDocumentNames);

        var card = Assert.Single(engine.Insights);
        Assert.Equal("Salesforce sync", card.Title);
        Assert.Equal(4, card.CallTime);
        Assert.Equal(72, engine.CallScore);
        Assert.Equal("Ask who signs off.", engine.CoachLine);
        Assert.Equal(CopilotStatus.Listening, engine.Status);
        engine.Stop();
    }

    [Fact]
    public async Task Pass_DedupsResolvesAndHonorsCorroboratedSupersedes()
    {
        var provider = new FakeProvider();
        provider.Results.Enqueue(Result(new InsightDraft("blocker", "Budget concern", "No budget until Q3.", null, "Could a Q2 pilot work?")));
        provider.Results.Enqueue(Result(
            new InsightDraft("blocker", "Budget still blocked", "Budget not available until Q3.", null),            // near duplicate
            new InsightDraft("blocker", "Budget timing", "Budget only in Q3.", null, null, "Budget concern"),       // corroborated supersedes
            new InsightDraft("question", "Needs SSO?", "Asked about SAML SSO.", null, null, "Nonexistent card"),    // bogus supersedes: kept
            new InsightDraft("blocker", "budget concern", "dup title", null)));                                    // same title
        var engine = Engine(provider);
        engine.Start(ProfilePresets.MakeDefault());

        engine.Ingest("We have no budget until Q3.", 10, Speaker.Them);
        await engine.AnalyzeNowAsync();
        engine.Ingest("Still, budget is the problem. Do you support SSO?", 20, Speaker.Them);
        await engine.AnalyzeNowAsync();

        Assert.Equal(new[] { "Needs SSO?", "Budget concern" }, engine.Insights.Select(i => i.Title));
        Assert.Contains("Budget concern", provider.Requests[1].KnownInsightTitles);

        provider.Results.Enqueue(new AnalysisResult(Array.Empty<InsightDraft>(), new Dictionary<string, int>(), null, null, new[] { "BUDGET CONCERN" }));
        engine.Ingest("Okay, a Q2 pilot works for us.", 30, Speaker.Them);
        await engine.AnalyzeNowAsync();
        Assert.True(engine.Insights.Single(i => i.Title == "Budget concern").IsHandled);
    }

    [Fact]
    public async Task Errors_ReArmWindowAndFlagMissingKey()
    {
        var provider = new FakeProvider { Throw = new AnalysisException("No key", missingKey: true) };
        var engine = Engine(provider);
        engine.Start(ProfilePresets.MakeDefault());
        engine.Ingest("How much is it?", 3, Speaker.Them);
        await engine.AnalyzeNowAsync();
        Assert.Equal(CopilotStatus.NeedsApiKey, engine.Status);

        provider.Throw = null;
        provider.Results.Enqueue(Result(new InsightDraft("suggestion", "Quote the price", "49 per seat.", null)));
        await engine.AnalyzeNowAsync();
        // The failed window is analyzed again on the next pass.
        Assert.Equal("Them: How much is it?", provider.Requests[1].Transcript);
        Assert.Single(engine.Insights);
    }

    [Fact]
    public void Unconfigured_ProviderNeedsKeyAndIgnoresSpeech()
    {
        var provider = new FakeProvider { IsConfigured = false };
        var engine = Engine(provider);
        engine.Start(ProfilePresets.MakeDefault());
        Assert.Equal(CopilotStatus.NeedsApiKey, engine.Status);
        engine.Ingest("Hello?", 1, Speaker.Them);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Pause_HoldsSpeechAndDismissRemovesCards()
    {
        var provider = new FakeProvider();
        provider.Results.Enqueue(Result(new InsightDraft("action_item", "Send pricing", "By Friday.", null)));
        var engine = Engine(provider);
        engine.Start(ProfilePresets.MakeDefault());
        engine.SetPaused(true);
        Assert.Equal(CopilotStatus.Paused, engine.Status);
        engine.Ingest("I'll send pricing by Friday.", 5, Speaker.Me);
        Assert.Empty(provider.Requests);
        engine.SetPaused(false); // resuming analyzes the held speech right away
        await TestWait.Until(() => engine.Insights.Count == 1);
        var card = Assert.Single(engine.Insights);
        engine.MarkHandled(card.Id);
        Assert.True(engine.Insights[0].IsHandled);
        engine.Dismiss(card.Id);
        Assert.Empty(engine.Insights);
    }

    [Fact]
    public async Task TalkShare_FeedsDominanceGauge()
    {
        var provider = new FakeProvider();
        var engine = Engine(provider);
        engine.Start(ProfilePresets.MakeDefault());
        engine.Ingest("A long monologue from me.", 0, Speaker.Me, duration: 45);
        engine.Ingest("Ok.", 45, Speaker.Them, duration: 15);
        await engine.AnalyzeNowAsync();
        Assert.Equal(75, engine.UserTalkPercent);
        Assert.Equal(75, engine.Sentiment["my_dominance"]);
    }

    [Fact]
    public void Pace_TimingsMatchMac()
    {
        var fast = CopilotPace.Fast.Timing();
        Assert.Equal(new PaceTiming(0.3, 8, 5, 15, 2), fast);
    }
}
