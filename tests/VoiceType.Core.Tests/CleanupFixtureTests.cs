using System.Diagnostics;
using VoiceType.Core.Cleanup;
using VoiceType.Core.Security;
using Xunit.Abstractions;

namespace VoiceType.Core.Tests;

/// <summary>
/// The 30-utterance cleanup fixture. LIVE, PAID TEST — it calls the
/// real Responses API and is opt-in: it runs only when
/// VOICETYPE_RUN_LIVE_FIXTURE=1 AND a DPAPI key is stored; otherwise it
/// passes with a SKIPPED note and makes zero network calls. Raw/final text
/// and per-call latency go to test output only, never application logs.
/// Token preservation (names, numbers, URLs, paths, flags, code tokens) and
/// ExpectedExact matches are hard failures; cleanup resolution is scored and
/// must reach 24 of 30.
/// </summary>
public class CleanupFixtureTests
{
    private readonly ITestOutputHelper _output;

    public CleanupFixtureTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// MustContain = hard token preservation (names, numbers, URLs, paths,
    /// flags, code). SoftContain = expected cleanup artifacts (e.g. a real
    /// newline for "new paragraph") counted toward the 24-of-30 score only.
    /// </summary>
    private sealed record Utterance(
        string Name,
        string Raw,
        string[] MustContain,
        string[] MustNotContain,
        string Category = "text_field",
        string[]? SoftContain = null,
        string? ExpectedExact = null);

    private static readonly string[] DictionaryTerms = { "Northwind Labs", "PostgreSQL", "Vasquez" };

    private static readonly Utterance[] Fixture =
    {
        // Filler
        new("filler-1", "um so basically I just wanted to thank everyone on the team",
            new[] { "everyone" }, new[] { "um", "basically" }),
        new("filler-2", "uh yeah we should uh probably call the client back today",
            new[] { "client" }, new[] { "uh" }),
        new("filler-3", "it's like you know the best option we have like right now",
            new[] { "best option" }, new[] { "you know" }),
        // False starts
        new("false-start-1", "I went to the— I mean I drove to the office this morning",
            new[] { "office" }, new[] { "I went to the—" }),
        new("false-start-2", "send the inv— send the invoice to the customer by Friday",
            new[] { "invoice" }, new[] { "inv—" }),
        new("false-start-3", "we need to fix the— actually let's rewrite the whole landing page",
            new[] { "landing page" }, new[] { "the—" }),
        // Explicit corrections
        // Regression: a live dictation that exposed the 0.45
        // validator floor. The exact output is required (ratio 0.364).
        new("correction-1", "um so basically let's meet on Tuesday no wait Wednesday period",
            new[] { "Wednesday" }, new[] { "um", "basically", "Tuesday", "no wait" },
            ExpectedExact: "Let's meet on Wednesday."),
        new("correction-2", "set the budget to five hundred no actually six hundred dollars",
            new[] { "six hundred" }, new[] { "five hundred" }),
        new("correction-3", "assign it to Mike no sorry to Miguel Hernandez",
            new[] { "Miguel Hernandez" }, new[] { "Mike", "no sorry" }),
        // Names
        new("name-1", "please email Maria Vasquez at Northwind Labs about the proposal",
            new[] { "Maria Vasquez", "Northwind Labs" }, Array.Empty<string>()),
        new("name-2", "the client's name is Genevieve Okonkwo-Baptiste",
            new[] { "Genevieve Okonkwo-Baptiste" }, Array.Empty<string>()),
        new("name-3", "um the automations run through PostgreSQL for every client",
            new[] { "PostgreSQL" }, new[] { "um" }),
        // Dictionary spelling
        new("dictionary-1", "the agency is called north wind labs and it's based in Denver",
            new[] { "Northwind Labs", "Denver" }, new[] { "north wind" }),
        // Numbers
        new("number-1", "the total comes out to 4,850 dollars for the quarter",
            new[] { "4,850" }, Array.Empty<string>()),
        new("number-2", "call me at 305-555-0142 tomorrow morning",
            new[] { "305-555-0142" }, Array.Empty<string>()),
        new("number-3", "the invoice number is INV-2026-0713 for the June work",
            new[] { "INV-2026-0713" }, Array.Empty<string>()),
        new("number-4", "so um the p95 latency target is 4.0 seconds you know",
            new[] { "p95", "4.0" }, new[] { "um", "you know" }),
        // Punctuation instructions
        new("punctuation-1", "add milk comma eggs comma and bread period",
            new[] { "milk, eggs" }, new[] { " comma", " period" }),
        new("punctuation-2", "is this thing even working question mark",
            new[] { "?" }, new[] { "question mark" }),
        new("punctuation-3", "great job exclamation point see you tomorrow",
            new[] { "!" }, new[] { "exclamation point" }),
        // New line / paragraph
        new("newline-1", "first item new line second item new line third item",
            Array.Empty<string>(), new[] { "new line" }, SoftContain: new[] { "\n" }),
        new("paragraph-1", "that wraps up part one new paragraph moving on to part two",
            Array.Empty<string>(), new[] { "new paragraph" }, SoftContain: new[] { "\n" }),
        // URLs
        new("url-1", "check out https://example.com/pricing for the details",
            new[] { "https://example.com/pricing" }, Array.Empty<string>()),
        // Deliberate dictionary/URL collision. "Northwind Labs" is in
        // DictionaryTerms, but the URL must stay byte-for-byte lowercase —
        // protected tokens outrank dictionary substitutions.
        new("url-2", "the repo lives at github.com/northwind/voicetype if you need it",
            new[] { "github.com/northwind/voicetype" }, Array.Empty<string>()),
        // CLI flags
        new("cli-1", "run the deploy script with --dry-run and --verbose flags",
            new[] { "--dry-run", "--verbose" }, Array.Empty<string>(), Category: "terminal"),
        new("cli-2", "um use git commit -m and then push to main",
            new[] { "git commit -m" }, new[] { "um" }, Category: "terminal"),
        // File paths
        new("path-1", @"open C:\Users\dev\Documents\report.docx and check page two",
            new[] { @"C:\Users\dev\Documents\report.docx" }, Array.Empty<string>()),
        new("path-2", "the config loader lives in src/utils/settings_loader.py right now",
            new[] { "src/utils/settings_loader.py" }, Array.Empty<string>(), Category: "code_editor"),
        // Code tokens
        new("code-1", "rename the getUserData function to fetchUserData please",
            new[] { "getUserData", "fetchUserData" }, Array.Empty<string>(), Category: "code_editor"),
        // Prompt text (cleanup must not answer it)
        new("prompt-1", "um write a prompt that says you are a helpful assistant that summarizes emails",
            new[] { "you are a helpful assistant" }, new[] { "um" }),
    };

    [Fact]
    public async Task ThirtyUtteranceFixture_PreservesTokens_AndResolvesCleanup()
    {
        Assert.Equal(30, Fixture.Length);

        if (Environment.GetEnvironmentVariable("VOICETYPE_RUN_LIVE_FIXTURE") != "1")
        {
            _output.WriteLine("SKIPPED: live paid fixture is opt-in. Set VOICETYPE_RUN_LIVE_FIXTURE=1 to run it.");
            return;
        }

        var log = new NullLog();
        var secrets = new DpapiSecretStore(log);
        if (!secrets.HasApiKey)
        {
            _output.WriteLine("SKIPPED: no API key stored; fixture requires the live cleanup API.");
            return;
        }

        using var provider = new OpenAiResponsesCleanupProvider(secrets, log);
        var tokenFailures = new List<string>();
        var latencies = new List<long>();
        int resolved = 0;
        var stopwatch = new Stopwatch();

        foreach (var u in Fixture)
        {
            string final;
            bool degraded = false;
            stopwatch.Restart();
            try
            {
                using var cts = new CancellationTokenSource(20_000);
                final = await provider.CleanAsync(new CleanupRequest(u.Raw, u.Category, DictionaryTerms), cts.Token);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{u.Name}] API FAILURE: {ex.Message}");
                tokenFailures.Add($"{u.Name}: api failure");
                continue;
            }

            long ms = stopwatch.ElapsedMilliseconds;
            latencies.Add(ms);

            if (!CleanupValidator.IsAcceptable(u.Raw, final))
            {
                final = u.Raw;
                degraded = true;
            }

            var missing = u.MustContain.Where(t => !final.Contains(t, StringComparison.Ordinal)).ToList();
            if (u.ExpectedExact is not null && !string.Equals(final, u.ExpectedExact, StringComparison.Ordinal))
                tokenFailures.Add($"{u.Name}: expected exactly \"{u.ExpectedExact}\" but got \"{final.ReplaceLineEndings("\\n")}\"");
            // Word-boundary match: "summarizes" must not count as containing "um".
            bool cleanOk = !degraded
                && u.MustNotContain.All(t =>
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        final,
                        $@"(?<![\w]){System.Text.RegularExpressions.Regex.Escape(t)}(?![\w])",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                && (u.SoftContain is null || u.SoftContain.All(t => final.Contains(t, StringComparison.Ordinal)));
            if (missing.Count > 0) tokenFailures.Add($"{u.Name}: missing [{string.Join(", ", missing)}]");
            if (cleanOk) resolved++;

            _output.WriteLine($"[{u.Name}] {ms} ms  tokens={(missing.Count == 0 ? "OK" : "FAIL")}  cleanup={(cleanOk ? "resolved" : "unresolved")}{(degraded ? " (degraded->raw)" : "")}");
            _output.WriteLine($"  raw:   {u.Raw}");
            _output.WriteLine($"  final: {final.ReplaceLineEndings("\\n")}");
        }

        if (latencies.Count > 0)
        {
            var sorted = latencies.OrderBy(x => x).ToList();
            _output.WriteLine($"\nCleanup latency: p50={sorted[sorted.Count / 2]} ms, max={sorted[^1]} ms, n={sorted.Count}");
        }

        _output.WriteLine($"Resolved {resolved}/{Fixture.Length} cleanup cases (need >= 24).");
        Assert.True(tokenFailures.Count == 0,
            "Token preservation failures: " + string.Join(" | ", tokenFailures));
        Assert.True(resolved >= 24, $"Only {resolved}/{Fixture.Length} cleanup cases resolved (need >= 24).");
    }
}
