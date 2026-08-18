# CLAUDE.md — Instructions for setting up VoiceType

You are helping someone install and run VoiceType on their own Windows machine. They may not be a developer. Treat this as a guided install, not a code task.

## Ground rules

1. **Never ask the user to paste their OpenAI API key into this chat.** They type it directly into the VoiceType Settings window. If they paste it here anyway, tell them to revoke that key at platform.openai.com and generate a new one.
2. **Tell them what the app does before they run it.** It installs a global keyboard hook, streams mic audio to OpenAI while they hold the chord, and passes every transcript through the system clipboard to paste it. Point them at the Privacy section of README.md and say the clipboard part out loud — if they have Windows Clipboard History (Win+V) enabled, Windows retains every transcript independently of this app. Do not skip this because it slows the install down.
3. **Run one step at a time and confirm each one.** Don't dump all six steps and hope.
4. **Don't modify the source to "fix" the install.** If the build fails, diagnose the environment. Report a real bug rather than patching around it.
5. **If they aren't on Windows, stop.** This is WPF + WASAPI + Win32 hooks. There's no port and there won't be one. Say so plainly instead of attempting a workaround.

## Step 1 — Check the environment

```powershell
$PSVersionTable.OS
dotnet --list-sdks
```

Need Windows 11 x64 and a .NET 10 SDK. If `dotnet` is missing or every SDK is below 10:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Then have them open a **new** terminal — `PATH` won't refresh in the current one — and re-run `dotnet --list-sdks`. If winget isn't available, send them to https://dotnet.microsoft.com/download/dotnet/10.0.

## Step 2 — Build

```powershell
dotnet build VoiceType.slnx -c Release
```

Expect `Build succeeded. 0 Warning(s) 0 Error(s)`. If NuGet restore fails, it's almost always a proxy or offline feed — check `dotnet nuget list source`.

## Step 3 — Verify before running anything

```powershell
dotnet test VoiceType.slnx -c Release --no-build
```

Expect **101 passed, 0 failed**. No API key required and no network calls. If tests fail, stop and diagnose. Don't tell them to run the app anyway.

## Step 4 — Get an API key

Walk them through it:

1. Go to https://platform.openai.com/api-keys and create a secret key.
2. Confirm billing is set up at https://platform.openai.com/settings/organization/billing — a key without credit returns 429 and dictation will fail with an auth/quota error.
3. Suggest setting a monthly spend limit so there's a hard ceiling.
4. **Copy the key to their clipboard. Do not paste it into this chat.**

The app uses `gpt-realtime-whisper` for transcription and `gpt-5.4-nano` for cleanup. Both bill to their key. Do not quote specific prices — you don't know current rates. Point them at https://openai.com/api/pricing.

## Step 5 — First run

```powershell
.\src\VoiceType.App\bin\Release\net10.0-windows\VoiceType.exe
```

It's a tray app, so no window opens. Tell them to look in the system tray (the `^` arrow near the clock).

Then: right-click the tray icon → **Settings** → paste the key into the API key box → pick a microphone → **Save**. The key gets DPAPI-encrypted to `%LOCALAPPDATA%\VoiceType\openai.key.bin` immediately.

## Step 6 — Smoke test

Have them do exactly this, in order:

1. Open Notepad and click into it so the caret is visible.
2. Hold `Ctrl`+`Win` and say: *"um so basically let's meet on Tuesday no wait Wednesday period"*
3. Release.

Expected result in Notepad: `Let's meet on Wednesday.` — filler gone, correction resolved, punctuation added.

If that works, they're done. Show them the shortcut table from README.md.

## Step 7 — Offer autostart

Only if they want it:

```powershell
$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\VoiceType.lnk")
$s.TargetPath = (Resolve-Path ".\src\VoiceType.App\bin\Release\net10.0-windows\VoiceType.exe").Path
$s.Save()
```

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Nothing happens on `Ctrl`+`Win` | Focused window is elevated (admin). Windows hides the chord from a non-elevated hook. Click into a normal window and retry. This is a documented limitation, not a bug. |
| Overlay shows, no text arrives | Missing or invalid API key, or no billing credit. Check Settings, then the OpenAI usage dashboard. |
| Text lands on the clipboard with a popup | Working as designed. The target refused safe insertion (password field, focus changed, uneditable control). Paste it manually. |
| "Last result expired" on Shift+Alt+Z | Working as designed. The replay buffer expires 5 minutes after the dictation so a stray chord cannot inject an old transcript into whatever is focused later. Dictate again. |
| Text pasted twice / two tray icons | Two copies running from an older build. Quit both from the tray, or `Get-Process VoiceType \| Stop-Process`, then relaunch one. |
| Recording seems stuck | Release both keys. The hook reconciles key state every 100 ms and will clear it. |
| Wrong words for names or products | Settings → Dictionary → add the correct spelling. Saves instantly. |
| Nothing pastes in a terminal | Confirm the terminal allows paste chords. VoiceType uses `Ctrl+Shift+V` for Windows Terminal, `Shift+Insert` for classic consoles. |

## If they ask you to add features

Read `docs/ARCHITECTURE.md` first. Two hard rules: `VoiceType.Core` never takes a WPF dependency, and nothing may log transcript content, audio, keystrokes, or window titles. New tests must pass offline with no API key.
