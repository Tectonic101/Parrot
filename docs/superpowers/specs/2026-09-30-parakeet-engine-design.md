# Parakeet as a second on-device model: design

Date: 2026-09-30. Status: **approved in brainstorming, not built.** Base:
master at `d21304a`. It touches `TranscriptionEngine` and the Transcription
settings page, which the live-nudges branch (`feat/live-nudges-tone-timeline`)
also edits; whichever lands second rebases.

## 1. Problem

Parrot's only free on-device engine is Whisper (WhisperKit). NVIDIA's
Parakeet TDT 0.6B v3 is faster, makes up less text on silence, and is about
as accurate as Whisper Large V3 Turbo, but it knows only 25 European
languages. **Turkish is not one of them.** On Turkish audio it doesn't stay
quiet: it writes confident-looking European words. Offering it naively would
silently ruin the owner's own calls (same class of risk as
the confidence-floor trap).

## 2. Decisions (with the user)

| Question | Choice |
|---|---|
| Add Parakeet? | **Yes, free, next to Whisper**, labelled "25 European languages, no Turkish" |
| Where it lives | **A model in the On-Device Model list**, not a new engine |
| Auto-detect + a Turkish call | **Check each utterance's language first; route unsupported ones to Whisper** |
| Whisper while on Parakeet | **Loaded only when an unsupported language first appears** (downloaded ahead, loaded lazily) |
| Default | **Recommended from the Mac's languages and past calls**; existing users keep their choice |

## 3. Facts this rests on

- Parakeet v3 ships in the FluidAudio version already pinned:
  `AsrModels.downloadAndLoad(version: .v3)` + `AsrManager.transcribe(_:decoderState:language:)`,
  returning text, confidence and token timings. No new dependency.
- Its `language:` argument is a script filter (Latin / Cyrillic / Greek),
  not language ID. There is no language in its output.
- Its 25 languages: bg, cs, da, de, el, en, es, et, fi, fr, hr, hu, it, lt,
  lv, mt, nl, pl, pt, ro, ru, sk, sl, sv, uk.
- WhisperKit (pinned) exposes `detectLangauge(audioArray:)` (sic) →
  `(language, langProbs)` over 99 languages, one encoder pass plus one
  decoder step.

## 4. What the user sees

**Settings → Transcription.** Engine card: "On-device — private, free",
Groq, Deepgram (was "On-device Whisper"). On-Device Model card:

```
Parakeet v3 — about 0.6 GB, fastest · 25 European languages (no Turkish)
── Whisper · 99 languages ──
Tiny / Base / Small / Large V3 Turbo Compressed / Large V3 Turbo
```

With Parakeet picked, one line under the list: "Other languages, like
Turkish, switch to Whisper Large V3 Turbo Compressed on their own."

**Language setting.** If it's set to a language Parakeet doesn't know
(Turkish, Arabic, Chinese, Japanese, Korean, Hindi), the Parakeet row is
disabled with the reason. A hand-picked Parakeet language skips the check.

**Custom Vocabulary** gets "Whisper only" (Parakeet has no prompt).

**Onboarding model step.** Two cards instead of five:
- **Parakeet** — "Fastest. English and 24 other European languages."
- **Whisper Turbo (compressed)** — "99 languages, including Turkish."

The recommended one is preselected; "More models in Settings" below.

**During a call**, the first switch shows the existing notice line:
"Turkish detected: using Whisper for them" (or "for you").

## 5. The language check (Parakeet + Auto-detect only)

A pure `LanguageGate` decides, per track (you / them):

1. Every utterance, before decoding, goes to Whisper **Tiny**'s language
   detector (40 MB, loaded with Parakeet). Not the main Whisper model: its
   encoder pass costs nearly a full decode and would erase Parakeet's speed.
2. **Route that utterance:** top language in Parakeet's 25 → Parakeet (with
   the matching script filter). Top language outside them with probability
   ≥ 0.5 → Whisper, the same clip, so a wrong line never reaches the
   transcript.
3. **Short clips (< 1.5 s)** are too short to judge: they follow the track's
   current engine.
4. **Sticky:** after 2 unsupported utterances, that track uses Whisper for
   the rest of the call and the check stops for it. No flip-flopping; for a
   mixed Turkish/English call Whisper handles both.
5. Whisper is started with language auto-detect as today.

**Lazy loading.** Choosing Parakeet also downloads the fallback Whisper
model (Turbo Compressed) and loads it once in the background, so macOS
compiles it for the Neural Engine ahead of time (the first-ever load can
take minutes); then it's unloaded. Mid-call, the first unsupported
utterance loads it (seconds). The transcription loop awaits the load; the
buffered audio waits and the transcript catches up afterwards. Parakeet
stays loaded while any track still uses it.

**Imported files** (`transcribeFile`), Parakeet + Auto-detect: check three
30 s windows (start, middle, end). Any unsupported → the whole file goes to
Whisper; else Parakeet.

**Live preview** uses the same routing (Parakeet makes previews cheaper).

## 6. The default

`EngineRecommendation.recommend(preferredLanguages:pastCallLanguages:)`:

- **Whisper** if any of the Mac's preferred languages (all of them, not just
  the first) is outside Parakeet's 25, or any past call's transcript is
  (Apple's on-device `NLLanguageRecognizer` on saved text).
- **Parakeet** otherwise.

New installs start on the recommendation (the old `"base"` default goes).
Existing users keep what they picked. For the owner: Turkish → Whisper,
with Parakeet one click away.

## 7. Architecture

- `LanguageGate` (new, `Services/`): pure struct; input = per-utterance
  `(source, duration, language, probability)`, output = `.parakeet(script)` /
  `.whisper`, plus the sticky state. No models.
- `ParakeetTranscriber` (new, `Services/`): wraps FluidAudio's
  `AsrManager` (download, load, transcribe samples, unload).
- `TranscriptionEngine`: model id `"parakeet-v3"` in the existing
  `whisperModel` setting; `loadModel` loads Parakeet + Tiny; `decodeLocally`
  asks the gate, then decodes with Parakeet or (lazily loaded) Whisper;
  `transcribeFile` does the three-window check. Glossary prompting stays
  on the Whisper path only.
- `EngineRecommendation` (new, `Services/`): pure; plus a small reader for
  past-call languages.
- Views: `SettingsView` (Transcription page), `OnboardingView` (model
  step), model status/progress for two downloads.

## 8. Testing

- `make test`: `LanguageGate` routing (supported, unsupported, short clip,
  sticky after 2, per track, hand-picked language skips it);
  `EngineRecommendation` (Turkish anywhere in the list → Whisper; all
  European → Parakeet; past Turkish call → Whisper); the three-window
  import rule; model-id and display-name mapping.
- `--liveloop-test <audio> parakeet-v3` on a Turkish+English recording:
  English lines from Parakeet, Turkish from Whisper, no European-looking
  gibberish; and an English-only recording: never loads Whisper. Report
  decode speed vs Turbo on the same file.
- Measure the Tiny language check per utterance (target: tens of ms) and
  the mid-call Whisper load time after the pre-warm.
- By hand: one real Turkish call and one English call on Parakeet.

## 9. Out of scope

Parakeet's custom-vocabulary booster (a separate CTC model), live speaker
labels, cloud engines, switching back from Whisper to Parakeet within a
call, and changing an existing user's model.
