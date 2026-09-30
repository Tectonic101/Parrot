import Foundation

/// Which engine transcribes each side of a call when Parakeet is the model.
/// Parakeet knows 25 European languages and writes confident nonsense for
/// the rest (Turkish included), so on Auto-detect each side is held until
/// Whisper's language check has heard ~10 s of it. Unsure goes to Whisper:
/// a slower line beats a wrong one. Pure; `--profile-test` drives it.
struct LanguageRouter: Equatable {
    enum Route: Equatable {
        case undecided
        /// `language`: the ISO code heard or pinned; Parakeet's script hint.
        case parakeet(language: String?)
        case whisper
    }

    enum Change: Equatable {
        case decided(AudioSource, Route)
        /// Unsure the first time (a quiet side, or the speakers leaking into
        /// the mic): keep holding and check the next stretch once more.
        case retry(AudioSource)
        /// A Parakeet side now hears a language Parakeet doesn't know: its
        /// lines since the last clean check get re-done with Whisper.
        case switchedToWhisper(AudioSource)
    }

    static let parakeetLanguages: Set<String> = [
        "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hr", "hu", "it",
        "lt", "lv", "mt", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "uk",
    ]
    /// 0.24.2's bar: real calls scored 0.93-1.00 on 10 s of speech.
    static let sure: Float = 0.8
    /// Short enough that a call switching to Turkish is caught in about 30 s
    /// of speech, well inside the 90 s of audio kept for the redo.
    static let recheckEvery: TimeInterval = 30

    private(set) var routes: [AudioSource: Route]
    private var sinceCheck: [AudioSource: TimeInterval] = [:]
    /// Parakeet on Auto-detect: the only case the router decides anything.
    /// A pinned language is the user's call; 0.24.2's banner handles a wrong pin.
    private let auto: Bool
    private var retried: Set<AudioSource> = []

    init(parakeet: Bool, pinned: String?) {
        auto = parakeet && pinned == nil
        let start: Route
        if !parakeet {
            start = .whisper
        } else if let pinned {
            start = Self.parakeetLanguages.contains(pinned) ? .parakeet(language: pinned) : .whisper
        } else {
            start = .undecided
        }
        routes = Dictionary(uniqueKeysWithValues: AudioSource.allCases.map { ($0, start) })
    }

    func route(_ source: AudioSource) -> Route { routes[source] ?? .whisper }
    var isHolding: Bool { routes.values.contains(.undecided) }

    /// A language check came back for `source` (nil language = no answer).
    mutating func heard(_ source: AudioSource, language: String?, confidence: Float) -> Change? {
        let supported = language.map(Self.parakeetLanguages.contains) ?? false
        let sure = confidence >= Self.sure
        switch route(source) {
        case .undecided:
            // An answer that isn't sure gets one more look before Whisper;
            // no answer at all (no detector) has nothing to retry.
            if language != nil, !sure, !retried.contains(source) {
                retried.insert(source)
                return .retry(source)
            }
            let decided: Route = supported && sure ? .parakeet(language: language) : .whisper
            routes[source] = decided
            sinceCheck[source] = 0
            return .decided(source, decided)
        case .parakeet:
            sinceCheck[source] = 0
            guard auto, sure, language != nil, !supported else { return nil }
            routes[source] = .whisper
            return .switchedToWhisper(source)
        case .whisper:
            return nil
        }
    }

    /// Parakeet decoded `seconds` more of `source`; true when a recheck is due.
    mutating func decoded(_ source: AudioSource, seconds: TimeInterval) -> Bool {
        guard auto, case .parakeet = route(source) else { return false }
        let total = (sinceCheck[source] ?? 0) + seconds
        sinceCheck[source] = total
        return total >= Self.recheckEvery
    }

    /// The user switched the call's language (0.24.2's banner). A Whisper
    /// side stays Whisper; the rest follow the new language.
    mutating func switchLanguage(to code: String) {
        let next: Route = Self.parakeetLanguages.contains(code) ? .parakeet(language: code) : .whisper
        for source in AudioSource.allCases where route(source) != .whisper {
            routes[source] = next
        }
    }
}

/// 0.24.2's language probe, per side: voiced audio gathered until there's
/// enough to ask Whisper what language it is. Each side is checked once,
/// then again whenever it's re-armed (optionally after skipping some speech). While the router holds a side, it's also checked 30 s
/// after its first speech with whatever there is, so a side that barely
/// talks still gets transcribed.
struct LanguageProbe {
    /// 10 s of speech at 16 kHz (`TranscriptionEngine.languageProbeSamples`).
    static let full = 10 * 16000
    static let wait: TimeInterval = 30

    private(set) var gathered: [AudioSource: [Float]] = [:]
    private var firstSpeechAt: [AudioSource: Date] = [:]
    private var done: Set<AudioSource> = []
    /// Voiced samples to let pass before gathering again (see `rearm`).
    private var skip: [AudioSource: Int] = [:]

    /// Feed one buffer; returns the probe when this side is ready to check.
    mutating func add(_ samples: [Float], voiced: Bool, from source: AudioSource,
                      at now: Date, holding: Bool) -> [Float]? {
        guard !done.contains(source) else { return nil }
        if voiced, let left = skip[source], left > 0 {
            skip[source] = left - samples.count
            return nil
        }
        if voiced {
            if firstSpeechAt[source] == nil { firstSpeechAt[source] = now }
            gathered[source, default: []].append(contentsOf: samples)
        }
        let full = (gathered[source]?.count ?? 0) >= Self.full
        let waited = holding && firstSpeechAt[source].map { now.timeIntervalSince($0) >= Self.wait } == true
        return full || waited ? finish(source) : nil
    }

    /// The probe for `source` if it's due: the 30 s wait while the router
    /// holds this side, or anything at all when `force` (the call is
    /// stopping). Called by the loop, since a quiet side sends no voiced
    /// buffers that would reach `add`'s own wait check.
    mutating func take(_ source: AudioSource, at now: Date, force: Bool, holding: Bool = false) -> [Float]? {
        guard !done.contains(source) else { return nil }
        if force { return finish(source) }
        guard holding, let first = firstSpeechAt[source], now.timeIntervalSince(first) >= Self.wait else { return nil }
        return finish(source)
    }

    /// Gather this side again, once `skipping` more voiced samples have
    /// passed (0 = right away: an unsure answer; a minute: the next check).
    mutating func rearm(_ source: AudioSource, skipping: Int = 0) {
        done.remove(source)
        gathered[source] = nil
        firstSpeechAt[source] = nil
        skip[source] = skipping
    }

    private mutating func finish(_ source: AudioSource) -> [Float] {
        done.insert(source)
        let audio = gathered[source] ?? []
        gathered[source] = nil
        return audio
    }
}

/// 0.24.2's "sounds like Turkish, switch to Turkish" banner, kept up all
/// call: each side is checked again after every 30 s of its speech, so a
/// call that changes language is caught too. A language already offered or
/// used in this call is never offered again, so a bilingual call (you in
/// English, them in Turkish) can't bounce the banner between the two. Pure.
struct MismatchWatch {
    /// Speech to let pass before gathering again: 20 s here plus the 10 s the
    /// probe then gathers = a check every 30 s of a side's speech.
    static let recheckAfter = 20 * 16000
    static let unsureRetries = 3

    private(set) var used: Set<String>
    private var unsure: [AudioSource: Int] = [:]

    init(setting: String?) {
        used = setting.map { [$0] } ?? []
    }

    /// One check's result. `mismatch`: 0.24.2's rule, what it would offer.
    /// Returns the language to offer (if any) and how much more of this
    /// side's speech to let pass before checking it again.
    mutating func heard(_ source: AudioSource, mismatch: String?, confidence: Float) -> (offer: String?, recheckAfter: Int) {
        if let mismatch {
            guard !used.contains(mismatch) else { return (nil, Self.recheckAfter) }
            used.insert(mismatch)
            return (mismatch, Self.recheckAfter)
        }
        // Unsure: look again right away, a few times, before waiting a minute.
        if confidence < 0.6 {
            unsure[source, default: 0] += 1
            if unsure[source, default: 0] < Self.unsureRetries { return (nil, 0) }
        }
        return (nil, Self.recheckAfter)
    }

    /// The call switched language (the banner, or by hand).
    mutating func switched(to code: String) {
        used.insert(code)
    }
}
