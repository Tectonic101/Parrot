# Parakeet as a second on-device model: design

Date: 2026-09-30, revised the same day to build on 0.24.2's language check.
Status: **approved in brainstorming, not built.** Base: master at `e5ead5f`
(0.24.2). It touches `TranscriptionEngine` and the Transcription settings
page, which the live-nudges PR (#86) also edits; whichever lands second
rebases.

## 1. Problem

Parrot's only free on-device engine is Whisper (WhisperKit). NVIDIA's
Parakeet TDT 0.6B v3 is faster, makes up less text on silence, and is about
as accurate as Whisper Large V3 Turbo, but it knows only 25 European
languages. **Turkish is not one of them.** On Turkish audio it doesn't stay
quiet: it writes confident-looking European words. Offering it naively would
silently ruin the owner's own calls (same class of risk as the
confidence-floor trap).

## 2. Decisions (with the user)

| Question | Choice |
|---|---|
| Add Parakeet? | **Yes, free, next to Whisper**, labelled "25 European languages, no Turkish" |
| Where it lives | **A model in the On-Device Model list**, not a new engine |
| Auto-detect + a Turkish call | **Hold each side's first ~10 s of speech until its language is known, then route it**; unsupported languages go to Whisper |
| Whisper while on Parakeet | **Loaded only when an unsupported language first appears** (downloaded and prepared ahead, loaded lazily) |
| Default | **Recommended from the Mac's languages, past calls and memory**; existing users keep their choice |

The first draft checked every utterance. 0.24.2's work showed Whisper needs
about 10 s of speech to be sure, so short utterances can't be judged
reliably; this revision uses that measured window instead.

## 3. Facts this rests on

- Parakeet v3 ships in the pinned FluidAudio: `AsrModels.downloadAndLoad(version: .v3)`
  plus `AsrManager.transcribe(_:decoderState:language:)`, returning text,
  confidence and token timings. No new dependency. The int8 download is
  about 0.5 GB (encoder 446 MB, decoder, joint, preprocessor).
- Its `language:` argument is a script filter (Latin, Cyrillic, Greek), not
  language ID. There is no language in its output.
- Its 25 languages: bg, cs, da, de, el, en, es, et, fi, fr, hr, hu, it, lt,
  lv, mt, nl, pl, pt, ro, ru, sk, sl, sv, uk.
- **0.24.2 (PR #85) already checks the call's language**: it gathers the
  first 10 s of voiced audio per track (`languageProbe`), asks
  `whisperKit.detectLangauge(audioArray:)` once, and offers "sounds like
  Turkish, switch to Turkish" when a pinned language is wrong. 10 s scored
  p ≥ 0.93 on 8 real tracks; below 0.8 it stays quiet. Today it checks only
  the first track to fill, and only when the loaded model is multilingual
  Whisper (English-only models can't detect).

## 4. What the user sees

**Settings → Transcription.** Engine card: "On-device, private, free", Groq,
Deepgram (was "On-device Whisper"). On-Device Model picker gains, at the top:

```
Parakeet v3 — about 0.5 GB, fastest · 25 European languages (no Turkish)
Tiny / Base / Small / Large V3 Turbo Compressed / Large V3 Turbo
```

With Parakeet picked, one line under the picker: "Calls in other languages,
like Turkish, switch to Whisper Large V3 Turbo Compressed on their own."

**Language setting.** If it's pinned to a language Parakeet doesn't know
(Turkish, Arabic, Chinese, Japanese, Korean, Hindi), the Parakeet row is
disabled with the reason, and a user already on Parakeet gets Whisper for
the call (the notice line says so). Pinned to one of Parakeet's languages:
no hold, Parakeet from the first word.

**Custom Vocabulary** gets "Whisper only" (Parakeet has no prompt).

**Onboarding** (`SpeechModelStep`): Parakeet joins the short list
(Base, Turbo Compressed, Turbo) as "Fastest, 25 European languages (no
Turkish)". The recommended row follows section 6.

**During a call on Parakeet + Auto**, each side's first lines appear about
10 s later than usual while its language is checked; the notice line reads
"Checking the language…" until both sides are decided. If a side turns out
to be Turkish: "Turkish heard: using Whisper for them" (or "for you").

## 5. The language check

A pure `LanguageRouter` holds one state per track (you, them):
`undecided`, `parakeet`, or `whisper`. It is only active on Parakeet with
Auto-detect; otherwise every track starts decided.

1. **Gather.** The existing probe gathers voiced audio **per track**, and each
   track now gets its own check (0.24.2 stopped after the first track).
2. **Detect.** The detector is the loaded Whisper when the model is
   multilingual Whisper (as today), and Whisper **Tiny** (40 MB, loaded with
   Parakeet) when the model is Parakeet.
3. **Decide** a track when it has 10 s of voiced speech, or 30 s after its
   first speech with whatever it has (a side that barely talks still gets
   transcribed). Supported language with p ≥ 0.8 → `parakeet`, with its
   script filter. Anything else, including an unsure answer → `whisper`: a
   slower line beats a wrong one.
4. **Hold.** While a track is `undecided`, the loop doesn't cut its audio:
   it waits in the track's buffer (typing dots still show, no preview).
   Once decided, the loop's existing backlog handling cuts and decodes it
   in bounded steps, so the transcript catches up in seconds. At stop,
   undecided tracks decide at once with what they have.
5. **Recheck.** A `parakeet` track is checked again on every further 60 s
   of its speech, on its latest ~10 s. If it now hears a language Parakeet
   doesn't know (p ≥ 0.8), the track switches to `whisper` for the rest of
   the call, and its lines since the last clean check (at most about 70 s)
   are re-transcribed with Whisper from kept audio and replaced in the
   transcript. The Copilot already saw the old lines; that's accepted.
6. **No switching back** within a call: a `whisper` track stays `whisper`.

**0.24.2's warning** keeps its rules (`languageMismatch(setting:backend:heard:confidence:)`)
and is fed by whichever track's check comes back first conclusive. With
Parakeet it now works too (via Tiny): pinned to English but hearing Turkish
still offers "switch to Turkish". `switchLanguage` to a language Parakeet
doesn't know sets both tracks to `whisper`.

**Lazy loading.** Picking Parakeet also downloads the fallback Whisper
(Turbo Compressed) and loads it once in the background so macOS prepares
it for the Neural Engine (the first-ever load can take minutes), then
unloads it. Mid-call, the first `whisper` route loads it (seconds); that
track's audio waits in its buffer meanwhile. It's unloaded again when the
call ends. Parakeet and Tiny stay loaded while Parakeet is the model.

**Imported files**, Parakeet + Auto-detect: detect on three 30 s windows
(start, middle, end). Any window not confidently a Parakeet language → the
whole file goes to Whisper; else Parakeet.

**Live preview** follows the track's route; none while `undecided`.

## 6. The default

`EngineRecommendation.recommend(preferredLanguages:pastCallLanguages:memoryGB:)`:

- **Whisper** (the memory-based pick, `MachineFit.whisperModel`) if any of
  the Mac's preferred languages (all of them, not just the first) is outside
  Parakeet's 25, or any past call is (Apple's on-device
  `NLLanguageRecognizer` on saved transcript text).
- **Parakeet** otherwise.

New installs start on the recommendation. Existing users keep what they
picked. For the owner: Turkish → Whisper, with Parakeet one click away.

## 7. Architecture

- `LanguageRouter` (new, `Services/`): pure struct. Per-track state, the
  decide rule, the recheck schedule, `switchLanguage` handling. No models.
- `ParakeetTranscriber` (new, `Services/`): wraps FluidAudio's `AsrManager`
  (download with progress, load, transcribe samples with a script filter).
- `TranscriptionEngine`:
  - model id `"parakeet-v3"` in the existing `whisperModel` setting; its
    display name, download and readiness;
  - `loadModel("parakeet-v3")` loads Parakeet + Tiny and prepares the
    fallback; `whisperKit` stays nil until a track needs Whisper;
  - the probe gathers per track; `checkLanguage` feeds the router and the
    0.24.2 warning;
  - the loop skips `undecided` tracks, then decodes by route;
  - recheck + rewind emits replacements through a new `onReplace` callback;
  - `transcribeFile` does the three-window check.
- `RecordingManager`: handles `onReplace` (swap a track's segments in a time
  range).
- `EngineRecommendation` (new, `Services/`): pure; plus a small reader for
  past-call languages.
- Views: `SettingsView` (Transcription page), `SpeechModelStep` (onboarding).

## 8. Testing

- `make test`: `LanguageRouter` (supported → parakeet; Turkish → whisper;
  unsure → whisper; 30 s timeout; stop decides; per track; recheck switches
  and reports the rewind range; no switch back; pinned language skips the
  hold; `switchLanguage` to Turkish forces whisper); 0.24.2 warning checks
  still pass, plus one for the second track; `EngineRecommendation`;
  the three-window import rule; model id, display name.
- `--language-test` with Tiny on the 8 real tracks 0.24.2 used: same
  verdicts at 10 s, p ≥ 0.8. If Tiny falls short, the detector becomes Base
  (140 MB) instead; the design doesn't change.
- `--liveloop-test <audio> parakeet-v3` on a Turkish+English recording:
  English lines from Parakeet, Turkish from Whisper, no European-looking
  lines for Turkish; on an English-only recording Whisper is never loaded.
  Report decode speed against Turbo on the same file.
- Measure the mid-call Whisper load after the pre-warm.
- By hand: one real Turkish call and one English call on Parakeet.

## 9. Out of scope

Parakeet's custom-vocabulary booster (a separate CTC model), live speaker
labels, cloud engines, switching a track back from Whisper to Parakeet, and
changing an existing user's model.
