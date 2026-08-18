namespace VoiceType.Core.Audio;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Microphone capture seam. Emits 24 kHz mono PCM16 chunks (~100 ms each)
/// while running. Audio lives only in memory buffers; implementations must
/// never write it to disk.
/// </summary>
public interface IAudioCapture : IDisposable
{
    IReadOnlyList<AudioDevice> EnumerateDevices();

    /// <summary>Starts capture on the given device (null = system default).</summary>
    void Start(string? deviceId);

    /// <summary>Stops capture and clears internal buffers.</summary>
    void Stop();

    /// <summary>24 kHz mono PCM16, roughly 100 ms per chunk.</summary>
    event Action<byte[]>? ChunkReady;

    /// <summary>Peak level 0..1 per chunk, for the overlay meter.</summary>
    event Action<float>? LevelChanged;

    /// <summary>Device failure or capture fault; capture has stopped.</summary>
    event Action<string>? CaptureError;
}
