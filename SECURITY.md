# Security

VoiceType installs a global keyboard hook, holds an API key, and injects keystrokes into other applications. That is a lot of trust for a tool you downloaded from a stranger, so here is what it actually does and how to tell me when it does something wrong.

## Reporting a vulnerability

Open a [private security advisory](https://github.com/blacktipdigital/voicetype/security/advisories/new) rather than a public issue. I will confirm within a few days. There is no bounty — this is a free tool — but you will be credited unless you'd rather not be.

If the advisory form isn't working for you, open a normal issue saying only that you have a security report and I'll get you a private channel. Don't put the details in a public issue.

## What the threat model covers

**In scope**, and where the code enforces it:

| Concern | Control |
|---|---|
| Keystrokes leaving the machine | The hook tracks 8 modifier keys plus Esc and Z as booleans. Everything else short-circuits. See [`ChordDetector.cs`](src/VoiceType.Core/Hotkeys/ChordDetector.cs) — 115 lines, read it. |
| Pasting into the wrong window | `TargetSnapshot` pins the window handle and the focused UIA element. Re-checked immediately before the keystroke, after the clipboard work and settling delay. |
| Pasting into a password field | Refused; the transcript goes to the clipboard with a recovery popup instead. |
| Pasting into an elevated app | Refused. Integrity is compared against our own and every uncertain path fails closed. |
| API key on disk | DPAPI CurrentUser, plaintext buffers zeroed after use. |
| Transcripts on disk | DPAPI CurrentUser, 7-day retention, 1,000-entry cap. |
| Content in logs | The `ILog` contract forbids transcripts, audio, keystrokes, window titles, and secrets. Exception messages are logged by type only, since parser errors can embed payload fragments. |
| Stale transcript replay | Shift+Alt+Z expires 5 minutes after the dictation. |
| Prompt field forgery | Dictionary terms and app category are stripped of control characters before entering the model prompt. |

**Out of scope**, deliberately:

- **Another process already running as your Windows user.** DPAPI protects files, not your session. Anything running as you can ask Windows to decrypt what you can decrypt. This is true of every credential store on the platform; if you're already compromised at that level, VoiceType is not your problem.
- **The clipboard.** Insertion works by pasting, so every transcript transits the system clipboard. Windows Clipboard History and third-party clipboard managers will capture them. This is documented in the README and cannot be fixed without abandoning paste-based insertion.
- **What OpenAI does with your audio.** Requests are sent with `store: false`, but their API policy governs, not this app.
- **Physical access and hardware keyloggers.**
- **Malicious builds.** Build from source, or verify what you're running.

## Known accepted limitations

- Dictation cannot start while an elevated window already has focus. Windows UIPI hides the chord from a non-elevated hook, so VoiceType safely does nothing. Mid-dictation elevation is detected within 250 ms and falls back to the clipboard.
- Transcript content flows through an LLM whose output becomes keystrokes. If you dictate text you did not write — reading an untrusted document aloud — that text can in principle steer the cleanup model. The system prompt instructs the model to treat transcript content as data, and the validator rejects results that shrink the text below a 0.30 ratio or drop protected tokens, but neither is a guarantee. Read what lands before you rely on it.
- No signed binary and no auto-update. You build from source; `git pull` and rebuild for changes.

## Verifying it yourself

```powershell
dotnet test VoiceType.slnx        # 101 tests, offline, no API key needed
```

Every network call in the codebase goes to `api.openai.com`. Confirm it:

```powershell
Select-String -Path src\**\*.cs -Pattern "https://|wss://"
```
