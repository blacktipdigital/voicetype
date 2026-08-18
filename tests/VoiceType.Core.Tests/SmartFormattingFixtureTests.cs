using System.Diagnostics;
using VoiceType.Core.Cleanup;
using VoiceType.Core.Security;
using Xunit.Abstractions;

namespace VoiceType.Core.Tests;

/// <summary>
/// Smart Formatting fixture. LIVE, PAID, OPT-IN — runs only
/// with VOICETYPE_RUN_LIVE_FIXTURE=1 and a stored key; otherwise SKIPPED with
/// zero network calls. Every case is a hard assertion: numbered lists from
/// cardinals/ordinals, explicit bullets, spoken punctuation/paragraphs,
/// ambiguous numbers staying prose, and terminal input never becoming a list.
/// Plain ordinal Contains checks (not word-boundary) because the probes are
/// structural markers like "\n2. ".
/// </summary>
public class SmartFormattingFixtureTests
{
    private readonly ITestOutputHelper _output;

    public SmartFormattingFixtureTests(ITestOutputHelper output) => _output = output;

    private sealed record Case(
        string Name,
        string Raw,
        string Category,
        string[] MustContain,
        string[] MustNotContain);

    private static readonly Case[] Fixture =
    {
        new("ordinal-list",
            "for tomorrow first call the plumber second send the invoice third film the reel",
            "text_field",
            new[] { "1. ", "\n2. ", "\n3. ", "plumber", "invoice", "reel" },
            new[] { "first call" }),
        new("cardinal-list",
            "my priorities are one call Sarah two edit the video three update the website",
            "text_field",
            new[] { "1. ", "\n2. ", "\n3. ", "Sarah" },
            Array.Empty<string>()),
        new("explicit-bullets",
            "bullet point call Sarah bullet point edit the video bullet point update the website",
            "text_field",
            new[] { "- ", "\n- ", "Sarah", "website" },
            new[] { "bullet point" }),
        new("spoken-punctuation-paragraph",
            "thanks again for the call comma talk soon new paragraph best comma Maria",
            "text_field",
            new[] { ",", "\n", "Maria" },
            new[] { " comma", "new paragraph" }),
        new("ambiguous-numbers-stay-prose",
            "the kickoff is at three thirty on May two and the budget is 4,500 dollars",
            "text_field",
            new[] { "4,500" },
            new[] { "\n1.", "\n2.", "\n- " }),
        new("terminal-never-lists",
            "run step one of the deploy script and then step two",
            "terminal",
            Array.Empty<string>(),
            new[] { "\n1.", "\n2.", "\n- " }),
    };

    [Fact]
    public async Task SmartFormattingFixture_AllCasesHold()
    {
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
        var failures = new List<string>();
        var stopwatch = new Stopwatch();

        foreach (var c in Fixture)
        {
            string final;
            stopwatch.Restart();
            try
            {
                using var cts = new CancellationTokenSource(20_000);
                final = await provider.CleanAsync(
                    new CleanupRequest(c.Raw, c.Category, Array.Empty<string>()), cts.Token);
            }
            catch (Exception ex)
            {
                failures.Add($"{c.Name}: api failure ({ex.GetType().Name})");
                continue;
            }

            long ms = stopwatch.ElapsedMilliseconds;
            if (!CleanupValidator.IsAcceptable(c.Raw, final))
            {
                failures.Add($"{c.Name}: validator rejected output");
                _output.WriteLine($"[{c.Name}] {ms} ms REJECTED\n  raw:   {c.Raw}\n  final: {final.ReplaceLineEndings("\\n")}");
                continue;
            }

            var missing = c.MustContain.Where(t => !final.Contains(t, StringComparison.Ordinal)).ToList();
            var present = c.MustNotContain.Where(t => final.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0) failures.Add($"{c.Name}: missing [{string.Join(", ", missing.Select(m => m.ReplaceLineEndings("\\n")))}]");
            if (present.Count > 0) failures.Add($"{c.Name}: must-not-contain hit [{string.Join(", ", present)}]");

            _output.WriteLine($"[{c.Name}] {ms} ms {(missing.Count == 0 && present.Count == 0 ? "OK" : "FAIL")}");
            _output.WriteLine($"  raw:   {c.Raw}");
            _output.WriteLine($"  final: {final.ReplaceLineEndings("\\n")}");
        }

        Assert.True(failures.Count == 0, "Smart Formatting failures: " + string.Join(" | ", failures));
    }
}
