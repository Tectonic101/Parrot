import Foundation

/// Parakeet and the language router (spec 2026-09-30).
extension ProfileTest {
    static func testLanguageRouter() {
        typealias R = LanguageRouter
        var auto = R(parakeet: true, pinned: nil)
        check("router: auto holds both sides", auto.isHolding && auto.route(.me) == .undecided && auto.route(.them) == .undecided)
        check("router: English, sure → Parakeet", auto.heard(.me, language: "en", confidence: 0.95) == .decided(.me, .parakeet(language: "en")))
        check("router: Turkish → Whisper", auto.heard(.them, language: "tr", confidence: 0.99) == .decided(.them, .whisper))
        check("router: both decided → no hold", !auto.isHolding)

        var unsure = R(parakeet: true, pinned: nil)
        check("router: unsure → Whisper", unsure.heard(.me, language: "en", confidence: 0.5) == .decided(.me, .whisper))
        check("router: no answer → Whisper", unsure.heard(.them, language: nil, confidence: 0) == .decided(.them, .whisper))

        let german = R(parakeet: true, pinned: "de")
        check("router: pinned German → Parakeet, no hold", german.route(.me) == .parakeet(language: "de") && !german.isHolding)
        check("router: pinned Turkish → Whisper", R(parakeet: true, pinned: "tr").route(.them) == .whisper)
        check("router: Whisper model → always Whisper", R(parakeet: false, pinned: nil).route(.me) == .whisper)

        var recheck = R(parakeet: true, pinned: nil)
        _ = recheck.heard(.them, language: "en", confidence: 0.99)
        check("recheck: not before 60 s of speech", !recheck.decoded(.them, seconds: 40))
        check("recheck: due at 60 s", recheck.decoded(.them, seconds: 25))
        check("recheck: still English → stays", recheck.heard(.them, language: "en", confidence: 0.97) == nil
              && recheck.route(.them) == .parakeet(language: "en"))
        check("recheck: counter restarts after a check", !recheck.decoded(.them, seconds: 30))
        check("recheck: Turkish now → switch", recheck.heard(.them, language: "tr", confidence: 0.9) == .switchedToWhisper(.them))
        check("recheck: unsure Turkish doesn't switch", {
            var r = R(parakeet: true, pinned: nil)
            _ = r.heard(.me, language: "en", confidence: 0.99)
            return r.heard(.me, language: "tr", confidence: 0.6) == nil
        }())
        check("router: no switching back", recheck.heard(.them, language: "en", confidence: 0.99) == nil && recheck.route(.them) == .whisper)
        check("router: Whisper tracks never recheck", !recheck.decoded(.them, seconds: 600))

        var banner = R(parakeet: true, pinned: "en")
        banner.switchLanguage(to: "tr")
        check("switch: to Turkish → both Whisper", banner.route(.me) == .whisper && banner.route(.them) == .whisper)
        var toGerman = R(parakeet: true, pinned: nil)
        toGerman.switchLanguage(to: "de")
        check("switch: to German decides held sides", toGerman.route(.me) == .parakeet(language: "de") && !toGerman.isHolding)
    }

    static func testLanguageProbe() {
        let t0 = Date(timeIntervalSince1970: 1000)
        let second = [Float](repeating: 0.1, count: 16000)
        var p = LanguageProbe()
        var ready: [Float]?
        for i in 0..<9 { ready = p.add(second, voiced: true, from: .them, at: t0.addingTimeInterval(Double(i)), holding: true) }
        check("probe: not ready at 9 s", ready == nil)
        ready = p.add(second, voiced: true, from: .them, at: t0.addingTimeInterval(9), holding: true)
        check("probe: ready at 10 s of speech", ready?.count == LanguageProbe.full)
        check("probe: once per track", p.add(second, voiced: true, from: .them, at: t0.addingTimeInterval(10), holding: true) == nil)
        check("probe: silence doesn't count", p.add(second, voiced: false, from: .me, at: t0, holding: true) == nil)
        _ = p.add(second, voiced: true, from: .me, at: t0, holding: true)
        check("probe: other track is separate", p.take(.me, at: t0.addingTimeInterval(5), force: false, holding: true) == nil)
        check("probe: 30 s wait while holding", p.take(.me, at: t0.addingTimeInterval(30), force: false, holding: true)?.count == 16000)
        var warn = LanguageProbe()
        _ = warn.add(second, voiced: true, from: .me, at: t0, holding: false)
        check("probe: no wait when not holding", warn.take(.me, at: t0.addingTimeInterval(60), force: false) == nil)
        check("probe: forced at stop takes what's there", warn.take(.me, at: t0, force: true)?.count == 16000)
        warn.rearm(.me)
        check("probe: rearm gathers again", warn.take(.me, at: t0, force: false) == nil
              && warn.add(Array(repeating: 0.1, count: LanguageProbe.full), voiced: true, from: .me, at: t0, holding: false) != nil)
    }

    static func testEngineRecommendation() {
        typealias E = EngineRecommendation
        check("recommend: English and German Mac → Parakeet",
              E.recommend(preferredLanguages: ["en-GB", "de-DE"], pastCallLanguages: [], memoryGB: 16) == ParakeetTranscriber.modelID)
        check("recommend: Turkish anywhere in the list → Whisper",
              E.recommend(preferredLanguages: ["en-GB", "tr-TR"], pastCallLanguages: [], memoryGB: 16) == "large-v3-turbo")
        check("recommend: a past Turkish call → Whisper",
              E.recommend(preferredLanguages: ["en-US"], pastCallLanguages: ["en", "tr"], memoryGB: 8) == "base")
        check("recommend: script subtags don't confuse it",
              E.recommend(preferredLanguages: ["sr-Latn-RS"], pastCallLanguages: [], memoryGB: 16) == "large-v3-turbo")
        check("recommend: no languages at all → Whisper",
              E.recommend(preferredLanguages: [], pastCallLanguages: [], memoryGB: 16) == "large-v3-turbo")
        let langs = E.pastCallLanguages(["Merhaba, bugün fiyatları konuşalım mı? Teklifinizi aldık, çok teşekkür ederiz.",
                                         "Thanks for joining, let's go through the pricing today."])
        check("recommend: reads past call languages", Set(langs) == ["tr", "en"])
        check("parakeet: model id is recognised", TranscriptionEngine.isParakeet("parakeet-v3") && !TranscriptionEngine.isParakeet("base"))
        check("parakeet: display name", TranscriptionEngine.displayName(for: "parakeet-v3") == "Parakeet v3")
    }
}
