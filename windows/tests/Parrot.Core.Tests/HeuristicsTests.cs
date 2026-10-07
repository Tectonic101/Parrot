// Parrot for Windows tests. Derived from Parrot (GPL-3.0), ProfileTest.swift (copilot heuristic checks).
using Parrot.Core.Copilot;
using Parrot.Core.Models;

namespace Parrot.Core.Tests;

public class HeuristicsTests
{
    [Theory]
    [InlineData("Do you take credit cards?", true)]
    [InlineData("how much is the enterprise plan", true)]
    [InlineData("Tell me about your onboarding", true)]
    [InlineData("We shipped the release yesterday.", false)]
    [InlineData("Sounds good, let's continue.", false)]
    public void LooksLikeQuestion(string text, bool expected) => Assert.Equal(expected, Heuristics.LooksLikeQuestion(text));

    [Theory]
    [InlineData("Really?", false)]
    [InlineData("How are you?", false)]
    [InlineData("Do you take credit cards?", true)]
    [InlineData("What's the price for fifty seats?", true)]
    public void IsSubstantiveQuestion(string text, bool expected) => Assert.Equal(expected, Heuristics.IsSubstantiveQuestion(text));

    [Fact]
    public void LatestQuestion_OnlyFromThem()
    {
        var window = new List<(string, Speaker)>
        {
            ("What is your budget?", Speaker.Me),
            ("Can you integrate with SAP?", Speaker.Them),
            ("We're fine with that.", Speaker.Them),
            ("Is there a discount?", Speaker.Me),
        };
        Assert.Equal("Can you integrate with SAP?", Heuristics.LatestQuestion(window));
        Assert.Null(Heuristics.LatestQuestion(new List<(string, Speaker)> { ("Is it ready?", Speaker.Me) }));
    }

    [Fact]
    public void NearDuplicateAndTopicStem()
    {
        Assert.True(Heuristics.IsNearDuplicate("Pricing concern over seat costs", "Still worried about seat pricing costs"));
        Assert.False(Heuristics.IsNearDuplicate("Pricing concern over seat costs", "Needs SSO before rollout"));
        Assert.True(Heuristics.SharesTopicStem("banking integration", "the bank asked"));
        Assert.False(Heuristics.SharesTopicStem("security review", "pricing tiers"));
    }

    [Fact]
    public void VerdictCorroborated_NeedsRealOpenCardOnSameTopic()
    {
        var open = new List<(string, string)> { ("Budget concern", "Budget concern No budget until Q3") };
        Assert.True(Heuristics.VerdictCorroborated("budget concern", "Budget still blocked for Q3", open));
        Assert.False(Heuristics.VerdictCorroborated("Budget concern", "Needs SSO", open));
        Assert.False(Heuristics.VerdictCorroborated("Unknown card", "Budget still blocked", open));
    }

    [Fact]
    public void WindowSuffixCount_FloorsAndCaps()
    {
        var times = Enumerable.Range(0, 100).Select(i => i * 10.0).ToList(); // one line per 10 s
        Assert.Equal(31, Heuristics.WindowSuffixCount(times, 300));
        Assert.Equal(10, Heuristics.WindowSuffixCount(times, 5));
        Assert.Equal(3, Heuristics.WindowSuffixCount(times.Take(3).ToList(), 1));
        Assert.Equal(50, Heuristics.WindowSuffixCount(times, 99999, maxCount: 50));
        Assert.Equal(0, Heuristics.WindowSuffixCount(new List<double>(), 300));
    }

    [Fact]
    public void PromptLine_UsesInternalTags() =>
        Assert.Equal("Them: hello", Heuristics.PromptLine("hello", Speaker.Them));
}
