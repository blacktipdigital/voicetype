using VoiceType.Core.Audio;
using VoiceType.Core.Cleanup;
using VoiceType.Core.Hotkeys;
using VoiceType.Core.Insertion;
using VoiceType.Core.Logging;
using VoiceType.Core.Security;
using VoiceType.Core.Storage;
using VoiceType.Core.Targeting;
using VoiceType.Core.Time;
using VoiceType.Core.Transcription;

namespace VoiceType.Core.Tests;

internal sealed class FakeHotkeySource : IHotkeySource
{
    public event Action? ChordDown;
    public event Action? ChordUp;
    public event Action? CancelRequested;
    public event Action? PasteLastRequested;

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }

    public void PressChord() => ChordDown?.Invoke();
    public void ReleaseChord() => ChordUp?.Invoke();
    public void PressEsc() => CancelRequested?.Invoke();
    public void PressPasteLast() => PasteLastRequested?.Invoke();
}

internal sealed class FakeClock : IClock
{
    public long ElapsedMs { get; set; }
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

    public void Advance(long ms) => ElapsedMs += ms;
}

internal sealed class FakeTargetService : ITargetService
{
    public TargetSnapshot? Snapshot { get; set; }
    public bool StillValid { get; set; } = true;
    public bool ForegroundElevated { get; set; }
    public int CaptureCount { get; private set; }

    public TargetSnapshot? CaptureForeground()
    {
        CaptureCount++;
        return Snapshot;
    }

    public bool IsTargetStillValid(TargetSnapshot snapshot) => StillValid;

    public bool IsForegroundElevated() => ForegroundElevated;
}

internal sealed class FakeTextInjector : ITextInjector
{
    public List<(string Text, TargetSnapshot Target)> InjectCalls { get; } = new();
    public List<string> CopyCalls { get; } = new();
    public InjectionResult NextResult { get; set; } = InjectionResult.Ok;
    public bool CopySucceeds { get; set; } = true;

    /// <summary>Records what the late re-validation callback returned, or null when the caller passed none.</summary>
    public bool? StillValidResult { get; private set; }

    /// <summary>When set, the fake fails the way the real injector does once re-validation refuses.</summary>
    public bool HonourStillValid { get; set; }

    /// <summary>
    /// Runs inside Inject before re-validation, standing in for the real
    /// injector's clipboard backup and settling delay — the window in which
    /// focus can move after the coordinator already approved the target.
    /// </summary>
    public Action? DuringInsertionWindow { get; set; }

    public InjectionResult Inject(string text, TargetSnapshot target, Func<bool>? stillValid = null)
    {
        DuringInsertionWindow?.Invoke();
        StillValidResult = stillValid?.Invoke();

        if (HonourStillValid && StillValidResult == false)
            return InjectionResult.Fail(InjectionFailure.TargetChanged);

        InjectCalls.Add((text, target));
        return NextResult;
    }

    public bool CopyToClipboard(string text)
    {
        CopyCalls.Add(text);
        return CopySucceeds;
    }
}

internal sealed class FakeAudioCapture : IAudioCapture
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public string? LastDeviceId { get; private set; }
    public bool ThrowOnStart { get; set; }

    public event Action<byte[]>? ChunkReady;
    public event Action<float>? LevelChanged;
    public event Action<string>? CaptureError;

    public IReadOnlyList<AudioDevice> EnumerateDevices() => Array.Empty<AudioDevice>();

    public void Start(string? deviceId)
    {
        if (ThrowOnStart) throw new InvalidOperationException("no microphone");
        StartCount++;
        LastDeviceId = deviceId;
    }

    public void Stop() => StopCount++;
    public void Dispose() { }

    public void RaiseChunk(byte[] chunk) => ChunkReady?.Invoke(chunk);
    public void RaiseLevel(float level) => LevelChanged?.Invoke(level);
    public void RaiseError(string message) => CaptureError?.Invoke(message);
}

internal sealed class FakeTranscriptionSession : ITranscriptionSession
{
    public List<byte[]> Audio { get; } = new();
    public string FinalText { get; set; } = "hello world";
    public Exception? FinishThrows { get; set; }
    public string LastPartial { get; set; } = string.Empty;
    public bool Cancelled { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<string>? PartialTranscript;

    public void AddAudio(byte[] chunk) => Audio.Add(chunk);

    public Task<string> FinishAsync(CancellationToken cancellationToken) =>
        FinishThrows is null ? Task.FromResult(FinalText) : Task.FromException<string>(FinishThrows);

    public Task CancelAsync()
    {
        Cancelled = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public void RaisePartial(string text)
    {
        LastPartial = text;
        PartialTranscript?.Invoke(text);
    }
}

internal sealed class FakeTranscriptionProvider : ITranscriptionProvider
{
    public FakeTranscriptionSession Session { get; set; } = new();
    public bool ThrowOnStart { get; set; }
    public int SessionsStarted { get; private set; }

    public ITranscriptionSession StartSession()
    {
        if (ThrowOnStart) throw new InvalidOperationException("cannot connect");
        SessionsStarted++;
        return Session;
    }
}

internal sealed class FakeCleanupProvider : ICleanupProvider
{
    public Func<CleanupRequest, string> Handler { get; set; } = r => r.RawTranscript;
    public Exception? Throws { get; set; }
    public List<CleanupRequest> Requests { get; } = new();

    public Task<string> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Throws is null ? Task.FromResult(Handler(request)) : Task.FromException<string>(Throws);
    }
}

internal sealed class FakeHistoryStore : IHistoryStore
{
    public List<HistoryEntry> Entries { get; } = new();

    public void Add(HistoryEntry entry) => Entries.Add(entry);
    public IReadOnlyList<HistoryEntry> GetAll() => Entries.OrderByDescending(e => e.TimestampUtc).ToList();
    public void Delete(Guid id) => Entries.RemoveAll(e => e.Id == id);
    public void Purge() { }
}

internal sealed class FakeSecretStore : ISecretStore
{
    public string? Key { get; set; } = "sk-test";

    public bool HasApiKey => Key is not null;
    public string? GetApiKey() => Key;
    public void SetApiKey(string apiKey) => Key = apiKey;
    public void Clear() => Key = null;
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    public VoiceTypeSettings Settings { get; set; } = new();

    public VoiceTypeSettings Load() => Settings;
    public void Save(VoiceTypeSettings settings) => Settings = settings;
}

internal sealed class NullLog : ILog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

internal static class Snapshots
{
    public static TargetSnapshot Make(
        TargetKind kind = TargetKind.Standard,
        bool isPassword = false,
        bool isHigherIntegrity = false,
        bool isEditable = true,
        int[]? runtimeId = null) =>
        new(Hwnd: 0x1234, ProcessId: 42, ProcessName: "test", Kind: kind,
            IsHigherIntegrity: isHigherIntegrity, IsPassword: isPassword, IsEditableGuess: isEditable,
            FocusedRuntimeId: runtimeId ?? new[] { 7, 42 });
}
