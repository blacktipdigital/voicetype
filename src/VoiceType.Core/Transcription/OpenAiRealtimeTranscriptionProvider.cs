using VoiceType.Core.Logging;
using VoiceType.Core.Security;

namespace VoiceType.Core.Transcription;

public sealed class OpenAiRealtimeTranscriptionProvider : ITranscriptionProvider
{
    private readonly ISecretStore _secrets;
    private readonly ILog _log;

    public OpenAiRealtimeTranscriptionProvider(ISecretStore secrets, ILog log)
    {
        _secrets = secrets;
        _log = log;
    }

    public ITranscriptionSession StartSession()
    {
        string? apiKey = _secrets.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("No API key configured.");

        return new OpenAiRealtimeSession(apiKey, _log);
    }
}
