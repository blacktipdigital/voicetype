# Architecture

Two projects. All logic lives in `VoiceType.Core`, which has **no WPF dependency** — that's why 101 tests run with no UI, no microphone, and no network. `VoiceType.App` is a thin WPF tray shell that wires concrete implementations into the coordinator.

```
src/
  VoiceType.Core/          no WPF, no UI, fully testable
    Audio/                 WASAPI capture (NAudio)
    Cleanup/               OpenAI Responses cleanup + validator
    Dictation/             DictationCoordinator — the state machine
    Hosting/               single-instance mutex guard
    Hotkeys/               low-level keyboard hook + chord detector
    Insertion/             clipboard lease, key sender, text injector
    Logging/               ILog seam (never logs content)
    Native/                Win32 P/Invoke
    Security/              DPAPI secret store
    Storage/               settings.json + history.json
    Targeting/             UIA target identity + editability heuristics
    Time/                  IClock seam
  VoiceType.App/           WPF tray app, overlay, settings, history, recovery
```

## The dictation path

1. **Chord down.** `KeyboardHookSource` (a `WH_KEYBOARD_LL` hook) feeds `ChordDetector`. Global hook because that's the only way to see `Ctrl`+`Win` while another app owns focus.
2. **Snapshot the target.** `TargetService` captures the foreground window handle *and* the focused element's UI Automation runtime ID. Both must still match at insertion time, so tabbing to another field mid-dictation refuses instead of pasting into the wrong box.
3. **Capture.** `WasapiAudioCapture` opens the mic in shared mode and streams PCM.
4. **Transcribe.** `OpenAiRealtimeSession` streams audio over `wss://api.openai.com/v1/realtime` and returns partials for the overlay plus a final transcript.
5. **Chord up.** Holds under `MinHoldMs` (300 ms) are treated as accidental taps and cancel silently. `Esc` cancels and is swallowed so it doesn't reach the app underneath.
6. **Clean up.** `OpenAiResponsesCleanupProvider` runs a fixed system prompt over the transcript. `CleanupValidator` rejects results that shrink below a 0.30 ratio or drop protected tokens, and falls back to the raw transcript rather than inserting garbage.
7. **Insert.** `ClipboardLease` backs up existing clipboard formats on an STA thread, `TextInjector` sends the app-appropriate paste chord, then the lease restores the original clipboard.
8. **Or refuse.** Any failed check — target moved, elevated window, password field, uneditable control, injection error — leaves the transcript on the clipboard and opens the recovery window with the reason.

## Design rules

These are load-bearing. Breaking one is a bug, not a style choice.

- **`VoiceType.Core` never references WPF.** It's what keeps the suite offline and headless.
- **Never send Enter.** No code path may submit on the user's behalf.
- **Never lose a transcript.** Every refusal path ends with the text on the clipboard.
- **Never log content.** The `ILog` contract forbids transcripts, audio, keystrokes, window titles, and secrets. Log operation names and exception types only.
- **Secrets go through `ISecretStore`.** DPAPI CurrentUser at rest. Never in `settings.json`, never in a log, never in a test.
- **Protected tokens outrank the dictionary.** Dictionary casing is never applied inside URLs, paths, CLI flags, camelCase, snake_case, or code.
- **One process only.** `SingleInstanceGuard` takes a named mutex before any tray icon, hook, capture, or provider exists. Two instances would each own a hook and paste twice.
- **The target is re-checked next to the keystroke, not just before insertion starts.** `Inject` takes a `stillValid` callback and runs it after the clipboard backup and settling delay. The caller's earlier check is not sufficient on its own — focus can move while the clipboard work happens.
- **Dictated content is encrypted at rest.** History goes through `IDataProtector` (DPAPI CurrentUser). Legacy plaintext files are read once and re-encrypted on the next write.
- **Prompt fields are sanitized before the join.** `appCategory` and dictionary terms are single-line by construction, so control characters in them are forged structure and get collapsed.
- **Every external dependency sits behind an interface** (`IAudioCapture`, `ITranscriptionProvider`, `ICleanupProvider`, `ITargetService`, `ISecretStore`, `IClock`, `ILog`) so tests use fakes.

## Tests

```powershell
dotnet test VoiceType.slnx        # 101 tests, offline, no key
```

Two live fixtures call the real API and cost money, so they're opt-in:

```powershell
$env:VOICETYPE_RUN_LIVE_FIXTURE = "1"
dotnet test VoiceType.slnx
```

The 30-utterance cleanup fixture asserts exact strings on regression cases and requires at least 24 of 30 resolved. The Smart Formatting fixture covers six exact list/punctuation cases. Both skip silently without the flag.

## Scope boundary

What automatic cleanup does and deliberately does not do: [FORMATTING-SCOPE.md](FORMATTING-SCOPE.md).
