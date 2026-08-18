# Formatting Scope

What VoiceType's automatic cleanup pass does, and what it deliberately refuses to do. This is the contract the cleanup prompt in [`ICleanupProvider.cs`](../src/VoiceType.Core/Cleanup/ICleanupProvider.cs) implements.

## In scope

The cleanup pass sees the raw transcript plus a coarse app category (terminal, code editor, or general). Nothing else. From that it will:

- **Remove fillers and disfluency.** "um", "uh", "so basically", false starts.
- **Resolve self-corrections.** "meet Tuesday, no wait, Wednesday" → `Wednesday`.
- **Add capitalization and terminal punctuation.**
- **Honor spoken punctuation.** "comma", "period", "question mark", "new paragraph", "new line".
- **Build numbered lists** from clear ordinal or cardinal sequences — "one… two… three" or "first… second… third".
- **Build bulleted lists** from explicit bullet language, using plain `- ` prefixes that survive a plain-text paste.
- **Apply custom dictionary spellings** for names and product terms the user has added.
- **Protect technical tokens.** URLs, file paths, CLI flags, camelCase, and snake_case pass through byte-for-byte.

## Precedence rules

Two rules exist because they conflict in practice and the order matters:

1. **Protected tokens outrank the dictionary.** If a user has `Northwind Labs` stored and dictates `github.com/northwind/voicetype`, the URL stays lowercase. Dictionary casing is never applied inside a URL, path, flag, or code token.
2. **List formatting is gated by app category.** In a terminal or code editor, list formatting requires explicit list language. "step one, run the build, step two, run the tests" stays prose in a terminal, because turning it into a numbered list would corrupt a command.

## Explicitly out of scope

Not "someday" — these are excluded by design, and some of them for safety:

- **Pressing Enter.** No code path submits on the user's behalf. Ever.
- **Adding facts.** The pass edits form, never content. It may not answer, expand, or embellish.
- **Rewriting selected text** from spoken instructions.
- **Tone changes, translation, summarizing, or custom prompt transforms.**
- **Voice-triggered snippet expansion.**
- **Reading nearby text or caret context.** This is the important one: it would widen the privacy boundary from "audio you deliberately recorded" to "whatever happened to be on your screen." Not worth it.
- **Learning styles per app or per contact.**
- **Rich-text output.** Plain text only, so paste behaves predictably everywhere.

## Safety fallback

`CleanupValidator` rejects a cleanup result that shrinks the transcript below a 0.30 character ratio or drops a protected token, and the raw transcript is inserted instead. A degraded pass is recorded in History with a fixed label so the user can see it happened. Aggressive-but-correct cleanup is common — "um so basically let's meet on Tuesday no wait Wednesday period" → "Let's meet on Wednesday." is a 0.364 ratio and must be accepted — so the floor sits at 0.30 rather than somewhere comfortable like 0.5.
