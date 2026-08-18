namespace VoiceType.Core.Transcription;

/// <summary>
/// Streaming transcription seam. A session accepts 24 kHz mono PCM16 audio
/// from the moment it is created (buffering locally until its transport is
/// ready), surfaces partial text, and produces one final transcript on
/// <see cref="FinishAsync"/>.
/// </summary>
public interface ITranscriptionProvider
{
    /// <summary>Returns immediately; the session connects in the background and buffers audio meanwhile.</summary>
    ITranscriptionSession StartSession();
}

public interface ITranscriptionSession : IAsyncDisposable
{
    /// <summary>Queues a 24 kHz mono PCM16 chunk. Safe to call before the transport is connected.</summary>
    void AddAudio(byte[] chunk);

    /// <summary>Raised as partial transcript text arrives. May fire on any thread.</summary>
    event Action<string>? PartialTranscript;

    /// <summary>Best partial text seen so far; recovery fallback when the final transcript never arrives.</summary>
    string LastPartial { get; }

    /// <summary>Commits the audio and awaits the final transcript.</summary>
    Task<string> FinishAsync(CancellationToken cancellationToken);

    /// <summary>Abandons the session; nothing is transcribed or kept.</summary>
    Task CancelAsync();
}
