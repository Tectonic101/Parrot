# Parrot for Windows

A Windows port of Parrot, the meeting recorder with a live AI assistant. It records
your calls from any app (Zoom, Teams, Meet in a browser, a softphone), writes a transcript
with **Me** / **Them** labels, shows live cards while the call runs, and writes a report
when it ends. Like the Mac app it is local-first: transcription runs on your PC by
default, and nothing is sent anywhere until you add your own API key for an AI provider.

This port is derived from Parrot (GPL-3.0). It is a C#/.NET 8 + WPF rewrite of the Swift
app in the parent directory, and it reuses the Mac app's prompts, heuristics and call
profiles unchanged.

> Status: early (0.1). The core paths are complete: capture, local and cloud
> transcription, the live assistant, reports, Ask and export. The unit tests run on any OS.
> Audio capture and the UI can only be exercised on real Windows; see
> [Verification status](#verification-status).

## What works

- **Recording two tracks.**
  - **Them** is everything your PC plays, captured with WASAPI loopback from the
    default output device or one you pick.
  - **Me** is your microphone.
  - Both are converted to 16 kHz mono and saved as separate WAV files. The two tracks are
    the speaker labels: there is no diarization model.
  - If the microphone can't be opened, Parrot records system audio only and tells you.
- **Local transcription with Whisper.**
  - Uses [Whisper.net](https://github.com/sandrohanea/whisper.net), which wraps whisper.cpp.
  - Available models: tiny, base (the default), small, medium and large-v3-turbo, plus
    English-only and quantized variants.
  - The model downloads once from Hugging Face, with progress shown, into
    `%LOCALAPPDATA%\Parrot\models`.
  - Speech is cut into utterances at natural pauses, using the Mac app's segmenter and
    hallucination filters.
  - An optional glossary helps with names and jargon.
- **Cloud transcription (opt-in).** OpenAI Whisper, Groq Whisper (fast and cheap) or
  Deepgram Nova-3. Audio leaves your PC only when you pick one of these engines.
- **Live assistant (Copilot).**
  - While you talk it shows cards: suggested answers, open questions, blockers, action
    items, plus a one-line coaching tip and a call score.
  - A question from the other side gets a fast lane, and cards are de-duplicated the way
    the Mac app does it.
  - Unresolved flags stay pinned until handled. Cards can be marked handled, dismissed,
    or have their suggested reply copied.
  - You can pause it mid-call.
- **AI providers.** You choose them in Settings and can use one model for live help and
  another for reports:
  - Claude (Anthropic API, default `claude-haiku-4-5`)
  - OpenAI
  - Groq
  - Google Gemini (through its OpenAI-compatible endpoint)
  - Ollama, which runs locally
  - any OpenAI-compatible server
- **Knowledge base.**
  - Add `.txt`, `.md` or `.pdf` files. They are chunked and searched with BM25, and only
    the few best-matching passages are sent with a request.
  - Each document can have an "About" note and can be limited to certain call types.
- **Call types (profiles).**
  - The 7 built-in presets from the Mac app: Default, Sales discovery, Coaching session,
    Job interview, Customer support, Vendor evaluation and a generic one.
  - Each has its own persona, card kinds and gauges.
  - You can duplicate a profile and edit its name, persona, standing rules (tone) and
    what the other side is called.
- **Post-call report.** A summary (overview, pain points, key points, next steps) and a
  coaching section. Every bullet carries a `[mm:ss]` receipt from the transcript.
- **Ask Parrot.** Ask questions about all your past meetings ("what did Dana say about
  budget?"). Answers cite their source as *(Meeting, 12:34)*. If no AI is set up, you get
  the best-matching excerpts instead.
- **Meeting library.**
  - A sidebar with search.
  - Rename the meeting or the other party ("Them" becomes "Dana").
  - Notes, copy the transcript or report, regenerate the report, open the audio folder,
    delete.
  - Markdown export with YAML front matter (Obsidian/Notion-friendly). It is the same
    format as the Mac export, including `parrot_id`.
- **Crash safety.** The transcript is saved every few seconds while recording, and WAV
  headers are patched as they grow. A meeting interrupted by a crash is recovered the next
  time Parrot starts.
- **Light and dark themes.** Colors follow the Mac's `Theme.swift`. The theme follows
  Windows or can be forced, and the title bar matches.

## Differences from the Mac app

Things the Mac app does that this port does not, or does differently:

| Area | Mac | Windows (this port) |
|---|---|---|
| Speaker labels | Me/Them plus on-device diarization and name learning ("Speaker 2" becomes "Jeremy") | Me/Them only, from the two tracks. You can rename "Them" for each meeting. |
| Echo handling | SpeexDSP acoustic echo canceller on the mic | No echo canceller. A text-level filter drops "Me" lines that repeat a nearby "Them" line (on by default). Headphones still give the cleanest result. |
| Knowledge base search | BM25 plus on-device sentence embeddings | BM25 only |
| Deepgram | Streaming over a websocket, word by word | Pre-recorded endpoint, one utterance at a time |
| MCP server ("use your meetings in Claude") | Yes | No |
| Auto-update (Sparkle) | Yes | No. Download new builds manually. |
| Calendar, nudges, tone timeline, follow-up email, redaction, audio import, post-call Groq transcript polish | Yes | Not yet |
| Editing card kinds and gauges | In the profile editor | Built-in kinds only. Duplicate a profile to change persona, rules and counterpart. |
| Global hotkeys, menu-bar item | Yes | Not yet (no tray icon) |
| Secrets | Keychain | Windows DPAPI (CurrentUser scope), stored in `%LOCALAPPDATA%\Parrot\secrets.dat` |
| Storage | SwiftData | One JSON file per meeting, in `%LOCALAPPDATA%\Parrot\meetings` |

Meetings recorded on a Mac cannot be imported, and Mac and Windows data are not synced.

## Where your data lives

Everything lives under `%LOCALAPPDATA%\Parrot`. Settings → General has a button that
opens this folder.

```
meetings\<id>.json     transcript, cards, report, notes
audio\<id>\them.wav    system audio (16 kHz mono)
audio\<id>\me.wav      microphone (16 kHz mono)
models\ggml-*.bin      Whisper models
knowledge.json         knowledge base (document text chunks)
profiles.json          call types
settings.json          settings (no keys)
secrets.dat            API keys, encrypted with DPAPI for your Windows account
parrot.log             diagnostics log (never contains keys or transcripts)
```

To uninstall, delete the app folder and `%LOCALAPPDATA%\Parrot`.

## Running a build

1. Download `Parrot-Windows.zip` from the **Windows** workflow run's artifacts on GitHub
   Actions. GitHub wraps every artifact in its own zip, so unzip twice.
2. Run `Parrot\Parrot.exe`. The build is self-contained, so you don't need to install
   .NET.
3. The build is **not signed**. Windows SmartScreen will warn on first launch; choose
   *More info → Run anyway*.
4. In **Settings → Assistant**, pick a provider and paste your API key. **Test**
   checks it. For a fully local setup, install Ollama and pull a model such as
   `ollama pull llama3.2:3b`.
5. Click **Record**. The first local recording downloads the Whisper model (142 MB for
   *base*).

Requirements: Windows 10 or 11 on x64. A CPU with AVX2 is
strongly recommended for local Whisper. Use *tiny* or *base* on slower machines, or
switch to Groq in **Settings → Transcription**. If transcription falls behind, the live
view tells you.

Microphone access: if **Me** stays silent, open Windows *Settings → Privacy & security →
Microphone* and turn on *Let desktop apps access your microphone*.

## Building from source

Prerequisite: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd windows
dotnet build Parrot.Windows.sln -c Release
dotnet test Parrot.Windows.sln -c Release
dotnet run --project src/Parrot.App -c Release
```

To make a self-contained folder like CI does:

```powershell
dotnet publish src/Parrot.App/Parrot.App.csproj -c Release -r win-x64 --self-contained true -o publish/Parrot
```

On Linux or macOS you can build and test `Parrot.Core` and its tests, and `Parrot.Audio`
also compiles (`EnableWindowsTargeting` is on). Building the WPF app needs a .NET SDK
that includes the Windows Desktop SDK. Microsoft's own SDK builds include it; some Linux
distribution packages leave it out. The app runs only on Windows.

### Project layout

```
windows/
  Parrot.Windows.sln
  Directory.Build.props        shared settings (C# latest, nullable, EnableWindowsTargeting)
  src/Parrot.Core/             net8.0, no Windows APIs: models, JSON storage, LLM providers,
                               cloud transcription, knowledge base, copilot, reports, Ask, export
  src/Parrot.Audio/            net8.0-windows: WASAPI capture (NAudio), Whisper.net, model download
  src/Parrot.App/              WPF app (MVVM, CommunityToolkit.Mvvm), DPAPI secret store, themes
  tests/Parrot.Core.Tests/     xunit tests for Parrot.Core (no network, no audio hardware)
```

### GPU acceleration (optional)

The default `Whisper.net.Runtime` package runs on the CPU. To use an NVIDIA GPU, add
`Whisper.net.Runtime.Cuda` to `src/Parrot.Audio/Parrot.Audio.csproj`. For other GPUs, add
`Whisper.net.Runtime.Vulkan`. Whisper.net picks the best available runtime at load time.
These packages are large and need matching drivers, which is why they are not included
by default.

## Code signing

The CI builds are unsigned. To ship signed builds, use **your own** code-signing
certificate: an OV/EV certificate, or Azure Trusted Signing. Sign `Parrot.exe` after
`dotnet publish`, for example:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f your-cert.pfx /p <password> publish\Parrot\Parrot.exe
```

In CI, keep the certificate in GitHub secrets. Never commit a certificate or its
password.

## Verification status

What is covered automatically:

- `Parrot.Core.Tests` contains 94 xunit tests. Areas covered:
  - chunking and BM25 retrieval
  - prompt and schema building
  - question detection and card de-duplication
  - persistence round trips
  - Claude and OpenAI-compatible request/response JSON, using a fake HTTP handler
  - Whisper API, Deepgram and model download
  - segmentation, WAV and resampling
  - the copilot engine
  - the recording session (fake audio sources), echo suppression
  - reports, Markdown export and Ask citations

What needs a real Windows PC to confirm:

- WASAPI loopback and microphone capture on different devices. This includes the
  silent-playback trick that keeps loopback flowing when nothing is playing, and the
  gap filler.
- The Whisper.net native runtime loading on the target CPU.
- DPAPI key storage.
- How the WPF UI renders in both themes and at different DPI scales.

## License

GPL-3.0, like Parrot. See [`../LICENSE`](../LICENSE). Every source file notes that it is
derived from Parrot (GPL-3.0).
