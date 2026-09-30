# Parakeet Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parakeet v3 as a free on-device model next to Whisper, with each side of a call on Auto-detect held for its first ~10 s of speech, checked for language, then routed to Parakeet (its 25 languages) or a lazily loaded Whisper (everything else, Turkish included).

**Architecture:** Pure `LanguageRouter` (per-track route state) and `LanguageProbe` (per-track voiced-audio gathering) decide; `ParakeetTranscriber` wraps FluidAudio's `AsrManager`; `TranscriptionEngine` loads Parakeet + a Whisper Tiny detector, holds undecided tracks in their buffers, decodes by route, lazily loads the fallback Whisper, rechecks Parakeet tracks every 60 s of speech and rewinds through a new `onReplace` callback.

**Tech Stack:** Swift 5.10, WhisperKit (pinned), FluidAudio (pinned `5390df9`, `AsrManager` / `AsrModels.v3`), SwiftUI, `--profile-test` harness (`make test`).

## Global Constraints

- Branch `feat/parakeet-engine`, based on master `e5ead5f` (0.24.2). Spec: `docs/superpowers/specs/2026-09-30-parakeet-engine-design.md`.
- `make test` must end `ALL PASS`. `make test` builds release; run `swift build` before any `.build/debug` harness.
- Parakeet languages (exactly): bg, cs, da, de, el, en, es, et, fi, fr, hr, hu, it, lt, lv, mt, nl, pl, pt, ro, ru, sk, sl, sv, uk.
- Confidence bar: p ≥ 0.8 (0.24.2's `languageMismatch`). Probe: 10 s of voiced speech (`languageProbeSamples`). Timeout: 30 s after a track's first speech. Recheck: every 60 s of a track's Parakeet speech.
- Unsure or no answer → Whisper. Never drop or filter text by confidence; store Parakeet lines with `confidence: nil` (its 0-1 score isn't Whisper's log-prob).
- Model id `"parakeet-v3"` in the existing `whisperModel` setting. Fallback Whisper: `"large-v3-v20240930_626MB"`. Detector: `"tiny"`.
- 0.24.2's `languageMismatch(setting:backend:heard:confidence:)` rules and its `lang:` checks stay unchanged.
- Theme tokens only in views; short plain UI text; no new dependencies.

---

### Task 1: LanguageRouter and LanguageProbe (pure)

**Files:**
- Create: `Parrot/Services/LanguageRouter.swift`
- Create: `Parrot/ProfileTest+Parakeet.swift`
- Modify: `Parrot/ProfileTest.swift` (register tests; `check` must be `static`, not `private static`)

**Interfaces:**
- Produces: `LanguageRouter(parakeet:pinned:)`, `.Route { undecided, parakeet(language: String?), whisper }`, `.Change { decided(AudioSource, Route), switchedToWhisper(AudioSource) }`, `route(_:)`, `isHolding`, `heard(_:language:confidence:) -> Change?`, `decoded(_:seconds:) -> Bool`, `switchLanguage(to:)`, `static parakeetLanguages`, `static sure`, `static recheckEvery`.
- Produces: `LanguageProbe` with `add(_:voiced:from:at:holding:) -> [Float]?`, `take(_:at:force:holding:) -> [Float]?`, `rearm(_:)`, `static full`, `static wait`.

- [ ] **Step 1: Failing tests** in `Parrot/ProfileTest+Parakeet.swift`:

```swift
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

        check("router: pinned German → Parakeet, no hold", R(parakeet: true, pinned: "de").route(.me) == .parakeet(language: "de")
              && !R(parakeet: true, pinned: "de").isHolding)
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
        check("probe: other track is separate", p.take(.me, at: t0.addingTimeInterval(5), force: false) == nil)
        check("probe: 30 s wait while holding", p.take(.me, at: t0.addingTimeInterval(30), force: false, holding: true)?.count == 16000)
        var warn = LanguageProbe()
        _ = warn.add(second, voiced: true, from: .me, at: t0, holding: false)
        check("probe: no wait when not holding", warn.take(.me, at: t0.addingTimeInterval(60), force: false) == nil)
        check("probe: forced at stop takes what's there", warn.take(.me, at: t0, force: true)?.count == 16000)
        warn.rearm(.me)
        check("probe: rearm gathers again", warn.take(.me, at: t0, force: false) == nil
              && warn.add(Array(repeating: 0.1, count: LanguageProbe.full), voiced: true, from: .me, at: t0, holding: false) != nil)
    }
}
```

Register `testLanguageRouter()` and `testLanguageProbe()` before `print(failures == 0 ...)` in `ProfileTest.run()`.

- [ ] **Step 2: Run** `swift build 2>&1 | tail -3` → FAIL, `cannot find 'LanguageRouter'`.

- [ ] **Step 3: Implement** `Parrot/Services/LanguageRouter.swift`:

```swift
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
        /// A Parakeet track now hears a language Parakeet doesn't know: its
        /// lines since the last clean check get re-done with Whisper.
        case switchedToWhisper(AudioSource)
    }

    static let parakeetLanguages: Set<String> = [
        "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hr", "hu", "it",
        "lt", "lv", "mt", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "uk",
    ]
    /// 0.24.2's bar: real calls scored 0.93-1.00 on 10 s of speech.
    static let sure: Float = 0.8
    static let recheckEvery: TimeInterval = 60

    private(set) var routes: [AudioSource: Route]
    private var sinceCheck: [AudioSource: TimeInterval] = [:]

    init(parakeet: Bool, pinned: String?) {
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
            let decided: Route = supported && sure ? .parakeet(language: language) : .whisper
            routes[source] = decided
            sinceCheck[source] = 0
            return .decided(source, decided)
        case .parakeet:
            sinceCheck[source] = 0
            guard sure, language != nil, !supported else { return nil }
            routes[source] = .whisper
            return .switchedToWhisper(source)
        case .whisper:
            return nil
        }
    }

    /// Parakeet decoded `seconds` more of `source`; true when a recheck is due.
    mutating func decoded(_ source: AudioSource, seconds: TimeInterval) -> Bool {
        guard case .parakeet = route(source) else { return false }
        let total = (sinceCheck[source] ?? 0) + seconds
        sinceCheck[source] = total
        return total >= Self.recheckEvery
    }

    /// The user switched the call's language (0.24.2's banner). A Whisper
    /// track stays Whisper; the rest follow the new language.
    mutating func switchLanguage(to code: String) {
        let next: Route = Self.parakeetLanguages.contains(code) ? .parakeet(language: code) : .whisper
        for source in AudioSource.allCases where route(source) != .whisper {
            routes[source] = next
        }
    }
}

/// 0.24.2's language probe, per track: voiced audio gathered until there's
/// enough to ask Whisper what language it is. Each track is checked once
/// (plus `rearm`). While the router holds a track, it's also checked 30 s
/// after its first speech with whatever there is, so a side that barely
/// talks still gets transcribed.
struct LanguageProbe {
    /// 10 s of speech at 16 kHz (TranscriptionEngine.languageProbeSamples).
    static let full = 10 * 16000
    static let wait: TimeInterval = 30

    private(set) var gathered: [AudioSource: [Float]] = [:]
    private var firstSpeechAt: [AudioSource: Date] = [:]
    private var done: Set<AudioSource> = []

    /// Feed one buffer; returns the probe when this track is ready to check.
    mutating func add(_ samples: [Float], voiced: Bool, from source: AudioSource,
                      at now: Date, holding: Bool) -> [Float]? {
        guard !done.contains(source) else { return nil }
        if voiced {
            if firstSpeechAt[source] == nil { firstSpeechAt[source] = now }
            gathered[source, default: []].append(contentsOf: samples)
        }
        let full = (gathered[source]?.count ?? 0) >= Self.full
        let waited = holding && firstSpeechAt[source].map { now.timeIntervalSince($0) >= Self.wait } == true
        return full || waited ? finish(source) : nil
    }

    /// The probe for `source` if it's due: the 30 s wait while the router
    /// holds this track, or anything at all when `force` (the call is
    /// stopping). Called by the loop, since a quiet track sends no buffers
    /// that would reach `add`'s own wait check.
    mutating func take(_ source: AudioSource, at now: Date, force: Bool, holding: Bool = false) -> [Float]? {
        guard !done.contains(source) else { return nil }
        if force { return finish(source) }
        guard holding, let first = firstSpeechAt[source], now.timeIntervalSince(first) >= Self.wait else { return nil }
        return finish(source)
    }

    /// An inconclusive answer: gather this track again.
    mutating func rearm(_ source: AudioSource) {
        done.remove(source)
        gathered[source] = nil
        firstSpeechAt[source] = nil
    }

    private mutating func finish(_ source: AudioSource) -> [Float] {
        done.insert(source)
        let audio = gathered[source] ?? []
        gathered[source] = nil
        return audio
    }
}
```

- [ ] **Step 4: Run** `make test 2>&1 | grep -E 'router:|recheck:|switch:|probe:|ALL PASS|FAIL'` → all PASS.
- [ ] **Step 5: Commit** `git add -A Parrot && git commit -m "Parakeet: the language router and the per-track probe"`

---

### Task 2: EngineRecommendation and the model id

**Files:**
- Create: `Parrot/Services/EngineRecommendation.swift`
- Create: `Parrot/Services/ParakeetTranscriber.swift` (only the static members in this task)
- Modify: `Parrot/Services/TranscriptionEngine.swift` (`displayName(for:)`)
- Test: `testEngineRecommendation` in `ProfileTest+Parakeet.swift`

**Interfaces:**
- Produces: `EngineRecommendation.recommend(preferredLanguages: [String], pastCallLanguages: [String], memoryGB: Int) -> String`; `EngineRecommendation.pastCallLanguages(_ texts: [String]) -> [String]`; `ParakeetTranscriber.modelID = "parakeet-v3"`, `ParakeetTranscriber.displayName = "Parakeet v3"`; `TranscriptionEngine.isParakeet(_ modelName: String) -> Bool`, `TranscriptionEngine.fallbackWhisper = "large-v3-v20240930_626MB"`, `TranscriptionEngine.detectorModel = "tiny"`.

- [ ] **Step 1: Failing test** (register):

```swift
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
        check("recommend: no languages at all → Whisper", E.recommend(preferredLanguages: [], pastCallLanguages: [], memoryGB: 16) == "large-v3-turbo")
        let langs = E.pastCallLanguages(["Merhaba, bugün fiyatları konuşalım mı? Teklifinizi aldık.",
                                         "Thanks for joining, let's go through the pricing today."])
        check("recommend: reads past call languages", Set(langs) == ["tr", "en"])
        check("parakeet: model id is recognised", TranscriptionEngine.isParakeet("parakeet-v3") && !TranscriptionEngine.isParakeet("base"))
        check("parakeet: display name", TranscriptionEngine.displayName(for: "parakeet-v3") == "Parakeet v3")
    }
```

- [ ] **Step 2: Run** `swift build 2>&1 | tail -3` → FAIL.

- [ ] **Step 3: Implement.** `Parrot/Services/EngineRecommendation.swift`:

```swift
import Foundation
import NaturalLanguage

/// Which on-device model a new install starts on. Parakeet when every
/// language this person uses is one of its 25; otherwise the Whisper that
/// fits the Mac's memory, because Parakeet can't transcribe the rest.
enum EngineRecommendation {
    static func recommend(preferredLanguages: [String], pastCallLanguages: [String], memoryGB: Int) -> String {
        let codes = preferredLanguages.map { Locale.Language(identifier: $0).languageCode?.identifier ?? $0 }
            + pastCallLanguages
        guard !codes.isEmpty, codes.allSatisfy(LanguageRouter.parakeetLanguages.contains) else {
            return MachineFit.whisperModel(memoryGB: memoryGB)
        }
        return ParakeetTranscriber.modelID
    }

    /// The dominant language of each saved transcript text (on-device).
    static func pastCallLanguages(_ texts: [String]) -> [String] {
        texts.compactMap { text in
            let recognizer = NLLanguageRecognizer()
            recognizer.processString(String(text.prefix(2000)))
            return recognizer.dominantLanguage?.rawValue
        }
    }
}
```

`Parrot/Services/ParakeetTranscriber.swift` (statics only for now):

```swift
import FluidAudio
import Foundation

/// NVIDIA Parakeet TDT 0.6B v3 via FluidAudio: 25 European languages on the
/// Neural Engine, about 0.5 GB. FluidAudio's ASR API stays in this file.
/// (Write `Language`, not `FluidAudio.Language`: FluidAudio also declares a
/// `struct FluidAudio`, which shadows the module name.)
final class ParakeetTranscriber: Sendable {
    static let modelID = "parakeet-v3"
    static let displayName = "Parakeet v3"
}
```

In `TranscriptionEngine`: add `case ParakeetTranscriber.modelID: ParakeetTranscriber.displayName` to `displayName(for:)`, and near it:

```swift
    nonisolated static func isParakeet(_ modelName: String) -> Bool { modelName == ParakeetTranscriber.modelID }
    /// The Whisper a Parakeet call switches to for other languages.
    nonisolated static let fallbackWhisper = "large-v3-v20240930_626MB"
    /// Whisper's language check while Parakeet is the model.
    nonisolated static let detectorModel = "tiny"
```

- [ ] **Step 4: Run** `make test 2>&1 | grep -E 'recommend:|parakeet:|ALL PASS|FAIL'` → PASS. If the Turkish sample isn't recognised as `tr`, lengthen it; don't loosen the check.
- [ ] **Step 5: Commit** `git add -A Parrot && git commit -m "Parakeet: recommend it only when every language fits"`

---

### Task 3: ParakeetTranscriber (FluidAudio) and loading

**Files:**
- Modify: `Parrot/Services/ParakeetTranscriber.swift`
- Modify: `Parrot/Services/TranscriptionEngine.swift` (`performLoad`, new `makeWhisperKit`, `ensureWhisper`, `prepareFallback`, properties, `stopTranscribing`)

**Interfaces:**
- Produces: `ParakeetTranscriber.load(progress: @Sendable (Double) -> Void) async throws -> ParakeetTranscriber`, `transcribe(_ samples: [Float], language: String?) async throws -> String`, `static var isDownloaded: Bool`; `TranscriptionEngine.parakeet: ParakeetTranscriber?`, `detector: WhisperKit?` (computed), `ensureWhisper() async -> WhisperKit?`, `currentModel: String`.

- [ ] **Step 1: Implement the wrapper:**

```swift
final class ParakeetTranscriber: Sendable {
    static let modelID = "parakeet-v3"
    static let displayName = "Parakeet v3"

    private let manager: AsrManager

    private init(manager: AsrManager) { self.manager = manager }

    static var isDownloaded: Bool {
        AsrModels.modelsExist(at: AsrModels.defaultCacheDirectory(for: .v3), version: .v3)
    }

    /// Downloads on first use (about 0.5 GB), then loads onto the Neural Engine.
    static func load(progress: @escaping @Sendable (Double) -> Void) async throws -> ParakeetTranscriber {
        let models = try await AsrModels.downloadAndLoad(version: .v3) { progress($0.fractionCompleted) }
        let manager = AsrManager(config: .default)
        try await manager.loadModels(models)
        return ParakeetTranscriber(manager: manager)
    }

    /// One utterance (16 kHz mono). `language`: the ISO code heard, used by
    /// Parakeet as a script filter (Latin, Cyrillic, Greek); nil = any.
    func transcribe(_ samples: [Float], language: String?) async throws -> String {
        var state = TdtDecoderState.make(decoderLayers: await manager.decoderLayerCount)
        let result = try await manager.transcribe(samples, decoderState: &state,
                                                  language: language.flatMap(Language.init(rawValue:)))
        return result.text
    }
}
```

- [ ] **Step 2: Refactor loading.** Extract the WhisperKit download + init from `performLoad` into:

```swift
    /// Download (if missing) and load one WhisperKit model.
    nonisolated static func makeWhisperKit(_ modelName: String,
                                           progress: (@Sendable (Double) -> Void)? = nil) async throws -> WhisperKit {
        let resolved = hubVariant(for: modelName)
        let folder: URL
        if let local = localModelFolder(for: modelName) {
            folder = local
        } else {
            folder = try await withStallTimeout(seconds: 60) { tick in
                try await WhisperKit.download(variant: resolved) { p in
                    let fraction = min(max(p.fractionCompleted, 0), 1)
                    tick(fraction)
                    progress?(fraction)
                }
            }
        }
        let config = WhisperKitConfig(model: resolved, modelFolder: folder.path, verbose: false, logLevel: .none,
                                      prewarm: true, load: true, download: false)
        return try await withTimeout(seconds: 300) { try await WhisperKit(config) }
    }
```

Keep `performLoad`'s existing states (`.downloading(progress:)`, `.loading`, generation checks) by calling it with a progress closure that hops to MainActor as today. Then add the Parakeet branch at the top of `performLoad`:

```swift
        currentModel = modelName
        if Self.isParakeet(modelName) {
            modelState = Self.isParakeetReady ? .loading : .downloading(progress: 0)
            let parakeet = try await ParakeetTranscriber.load { fraction in
                Task { @MainActor [weak self] in
                    guard let self, self.loadGeneration == generation,
                          case .downloading(let current) = self.modelState else { return }
                    self.modelState = .downloading(progress: max(current, fraction))
                }
            }
            let detector = try? await Self.makeWhisperKit(Self.detectorModel)
            guard loadGeneration == generation else { return }
            self.parakeet = parakeet
            tinyDetector = detector
            whisperKit = nil        // loaded again only when a call needs it
            modelState = .ready
            isReady = true
            Task { await self.loadSpeechDetector() }
            Task.detached(priority: .utility) { await Self.prepareFallback() }
            return
        }
        parakeet = nil
        tinyDetector = nil
```

(Wrap in the existing `do/catch`; `isParakeetReady` = `ParakeetTranscriber.isDownloaded`.) Properties:

```swift
    /// The model the user picked (Whisper tag or `ParakeetTranscriber.modelID`).
    private(set) var currentModel = ""
    private var parakeet: ParakeetTranscriber?
    private var tinyDetector: WhisperKit?
    private var whisperLoad: Task<WhisperKit?, Never>?

    /// What answers "which language is this?": the loaded Whisper, or Tiny
    /// while Parakeet is the model (English-only models can't tell).
    private var detector: WhisperKit? { parakeet != nil ? tinyDetector : whisperKit }
```

Lazy load and the one-time preparation:

```swift
    /// The Whisper to decode with. On Parakeet it's the fallback, loaded the
    /// first time a track needs it (seconds, thanks to `prepareFallback`);
    /// the track's audio waits in its buffer meanwhile.
    // ponytail: only the transcription loop and imports call this, never at once.
    func ensureWhisper() async -> WhisperKit? {
        if let whisperKit { return whisperKit }
        if let whisperLoad { return await whisperLoad.value }
        let load = Task { try? await Self.makeWhisperKit(Self.fallbackWhisper) }
        whisperLoad = load
        let kit = await load.value
        whisperKit = kit
        whisperLoad = nil
        return kit
    }

    /// Download the fallback and load it once, so macOS prepares it for the
    /// Neural Engine now (the first load can take minutes) instead of mid-call.
    nonisolated static func prepareFallback() async {
        let key = "parakeetFallbackPrepared"
        guard UserDefaults.standard.string(forKey: key) != fallbackWhisper else { return }
        guard (try? await makeWhisperKit(fallbackWhisper)) != nil else { return }
        UserDefaults.standard.set(fallbackWhisper, forKey: key)
    }
```

In `stopTranscribing`, after the drain (`transcriptionTask = nil`): `if parakeet != nil { whisperKit = nil }` (memory back after the call).

`startTranscribing` currently guards `isReady`; unchanged. `RecordingError.modelNotReady` text becomes "The speech model is still loading. Please wait."

- [ ] **Step 3: Build** `make test 2>&1 | tail -2` → `ALL PASS` (no behaviour change for Whisper users).
- [ ] **Step 4: Commit** `git add -A Parrot && git commit -m "Parakeet: load it with a Tiny language check; Whisper loads on demand"`

---

### Task 4: Per-track probe feeds the router and the 0.24.2 warning

**Files:**
- Modify: `Parrot/Services/TranscriptionEngine.swift` (`languageProbe` → `probe: LanguageProbe?`, `router`, `appendAudio`, `startTranscribing`, `checkLanguage`, `switchLanguage`, `stopTranscribing`)
- Test: `testProbeWarning`

**Interfaces:**
- Produces: `TranscriptionEngine.router` (bufferLock-guarded `LanguageRouter`), `checkLanguage(_ samples: [Float], source: AudioSource) async`, `static func noticeFor(_ change: LanguageRouter.Change, heard: String?) -> String?`.

- [ ] **Step 1: Failing test** (register):

```swift
    static func testProbeWarning() {
        typealias T = TranscriptionEngine
        check("notice: Turkish on their side", T.noticeFor(.decided(.them, .whisper), heard: "tr") == "Turkish heard: using Whisper for them")
        check("notice: unsure on your side", T.noticeFor(.decided(.me, .whisper), heard: nil) == "Language unclear: using Whisper for you")
        check("notice: Parakeet decided → none", T.noticeFor(.decided(.me, .parakeet(language: "en")), heard: "en") == nil)
        check("notice: mid-call switch", T.noticeFor(.switchedToWhisper(.them), heard: "tr") == "Turkish heard: using Whisper for them from here")
    }
```

- [ ] **Step 2: Implement.** Replace `languageProbe: [AudioSource: [Float]]?` with `private var probe: LanguageProbe?` (nil = off) and add `private var router = LanguageRouter(parakeet: false, pinned: nil)`, both guarded by `bufferLock`. In `startTranscribing`, where `sessionLanguage = language` and `languageProbe = [:]` are set:

```swift
            probe = LanguageProbe()
            router = LanguageRouter(parakeet: parakeet != nil && backend == .local, pinned: language)
```

and after the lock, when `router.isHolding`: `cloudNotice = "Checking the language…"`. If Parakeet is the model and the backend is local with a pinned unsupported language, `cloudNotice = "\(TranscriptionLanguage.name(code)) isn't a Parakeet language: using Whisper"`.

In `appendAudio`, replace the probe block:

```swift
        if isTranscribing, frameCount > 0 {
            let energy = samples.reduce(into: Float(0)) { $0 += abs($1) } / Float(frameCount)
            let full: [Float]? = bufferLock.withLock {
                probe?.add(samples, voiced: energy > Segmenter.silenceFloor, from: source, at: Date(),
                           holding: router.route(source) == .undecided)
            }
            if let full { Task { await self.checkLanguage(full, source: source) } }
        }
```

`checkLanguage` becomes per track and feeds both consumers:

```swift
    private func checkLanguage(_ samples: [Float], source: AudioSource) async {
        var heard: String?
        var confidence: Float = 0
        if let detector, !samples.isEmpty,
           let result = try? await detector.detectLangauge(audioArray: Self.normalizedForDecode(samples)) {
            heard = result.language
            confidence = exp(result.langProbs[result.language] ?? -.infinity)
        }
        let (setting, backend, change, holding) = bufferLock.withLock {
            let wasHolding = router.route(source) == .undecided
            let change = router.heard(source, language: heard, confidence: confidence)
            return (sessionLanguage, sessionBackend, change, wasHolding)
        }
        AudioCaptureManager.oslog.info("Language check \(source.label, privacy: .public): heard \(heard ?? "-", privacy: .public) p=\(confidence, privacy: .public)")
        if let change, let notice = Self.noticeFor(change, heard: heard) {
            await MainActor.run { if self.isTranscribing { self.cloudNotice = notice } }
        } else if holding {
            let stillHolding = bufferLock.withLock { router.isHolding }
            if !stillHolding { await MainActor.run { if self.cloudNotice == "Checking the language…" { self.cloudNotice = nil } } }
        }
        guard !holding, let heard else { return }   // a routing check isn't a warning check
        if let mismatch = Self.languageMismatch(setting: setting, backend: backend, heard: heard, confidence: confidence) {
            await MainActor.run { if self.isTranscribing, self.languageMismatch == nil { self.languageMismatch = mismatch } }
        } else if confidence < 0.6 {
            await MainActor.run {
                self.languageChecks += 1
                if self.languageChecks < 3, self.isTranscribing { self.bufferLock.withLock { self.probe?.rearm(source) } }
            }
        }
    }

    static func noticeFor(_ change: LanguageRouter.Change, heard: String?) -> String? {
        let name = heard.map(TranscriptionLanguage.name)
        switch change {
        case .decided(let s, .whisper):
            let side = s == .me ? "you" : "them"
            return name.map { "\($0) heard: using Whisper for \(side)" } ?? "Language unclear: using Whisper for \(side)"
        case .switchedToWhisper(let s):
            return "\(name ?? "Another language") heard: using Whisper for \(s == .me ? "you" : "them") from here"
        default:
            return nil
        }
    }
```

(`languageMismatch == nil` keeps "first conclusive track wins". `TranscriptionLanguage.name` returns the code for languages outside the picker; acceptable.)

`switchLanguage(to:)`: inside its existing `bufferLock.withLock { sessionLanguage = code }` also call `router.switchLanguage(to: code)`. In `stopTranscribing`, the old `languageProbe = nil` becomes nothing (the probe is still needed for the drain's forced decisions in Task 5); set `probe = nil` after the drain instead.

Update `--language-test` (SnapshotTool) only if it referenced `languageProbe`; it uses `languageProbeSamples`, which stays.

- [ ] **Step 3: Run** `make test 2>&1 | grep -E 'notice:|lang:|ALL PASS|FAIL'` → PASS, 0.24.2's `lang:` checks unchanged.
- [ ] **Step 4: Commit** `git commit -am "Language check: every track is checked, and it routes Parakeet calls"`

---

### Task 5: The loop holds, then decodes by route

**Files:**
- Modify: `Parrot/Services/TranscriptionEngine.swift` (transcription loop: per-source hold, forced decisions while draining, `decodeLocally`, preview)

- [ ] **Step 1: Hold undecided tracks.** At the start of the `for source in AudioSource.allCases` body, before the cut:

```swift
                    // Parakeet on Auto-detect: this side's language isn't known
                    // yet, so its audio waits in the buffer (Whisper needs ~10 s
                    // of speech to be sure). Past 30 s of waiting it's checked
                    // with what there is; while stopping, right away.
                    if self.bufferLock.withLock({ self.router.route(source) }) == .undecided {
                        let due = self.bufferLock.withLock { self.probe?.take(source, at: Date(), force: draining, holding: true) }
                        if let due {
                            await self.checkLanguage(due, source: source)
                        } else if draining {
                            _ = self.bufferLock.withLock { self.router.heard(source, language: nil, confidence: 0) }
                        }
                        if self.bufferLock.withLock({ self.router.route(source) }) == .undecided { continue }
                    }
```

(The inline `await checkLanguage` on the drain path makes sure a stopping call never exits with held audio. On the live path the probe's own `add` fires the check from `appendAudio`.)

- [ ] **Step 2: Decode by route.** Replace the start of `decodeLocally()`:

```swift
                    func decodeLocally() async throws -> [(text: String, confidence: Float?)] {
                        let route = self.bufferLock.withLock { self.router.route(source) }
                        if case .parakeet(let language) = route, let parakeet = self.parakeet {
                            let text = try await parakeet.transcribe(decodeSamples, language: language)
                            self.keepForRecheck(chunk, source: source, start: startTime, end: endTime)
                            // Parakeet's 0-1 score isn't Whisper's log-prob; don't mix them.
                            return [(text, nil)]
                        }
                        guard let whisperKit = await self.ensureWhisper() else { return [] }
                        // … existing Whisper body unchanged, using this `whisperKit` …
```

`keepForRecheck` is added in Task 6; stub it now as an empty `private func keepForRecheck(_ samples: [Float], source: AudioSource, start: TimeInterval, end: TimeInterval) {}`.

- [ ] **Step 3: Preview by route.** In the rolling-preview branch, replace `if voiced, let whisperKit = self.whisperKit {` with a route check: `.undecided` → no preview; `.parakeet(let language)` → `let text = (try? await parakeet.transcribe(Self.normalizedForDecode(pending), language: language)) ?? ""` then the same `display`/`currentText` update and pacing; `.whisper` → the existing Whisper preview only if `self.whisperKit` is already loaded (a preview never triggers the lazy load).

- [ ] **Step 4: Harness check.** `swift build`, then on a Turkish+English recording (the Sep 30 call's `.caf`, or any mixed file):

```bash
PARROT_LOOP_TRACE=1 .build/debug/Parrot --liveloop-test /path/mixed.caf parakeet-v3
```

Expected: "Checking the language…", then English lines from Parakeet and Turkish ones from Whisper, no European-looking lines over Turkish speech. On an English-only file, Whisper is never loaded (no fallback load line). Note decode speed against `large-v3-turbo` on the same file.

- [ ] **Step 5: Run** `make test 2>&1 | tail -2` → `ALL PASS`. **Commit** `git commit -am "Parakeet calls: hold each side until its language is known, then route"`

---

### Task 6: Recheck and rewind

**Files:**
- Modify: `Parrot/Services/TranscriptionEngine.swift` (`keepForRecheck`, recheck, `onReplace`)
- Modify: `Parrot/Models/Meeting.swift` (`segmentIDs(label:in:)`)
- Modify: `Parrot/Services/RecordingManager.swift` (wire `onReplace`)
- Test: `testRewindRange`

**Interfaces:**
- Produces: `TranscriptionEngine.onReplace: ((AudioSource, ClosedRange<TimeInterval>, [TranscriptionResult]) -> Void)?`, `Meeting.segmentIDs(label: String, in range: ClosedRange<TimeInterval>) -> [UUID]`, `static func recheckAudio(_ clips: [[Float]], seconds: Int) -> [Float]`.

- [ ] **Step 1: Failing test** (register):

```swift
    static func testRewindRange() {
        let m = Meeting(title: "rewind")
        let lines = [(10.0, "Them"), (40.0, "Them"), (40.0, "Me"), (95.0, "Them")].map { t, label in
            TranscriptSegment(startTime: t, endTime: t + 3, text: "x", speakerLabel: label)
        }
        m.segments = lines
        check("rewind: only that side, only that stretch",
              Set(m.segmentIDs(label: "Them", in: 30...90)) == [lines[1].id])
        let clips: [[Float]] = [Array(repeating: 1, count: 16000 * 6), Array(repeating: 2, count: 16000 * 6)]
        let audio = TranscriptionEngine.recheckAudio(clips, seconds: 10)
        check("rewind: recheck uses the latest 10 s", audio.count == 16000 * 10 && audio.last == 2 && audio.first == 1)
    }
```

- [ ] **Step 2: Implement.**

`Meeting`:

```swift
    /// Lines from one side whose start falls in `range` (a rewind replaces them).
    func segmentIDs(label: String, in range: ClosedRange<TimeInterval>) -> [UUID] {
        segments.filter { $0.speakerLabel == label && range.contains($0.startTime) }.map(\.id)
    }
```

`TranscriptionEngine`:

```swift
    /// A Parakeet side's lines since its last clean language check, with
    /// their audio, so a late switch to Whisper can re-do them. Loop-only.
    private var kept: [AudioSource: [(start: TimeInterval, end: TimeInterval, audio: [Float])]] = [:]
    /// Replace one side's lines in a time range (a rewind), same hop as onSegment.
    var onReplace: ((AudioSource, ClosedRange<TimeInterval>, [TranscriptionResult]) -> Void)?

    nonisolated static func recheckAudio(_ clips: [[Float]], seconds: Int) -> [Float] {
        Array(clips.joined().suffix(seconds * 16000))
    }

    /// Sides due a language recheck; the loop runs it before that side's
    /// next cut, so a recheck never races the decode it might redo.
    private var pendingRecheck: Set<AudioSource> = []

    private func keepForRecheck(_ samples: [Float], source: AudioSource, start: TimeInterval, end: TimeInterval) {
        kept[source, default: []].append((start, end, samples))
        // ponytail: 90 s cap per side, so failed checks can't grow memory.
        while (kept[source]?.reduce(0) { $0 + $1.audio.count } ?? 0) > 90 * 16000 { kept[source]?.removeFirst() }
        if bufferLock.withLock({ router.decoded(source, seconds: end - start) }) { pendingRecheck.insert(source) }
    }
```

In the loop, right after the hold block from Task 5 and before the cut:

```swift
                    if self.pendingRecheck.remove(source) != nil { await self.recheck(source) }
```

The recheck itself:

```swift
    private func recheck(_ source: AudioSource) async {
        let clips = kept[source] ?? []
        await checkLanguage(Self.recheckAudio(clips.map(\.audio), seconds: 10), source: source)
        guard bufferLock.withLock({ router.route(source) }) == .whisper else {
            kept[source] = []            // clean: these lines are verified
            return
        }
        kept[source] = nil
        guard let first = clips.first, let last = clips.last, let whisper = await ensureWhisper() else { return }
        var redone: [TranscriptionResult] = []
        for clip in clips {
            let pieces = (try? await whisper.transcribe(audioArray: Self.normalizedForDecode(clip.audio),
                                                        decodeOptions: baseDecodeOptions)) ?? []
            let text = Self.cleaned(pieces.map(\.text).joined(separator: " "))
            if !text.isEmpty {
                redone.append(TranscriptionResult(text: text, source: source, startTime: clip.start,
                                                  endTime: clip.end, confidence: nil))
            }
        }
        await MainActor.run { self.onReplace?(source, first.start...last.end, redone) }
    }
```

(`baseDecodeOptions`: store the session's `decodeOptions` in a property in `startTranscribing` so `recheck` can use it; auto-detect, no glossary.) Clear `kept` and `pendingRecheck` in `startTranscribing` and after the drain.

`RecordingManager`, next to `onSegment`:

```swift
        transcriptionEngine.onReplace = { [weak self] source, range, results in
            Task { @MainActor in self?.replaceSegments(source: source, in: range, with: results) }
        }
```

```swift
    /// A rewind: one side's lines in `range` came from the wrong model.
    private func replaceSegments(source: AudioSource, in range: ClosedRange<TimeInterval>,
                                 with results: [TranscriptionEngine.TranscriptionResult]) {
        guard let modelContext, let meeting = currentMeeting else { return }
        let ids = Set(meeting.segmentIDs(label: source.label, in: range))
        for segment in meeting.segments where ids.contains(segment.id) { modelContext.delete(segment) }
        results.forEach(addSegment)
        try? modelContext.save()
    }
```

- [ ] **Step 3: Run** `make test 2>&1 | grep -E 'rewind:|ALL PASS|FAIL'` → PASS. **Commit** `git add -A Parrot && git commit -m "Parakeet calls: recheck every minute; a switch re-does the last stretch with Whisper"`

---

### Task 7: Imported files on Parakeet

**Files:**
- Modify: `Parrot/Services/TranscriptionEngine.swift` (`transcribeFile`, `importWindows`, `importUsesParakeet`)
- Test: `testImportRoute`

- [ ] **Step 1: Failing test** (register):

```swift
    static func testImportRoute() {
        typealias T = TranscriptionEngine
        check("import: three windows", T.importWindows(sampleCount: 16000 * 600).count == 3)
        check("import: a short file is one window", T.importWindows(sampleCount: 16000 * 20).count == 1)
        check("import: all English → Parakeet", T.importUsesParakeet([("en", 0.99), ("en", 0.95), ("en", 0.97)]))
        check("import: any Turkish → Whisper", !T.importUsesParakeet([("en", 0.99), ("tr", 0.99), ("en", 0.97)]))
        check("import: any unsure → Whisper", !T.importUsesParakeet([("en", 0.99), ("en", 0.5)]))
        check("import: nothing heard → Whisper", !T.importUsesParakeet([]))
    }
```

- [ ] **Step 2: Implement:**

```swift
    /// Start, middle and end: 30 s each (one window for short files).
    nonisolated static func importWindows(sampleCount: Int) -> [Range<Int>] {
        let window = 30 * 16000
        guard sampleCount > window * 3 else { return [0 ..< sampleCount] }
        let mid = sampleCount / 2 - window / 2
        return [0 ..< window, mid ..< mid + window, sampleCount - window ..< sampleCount]
    }

    nonisolated static func importUsesParakeet(_ verdicts: [(language: String?, confidence: Float)]) -> Bool {
        !verdicts.isEmpty && verdicts.allSatisfy { v in
            v.language.map(LanguageRouter.parakeetLanguages.contains) == true && v.confidence >= LanguageRouter.sure
        }
    }
```

In `transcribeFile`: if `parakeet == nil`, the existing Whisper path unchanged (now via `guard let whisperKit = await ensureWhisper()`). If Parakeet: load the file's samples (`AudioProcessor.loadAudio` + `convertBufferToArray`); on Auto-detect, detect each window with `detector`; `importUsesParakeet` false → the existing Whisper path; true (or a pinned Parakeet language) → cut utterances with `Segmenter.nextCut(in:draining: true)` over the samples (the live loop's cutter, so timestamps come from sample offsets), `parakeet.transcribe` each, keep the existing `speechTimeline`/`hasVoice` filter, and return `TranscriptionResult`s with `source: .them`, `confidence: nil`.

- [ ] **Step 3: Run** `make test 2>&1 | grep -E 'import:|ALL PASS|FAIL'` → PASS. **Commit** `git commit -am "Parakeet: imported files are checked at the start, middle and end"`

---

### Task 8: Settings and onboarding

**Files:**
- Modify: `Parrot/Views/SettingsView.swift` (Engine label, model picker, caption, Custom Vocabulary note)
- Modify: `Parrot/Services/CloudTranscription.swift` (`TranscriptionBackend.local.label`)
- Modify: `Parrot/Views/Onboarding/SpeechModelStep.swift`

- [ ] **Step 1: Settings.** `TranscriptionBackend.local.label` → "On-device". In the Engine card's local row text: "On-device, private, free". Model picker, first item:

```swift
                        Text("Parakeet v3 — 0.5 GB, fastest, 25 European languages (no Turkish)")
                            .tag(ParakeetTranscriber.modelID)
                            .selectionDisabled(!parakeetFitsLanguage)
```

with `private var parakeetFitsLanguage: Bool { transcriptionLanguage == "auto" || LanguageRouter.parakeetLanguages.contains(transcriptionLanguage) }`. Under the picker, when `selectedModel == ParakeetTranscriber.modelID`: a caption row "Calls in other languages, like Turkish, switch to Whisper Large V3 Turbo Compressed on their own." (`Theme.Typography.caption`, `Theme.Colors.ink2`); when the language is pinned outside Parakeet's list: "Parakeet can't do \(TranscriptionLanguage.name(transcriptionLanguage)). Calls use Whisper." Custom Vocabulary title → "Names and jargon Whisper mis-hears (Whisper only)".

- [ ] **Step 2: Onboarding.** In `SpeechModelStep`: add `(ParakeetTranscriber.modelID, "~0.5 GB", "Fastest, 25 European languages (no Turkish)")` first in `modelChoices`; add it to `shortList`; the button reads "Show all \(Self.modelChoices.count) models"; `recommended` becomes:

```swift
        let recommended = EngineRecommendation.recommend(
            preferredLanguages: Locale.preferredLanguages,
            pastCallLanguages: EngineRecommendation.pastCallLanguages(recentTranscripts),
            memoryGB: memoryGB)
```

with `@Query(sort: \Meeting.date, order: .reverse) private var meetings: [Meeting]` and `recentTranscripts = meetings.prefix(20).map { $0.sortedSegments.prefix(40).map(\.text).joined(separator: " ") }` (empty on a fresh install; filled when the welcome tour is re-run).

- [ ] **Step 3: Snapshot.** `swift build && .build/debug/Parrot --help-shots /tmp/parakeet-shots` (the existing help-shot harness renders Settings and onboarding pages); check the Transcription page and the speech-model step in light and dark.
- [ ] **Step 4: Run** `make test 2>&1 | tail -2` → `ALL PASS`. **Commit** `git commit -am "Settings and onboarding: Parakeet next to Whisper"`

---

### Task 9: Docs, detector accuracy, final verification

**Files:**
- Modify: `FILEMAP.md` (rows for `LanguageRouter.swift`, `EngineRecommendation.swift`, `ParakeetTranscriber.swift`, `ProfileTest+Parakeet.swift`; update the `TranscriptionEngine.swift` row)
- Modify: `docs/help/getting-started.html` (the model list), and the transcription settings help if it lists models
- Regenerate: `make xcode`

- [ ] **Step 1: Tiny's accuracy.** Run 0.24.2's check with Tiny on the same real tracks it was measured on:

```bash
.build/debug/Parrot --language-test /path/track.caf ~/Documents/huggingface/models/argmaxinc/whisperkit-coreml/openai_whisper-tiny
```

Expected: the same language as with the larger model, p ≥ 0.8, on every track. If any track falls short, change `detectorModel` to `"base"` and re-run; note the result in the PR.
- [ ] **Step 2: Docs.** Help: Parakeet in the model list with "25 European languages (no Turkish)" and one short paragraph on what happens with other languages. FILEMAP rows. `make xcode`.
- [ ] **Step 3:** `make test` → `ALL PASS`; `make` → `dist/Parrot.app`.
- [ ] **Step 4: Commit** `git add -A && git commit -m "Docs: Parakeet"`

## Manual checks (owner, needs real calls)

- An English call on Parakeet + Auto: lines appear ~10 s late at the start, then normally; Whisper never loads (Activity Monitor memory stays low).
- A Turkish call on Parakeet + Auto: "Turkish heard: using Whisper for them", Turkish lines correct, the first lines arrive a few seconds after the switch.
- A call that starts in English and moves to Turkish: within about a minute, "…from here", and the last stretch is re-done in Turkish.
