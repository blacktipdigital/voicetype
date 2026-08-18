# VoiceType

Push-to-talk dictation for Windows. Hold `Ctrl` + `Win`, talk, release. Cleaned-up text appears at your cursor in whatever app you were already using.

Open source, no account, no subscription. You bring your own OpenAI API key and pay OpenAI directly for what you use.

**Fastest way to install it: open this repo in Claude Code and say "set this up for me."** Claude reads [CLAUDE.md](CLAUDE.md) and walks you through the whole thing. Manual steps are below if you'd rather do it yourself.

---

## What it does

- **Hold to talk.** Hold `Ctrl`+`Win`. A small overlay shows it's recording. Release and the text pastes at your caret.
- **Cleans as it goes.** Fillers removed, punctuation and capitalization added, self-corrections resolved. Say "let's meet Tuesday, no wait, Wednesday" and you get `Let's meet on Wednesday.`
- **Spoken punctuation and paragraphs.** "comma", "period", "new paragraph" all work.
- **Numbered and bulleted lists** from natural speech ("my priorities are one, call Sarah, two, edit the video").
- **Protects code.** URLs, file paths, CLI flags, camelCase, and snake_case pass through untouched.
- **Custom dictionary.** Teach it names and product spellings it would otherwise mangle.
- **Works in terminals.** Picks the right paste chord per app: `Ctrl+Shift+V` in Windows Terminal, `Shift+Insert` in classic consoles, `Ctrl+V` everywhere else.
- **Never loses a transcript.** If it can't safely type into the target, the text goes to your clipboard and a recovery popup tells you why.
- **Never presses Enter.** By design. It will not send your message for you.

## Shortcuts

| Shortcut | Action |
|---|---|
| Hold `Ctrl`+`Win` | Record. Release to transcribe and paste. |
| Tap `Ctrl`+`Win` (under 300 ms) | Cancels silently. Stops accidental triggers. |
| `Esc` while recording | Cancel. Nothing is inserted or sent. |
| `Shift`+`Alt`+`Z` | Paste the last result again, within 5 minutes of dictating it. |

Tray icon gives you Settings, History, and Quit.

## Requirements

- **Windows 11 x64.** Windows 10 is untested. Not portable to macOS or Linux — it's built on WPF, WASAPI, and Win32 hooks.
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** to build it.
- **An OpenAI API key** with billing enabled, from [platform.openai.com](https://platform.openai.com/api-keys).
- **A microphone.**

It calls two OpenAI endpoints: `gpt-realtime-whisper` for streaming transcription and `gpt-5.4-nano` for the cleanup pass. Usage is billed to your key at OpenAI's current rates — check their pricing page, and set a spend limit on your account if you want a hard ceiling.

## Install

```powershell
git clone https://github.com/blacktipdigital/voicetype.git
cd voicetype
dotnet build VoiceType.slnx -c Release
```

Run it:

```powershell
.\src\VoiceType.App\bin\Release\net10.0-windows\VoiceType.exe
```

Then right-click the tray icon → **Settings** → paste your API key → pick your microphone → **Save**. Hold `Ctrl`+`Win` in Notepad and say a sentence to confirm it works.

To start it with Windows, put a shortcut to that `.exe` in `shell:startup`.

## Privacy

Read this part. This app installs a global keyboard hook and streams microphone audio to a third party, and you should know exactly what that means before you run it.

- **The keyboard hook is global and it has to be.** That's the only way to catch `Ctrl`+`Win` while another app has focus. It inspects key state to detect the chord and passes everything else through. It does not record, store, or transmit your keystrokes. The code is in [`KeyboardHookSource.cs`](src/VoiceType.Core/Hotkeys/KeyboardHookSource.cs) and [`ChordDetector.cs`](src/VoiceType.Core/Hotkeys/ChordDetector.cs) — it's about 150 lines, read it yourself.
- **The microphone is only live while you hold the chord.** Release, `Esc`, or an error all stop capture immediately. Audio is streamed to OpenAI for transcription and is not written to disk.
- **Your audio and transcripts go to OpenAI.** That is how transcription and cleanup happen. Their API data-usage policy applies. If that's not acceptable for what you dictate, don't use this.
- **Your API key is encrypted at rest** with Windows DPAPI (CurrentUser scope) at `%LOCALAPPDATA%\VoiceType\openai.key.bin`. Only your Windows account can decrypt it. It never enters `settings.json`, the repo, or any log. Note what DPAPI does and does not do: it protects the file, not your session — any code already running as you can ask Windows to decrypt it, same as every other app that stores a credential this way.
- **Insertion goes through your clipboard, and that has consequences.** To place text at your caret VoiceType copies the transcript to the clipboard and sends a paste chord, then restores your previous clipboard contents about half a second later. While it is there, any process that watches the clipboard can read it. **If Windows Clipboard History (Win+V) is on, Windows keeps a copy of every transcript**, and syncs it to your Microsoft account if you enabled "Sync across devices" — that copy outlives VoiceType's own retention and is not something the app can clear. Turn clipboard history off in Settings → System → Clipboard if you dictate anything sensitive.
- **History is local, text-only, and encrypted at rest.** `%LOCALAPPDATA%\VoiceType\history.json`, DPAPI CurrentUser so another process running as you cannot read it off disk, 7-day retention, 1,000-entry cap, cleared from the History window whenever you want. No audio is kept.
- **Logs never contain content.** No transcripts, no audio, no keystrokes, no window titles, no secrets. Enforced at the [`ILog`](src/VoiceType.Core/Logging/ILog.cs) seam.
- **No telemetry, no analytics, no phone-home.** The only network calls in the codebase go to `api.openai.com`. Grep for `https://` and verify.

## Known limitations

Honest list. These are real and mostly by design.

- **Dictation can't start when an elevated (admin) window already has focus.** Windows UIPI hides the chord from a non-elevated hook, so VoiceType safely does nothing. If a window elevates mid-dictation, it's detected within 250 ms and falls back to the clipboard.
- **Password fields are refused on purpose.** The transcript goes to your clipboard instead.
- **Not every text box.** Remote desktop sessions, games, and custom-drawn controls can block synthetic input. When that happens you get the recovery popup, not a lost transcript.
- **English-first.** Other languages aren't tested.
- **No signed installer.** You build from source. That's a deliberate tradeoff: you can read every line before you run something with a keyboard hook in it.
- **No auto-update.** `git pull` and rebuild.
- **Target latency is ~2s p50 / ~4s p95** from release to paste on a normal connection, for dictations up to about 30 seconds.

## Not included

Deliberately out of scope: login, billing, teams, cloud sync, mobile, macOS, screenshots, reading nearby text, voice commands, snippets, style learning, selected-text rewriting, hands-free mode, and automatic Enter.

## Architecture

`VoiceType.Core` holds all logic behind interfaces and has no WPF dependency, which is why 101 tests run without a UI or a network call. `VoiceType.App` is the WPF tray shell. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

```powershell
dotnet test VoiceType.slnx
```

101 tests, no API key needed, zero network calls. Two live fixtures that do hit the API are opt-in only via `VOICETYPE_RUN_LIVE_FIXTURE=1`.

## Contributing

Issues and PRs welcome. Keep `VoiceType.Core` free of WPF references, keep new tests offline by default, and don't add anything that logs transcript content.

## Security

Threat model, what is deliberately out of scope, and how to report a vulnerability privately: [SECURITY.md](SECURITY.md).

## License

MIT. See [LICENSE](LICENSE).

Not affiliated with, endorsed by, or derived from the code of any commercial dictation product. Built independently against public documentation.
