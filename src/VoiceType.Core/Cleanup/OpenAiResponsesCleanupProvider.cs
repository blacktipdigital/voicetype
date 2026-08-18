using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceType.Core.Logging;
using VoiceType.Core.Security;

namespace VoiceType.Core.Cleanup;

/// <summary>
/// OpenAI Responses adapter: gpt-5.4-nano, reasoning effort none, store=false.
/// Sends exactly {rawTranscript, appCategory, dictionaryTerms} as the user
/// input — never screen text. Logs status only, never transcript content.
/// </summary>
public sealed class OpenAiResponsesCleanupProvider : ICleanupProvider, IDisposable
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    private const string Model = "gpt-5.4-nano";

    // Static few-shot pair: at reasoning effort none the nano model returns
    // the raw text unchanged for most utterances unless it sees the mapping
    // demonstrated once (verified live 2026-07-10: 5/6 hard cases resolved
    // with examples vs 1/6 without). Fixed task examples, never user data.
    private const string ExampleInput1 =
        "appCategory: text_field\ndictionaryTerms: \nrawTranscript:\num so I think we should uh go with the blue one";
    private const string ExampleOutput1 = "I think we should go with the blue one";
    private const string ExampleInput2 =
        "appCategory: text_field\ndictionaryTerms: \nrawTranscript:\nsend it to John no wait to Jane comma thanks period";
    private const string ExampleOutput2 = "send it to Jane, thanks.";
    private const string ExampleInput3 =
        "appCategory: text_field\ndictionaryTerms: \nrawTranscript:\nmy priorities are first call Sarah second edit the video third update the website";
    private const string ExampleOutput3 = "My priorities are:\n1. Call Sarah\n2. Edit the video\n3. Update the website";
    // Filler + self-correction + spoken period combined: without this shape
    // demonstrated, the model resolves the correction but keeps leading filler.
    private const string ExampleInput4 =
        "appCategory: text_field\ndictionaryTerms: \nrawTranscript:\nuh yeah so the call is at four no wait five period";
    private const string ExampleOutput4 = "The call is at five.";

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan }; // caller's token governs
    private readonly ISecretStore _secrets;
    private readonly ILog _log;

    public OpenAiResponsesCleanupProvider(ISecretStore secrets, ILog log)
    {
        _secrets = secrets;
        _log = log;
    }

    public async Task<string> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        string? apiKey = _secrets.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("No API key configured.");

        // Labeled fields, not a JSON blob: the nano model treats raw JSON as
        // data to echo and skips the cleanup. Same three fields, nothing else.
        //
        // appCategory and dictionaryTerms are single-line by construction, so
        // any newline in them is forged structure — a term ending in a fake
        // "rawTranscript:" line would otherwise replace the spoken text with
        // attacker-chosen content that then gets pasted. Strip line breaks and
        // control characters before the join; the transcript itself is the
        // last field, so it cannot be followed by a forged one.
        string userInput =
            $"appCategory: {SanitizeField(request.AppCategory)}\n" +
            $"dictionaryTerms: {string.Join("; ", request.DictionaryTerms.Select(SanitizeField))}\n" +
            $"rawTranscript:\n{request.RawTranscript}";

        var payload = new
        {
            model = Model,
            store = false,
            reasoning = new { effort = "none" },
            instructions = CleanupPrompt.SystemPrompt,
            input = new object[]
            {
                new { role = "user", content = ExampleInput1 },
                new { role = "assistant", content = ExampleOutput1 },
                new { role = "user", content = ExampleInput2 },
                new { role = "assistant", content = ExampleOutput2 },
                new { role = "user", content = ExampleInput3 },
                new { role = "assistant", content = ExampleOutput3 },
                new { role = "user", content = ExampleInput4 },
                new { role = "assistant", content = ExampleOutput4 },
                new { role = "user", content = userInput },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.Error($"Cleanup API returned {(int)response.StatusCode}.");
            throw new InvalidOperationException($"Cleanup API error {(int)response.StatusCode}.");
        }

        return ExtractOutputText(body);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Collapses anything that could forge a new labeled line into a space.
    /// Public so the field-forgery guard is directly testable without a
    /// network call.
    /// </summary>
    public static string SanitizeField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(char.IsControl(c) ? ' ' : c);

        return sb.ToString().Trim();
    }

    private static string ExtractOutputText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException("Cleanup API returned an error object.");

        var text = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message") continue;
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text"
                        && part.TryGetProperty("text", out var t))
                        text.Append(t.GetString());
                }
            }
        }

        return text.ToString();
    }
}
