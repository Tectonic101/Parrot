// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/ProfilePresets.swift.
// Persona, kind and gauge text is kept verbatim from the Mac presets.
using Parrot.Core.Models;

namespace Parrot.Core.Profiles;

public static class ProfilePresets
{
    public static readonly Guid DefaultProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    public static readonly Guid SalesId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    public static readonly Guid CoachingId = Guid.Parse("00000000-0000-0000-0000-0000000000C2");
    public static readonly Guid InterviewId = Guid.Parse("00000000-0000-0000-0000-0000000000C3");
    public static readonly Guid SupportId = Guid.Parse("00000000-0000-0000-0000-0000000000C4");
    public static readonly Guid GenericId = Guid.Parse("00000000-0000-0000-0000-0000000000C5");
    public static readonly Guid VendorId = Guid.Parse("00000000-0000-0000-0000-0000000000C6");

    /// Same numbering as the Mac presets (v4: "Vendor call" added).
    public const int PresetVersion = 4;

    private static ProfileKind Kind(string key, string label, string hex, string icon, string trigger,
                                    bool pinned = false, int priority = 0) =>
        new() { Key = key, Label = label, ColorHex = hex, Icon = icon, TriggerDescription = trigger, IsPinned = pinned, Priority = priority };

    private static SentimentGauge Gauge(string key, string label, string low, string high, string hex) =>
        new() { Key = key, Label = label, LowLabel = low, HighLabel = high, ColorHex = hex };

    public const string DefaultPersona =
        "You are a live call assistant. Draft short, concrete lines the user can say, flag obstacles, and capture commitments.";

    public const string VendorPersona =
        "You assist someone who is the CUSTOMER on this call: a bank, payment provider, supplier or agency " +
        "is explaining what it offers, answering their questions, or onboarding them. The user is not " +
        "selling anything and the other party is never a prospect. Protect the user's interests: capture " +
        "exactly what the vendor commits to and what it costs, flag risks, holds, exclusions and unanswered " +
        "questions, and suggest what to ask next. Keep the vendor's numbers verbatim.";

    public const string SalesPersona =
        "You are an elite B2B sales coach embedded in a live discovery call, coaching the user in real time. " +
        "Push qualification over pitching: help the user uncover pain, budget, authority, and timeline. " +
        "Don't just hand over lines — coach. Call it out when the user is talking too much, skips a buying " +
        "signal, leaves the prospect's question unanswered, or misses a chance to dig into a stated pain. " +
        "Every card must be usable in the next 30 seconds.";

    public static CallProfile MakeDefault() => new()
    {
        Id = DefaultProfileId, Name = "Default", Icon = "",
        Summary = "General-purpose Assistant.", IsBuiltIn = true, SortOrder = 0,
        Persona = DefaultPersona, Tone = "", Counterpart = "the other person", AllowGeneralKnowledge = true,
        PresetVersion = PresetVersion,
        Kinds =
        {
            Kind("suggestion", "Suggested answer", "4F6FB0", "lightbulb", "The other person asked something or raised a topic — draft a short, concrete line to say now."),
            Kind("question", "Open question", "2F7E96", "question", "The other person asked a direct question that has NOT been answered yet — surface it briefly."),
            Kind("blocker", "Blocker", "E8943A", "warning", "An objection or obstacle came up (price, timing, decision maker, competitor) that isn't resolved.", pinned: true, priority: 10),
            Kind("action_item", "Action item", "3F9168", "check", "The user committed to do something after the call; include any time/date mentioned."),
            Kind("feedback", "Feedback", "5F6470", "chart", "A brief read on a SIGNIFICANT shift only — sparingly."),
        },
        Gauges = { Gauge("my_dominance", "You're talking", "Balanced", "Dominating", "5F6470") },
    };

    public static List<CallProfile> All() => new()
    {
        MakeDefault(),
        new CallProfile
        {
            Id = SalesId, Name = "Sales discovery", Icon = "",
            Summary = "Discovery & objection handling for sales calls.", IsBuiltIn = true, SortOrder = 1,
            Persona = SalesPersona, Counterpart = "the prospect", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("suggestion", "Suggested answer", "4F6FB0", "lightbulb", "The prospect asked something — draft a short, concrete line to say now."),
                Kind("objection", "Objection", "E8943A", "hand", "The prospect raised a concern (price, timing, competitor, authority) that isn't resolved.", pinned: true, priority: 10),
                Kind("unanswered_question", "Unanswered question", "C0563B", "question", "The prospect asked a question and the conversation moved on WITHOUT actually answering it — flag it so the user can circle back.", pinned: true, priority: 9),
                Kind("opportunity", "Opportunity", "7A5FB0", "sparkles", "The prospect revealed a pain, goal, or need the user's offering could solve — suggest how to position a solution (ground it in the knowledge base when available).", priority: 7),
                Kind("buying_signal", "Buying signal", "3F9168", "arrow", "The prospect showed interest or intent — flag it so the user can advance the deal."),
                Kind("next_step", "Next step", "2F7E96", "calendar", "A concrete next step or commitment to propose or confirm."),
                Kind("discovery_gap", "Ask this next", "C29218", "search", "An important unknown (budget, timeline, decision maker, success criteria) the user hasn't asked about yet — phrase the title as the question to ask."),
            },
            Gauges =
            {
                Gauge("buying_temperature", "Buying temp", "Cold", "Hot", "E8943A"),
                Gauge("my_dominance", "You're talking", "Balanced", "Dominating", "5F6470"),
            },
        },
        new CallProfile
        {
            Id = CoachingId, Name = "1:1 coaching", Icon = "",
            Summary = "Supportive listening for coaching / 1:1s.", IsBuiltIn = true, SortOrder = 2,
            Persona = "You are a warm, non-judgmental coaching assistant. Help the user listen deeply, reflect back, and ask open questions. Never frame the other person as an objection or obstacle.",
            Counterpart = "the person", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("reflection", "Reflection", "4F6FB0", "quote", "Offer a brief reflective statement the user could mirror back to show understanding."),
                Kind("open_question", "Open question", "2F7E96", "question", "A non-leading open question the user could ask to deepen the conversation."),
                Kind("emotional_cue", "Emotional cue", "E8943A", "wave", "The person expressed a notable emotion (frustration, relief, worry) worth acknowledging.", priority: 5),
                Kind("commitment", "Commitment", "3F9168", "check", "Either side committed to a concrete next step; include any timing."),
                Kind("coaching_moment", "Coaching moment", "5F6470", "lightbulb", "An opening for the user to offer guidance or a useful reframe."),
            },
            Gauges =
            {
                Gauge("client_openness", "Openness", "Guarded", "Open", "2F7E96"),
                Gauge("my_dominance", "You're talking", "Balanced", "Dominating", "5F6470"),
            },
        },
        new CallProfile
        {
            Id = InterviewId, Name = "Interview", Icon = "",
            Summary = "For when you're interviewing a candidate.", IsBuiltIn = true, SortOrder = 3,
            Persona = "You are an interview assistant helping the user assess a candidate fairly. Surface follow-ups, signals, and red flags; help them cover the ground they planned.",
            Counterpart = "the candidate", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("follow_up_question", "Follow-up", "2F7E96", "question", "A sharp follow-up question to probe the candidate's last answer."),
                Kind("red_flag", "Red flag", "E8943A", "flag", "Something concerning in the candidate's answer worth noting.", pinned: true, priority: 10),
                Kind("strong_signal", "Strong signal", "3F9168", "star", "A strong positive signal worth recording."),
                Kind("topic_to_cover", "Topic to cover", "4F6FB0", "list", "A planned topic the user hasn't covered yet."),
                Kind("note", "Note", "5F6470", "note", "A neutral observation worth capturing."),
            },
            Gauges = { Gauge("candidate_confidence", "Confidence", "Hesitant", "Confident", "3F9168") },
        },
        new CallProfile
        {
            Id = SupportId, Name = "Customer support", Icon = "",
            Summary = "Resolve issues and keep customers calm.", IsBuiltIn = true, SortOrder = 4,
            Persona = "You are a calm, helpful support assistant. Help the user resolve the customer's issue clearly and keep them reassured.",
            Counterpart = "the customer", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("answer", "Answer", "4F6FB0", "lightbulb", "The customer asked something — draft a clear, accurate answer the user can give."),
                Kind("unresolved_issue", "Unresolved issue", "E8943A", "warning", "An issue the customer raised that isn't resolved yet.", pinned: true, priority: 10),
                Kind("frustration_cue", "Frustration cue", "E8943A", "wave", "The customer is getting frustrated — flag it so the user can de-escalate."),
                Kind("follow_up", "Follow-up", "3F9168", "arrow", "A follow-up action the user should take or promise."),
                Kind("note", "Note", "5F6470", "note", "A neutral observation worth capturing."),
            },
            Gauges = { Gauge("customer_frustration", "Frustration", "Calm", "Upset", "E8943A") },
        },
        new CallProfile
        {
            Id = GenericId, Name = "Generic", Icon = "",
            Summary = "Minimal, neutral Assistant for any call.", IsBuiltIn = true, SortOrder = 5,
            Persona = "You are a neutral meeting assistant. Surface useful suggestions, open questions, and action items without assuming the call's purpose.",
            Counterpart = "the other person", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("suggestion", "Suggestion", "4F6FB0", "lightbulb", "A useful thing the user could say in response to the recent conversation."),
                Kind("question", "Open question", "2F7E96", "question", "A direct question the other person asked that hasn't been answered."),
                Kind("action_item", "Action item", "3F9168", "check", "Something the user committed to; include any timing."),
                Kind("note", "Note", "5F6470", "note", "A neutral observation worth capturing."),
            },
            Gauges = { Gauge("engagement", "Engagement", "Flat", "Engaged", "2F7E96") },
        },
        new CallProfile
        {
            Id = VendorId, Name = "Vendor call", Icon = "",
            Summary = "You are the customer: a bank, supplier or agency is pitching or onboarding you.", IsBuiltIn = true, SortOrder = 6,
            Persona = VendorPersona, Counterpart = "the vendor", AllowGeneralKnowledge = true, PresetVersion = PresetVersion,
            Kinds =
            {
                Kind("their_commitment", "Their commitment", "3F9168", "check", "The vendor promised something concrete: a timeline, a feature, a fee, a follow-up, a document. Capture it exactly as said."),
                Kind("my_open_question", "My open question", "C0563B", "question", "You asked the vendor something and did not get a clear answer, or the vendor moved on. Flag it so you can circle back; the reply is how to ask it again.", pinned: true, priority: 10),
                Kind("red_flag", "Red flag", "E8943A", "warning", "A limitation, risk, fee, hold, exclusion, lock-in or condition the vendor mentioned that could hurt you. Quote the condition.", pinned: true, priority: 9),
                Kind("pricing_detail", "Pricing detail", "4F6FB0", "tag", "A number the vendor stated: fee, rate, settlement time, limit, minimum, timeline. Record it so it is not lost."),
                Kind("ask_this", "Ask this", "C29218", "search", "An important thing you have not asked this vendor yet: pricing, timelines, support, exit terms, compliance steps. Phrase the title as the question.", priority: 7),
                Kind("next_step", "Next step", "2F7E96", "calendar", "A concrete action either side agreed to; say who and when."),
            },
            Gauges =
            {
                Gauge("fit", "Fit", "Poor", "Strong", "3F9168"),
                Gauge("my_dominance", "You're talking", "Balanced", "Dominating", "5F6470"),
            },
        },
    };
}
