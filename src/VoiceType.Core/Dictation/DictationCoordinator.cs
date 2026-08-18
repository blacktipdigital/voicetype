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

namespace VoiceType.Core.Dictation;

public enum DictationState
{
    Idle,
    Recording,
    Finalizing,
    Cleaning,
    Inserting,
    CopyRecovery,
}

public enum RecoveryReason
{
    NoTarget,
    PasswordField,
    FocusChanged,
    HigherIntegrityTarget,
    UneditableTarget,
    InjectionFailed,
    TranscriptionIncomplete,
}

public sealed record RecoveryContext(string Transcript, RecoveryReason Reason, bool CopiedToClipboard);

/// <summary>
/// The only state owner. Consumes hotkey signals, snapshots the target and
/// starts capture + transcription at chord-down, finalizes on release,
/// refuses unsafe targets, and hands off to the injector exactly once.
/// UI observes events and never runs the pipeline.
/// </summary>
public sealed class DictationCoordinator : IDisposable
{
    /// <summary>Holds shorter than this are accidental taps and cancel silently.</summary>
    public const int MinHoldMs = 300;

    /// <summary>Poll interval for the recording-time guard (elevated foreground ends the session).</summary>
    public const int GuardIntervalMs = 100;

    /// <summary>How long after release to wait for the final transcript.</summary>
    public const int FinalizeTimeoutMs = 15_000;

    /// <summary>How long the cleanup pass may take before falling back to raw text.</summary>
    public const int CleanupTimeoutMs = 10_000;

    private readonly object _gate = new();
    private readonly Timer _guardTimer;
    private readonly IHotkeySource _hotkeys;
    private readonly IAudioCapture _audio;
    private readonly ITargetService _targets;
    private readonly ITranscriptionProvider _transcription;
    private readonly ICleanupProvider _cleanup;
    private readonly ITextInjector _injector;
    private readonly ISecretStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly IHistoryStore _history;
    private readonly IClock _clock;
    private readonly ILog _log;

    private DictationState _state = DictationState.Idle;
    private TargetSnapshot? _target;
    private ITranscriptionSession? _session;
    private long _chordDownAtMs;
    private long _releaseAtMs;
    private string? _lastResult;

    public DictationCoordinator(
        IHotkeySource hotkeys,
        IAudioCapture audio,
        ITargetService targets,
        ITranscriptionProvider transcription,
        ICleanupProvider cleanup,
        ITextInjector injector,
        ISecretStore secrets,
        ISettingsStore settings,
        IHistoryStore history,
        IClock clock,
        ILog log)
    {
        _hotkeys = hotkeys;
        _audio = audio;
        _targets = targets;
        _transcription = transcription;
        _cleanup = cleanup;
        _injector = injector;
        _secrets = secrets;
        _settings = settings;
        _history = history;
        _clock = clock;
        _log = log;

        _guardTimer = new Timer(_ => GuardTick(), null, Timeout.Infinite, Timeout.Infinite);

        _hotkeys.ChordDown += OnChordDown;
        _hotkeys.ChordUp += OnChordUp;
        _hotkeys.CancelRequested += OnCancel;
        _hotkeys.PasteLastRequested += OnPasteLast;
        _audio.ChunkReady += OnAudioChunk;
        _audio.CaptureError += OnCaptureError;
    }

    /// <summary>Last final result (cleaned or raw fallback), kept in memory for Shift+Alt+Z.</summary>
    public string? LastResult => _lastResult;

    public DictationState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>The in-flight finalize/insert flow; awaitable by tests and shutdown.</summary>
    public Task LastFlow { get; private set; } = Task.CompletedTask;

    public event Action<DictationState>? StateChanged;
    public event Action<RecoveryContext>? RecoveryRequested;
    public event Action<string>? TextInserted;
    public event Action<string>? PartialTranscript;
    public event Action<string>? ErrorOccurred;

    public void Dispose()
    {
        _guardTimer.Dispose();
        _hotkeys.ChordDown -= OnChordDown;
        _hotkeys.ChordUp -= OnChordUp;
        _hotkeys.CancelRequested -= OnCancel;
        _hotkeys.PasteLastRequested -= OnPasteLast;
        _audio.ChunkReady -= OnAudioChunk;
        _audio.CaptureError -= OnCaptureError;
    }

    /// <summary>
    /// Recording-time guard, driven by an internal 100 ms timer. If an
    /// elevated window takes focus mid-dictation, the keyboard hook goes
    /// blind (UIPI) — Esc can no longer cancel and the release may be
    /// missed. End the session immediately; the target-validity check then
    /// routes the transcript to copy recovery.
    /// </summary>
    internal void GuardTick()
    {
        lock (_gate)
        {
            if (_state != DictationState.Recording) return;
        }

        if (!_targets.IsForegroundElevated()) return;

        _log.Warn("Foreground window turned elevated during recording; ending session.");
        OnChordUp();
    }

    private void OnChordDown()
    {
        if (!_secrets.HasApiKey)
        {
            _log.Warn("Dictation attempted without an API key.");
            ErrorOccurred?.Invoke("No API key set — right-click the tray icon and open Settings.");
            return;
        }

        lock (_gate)
        {
            if (_state != DictationState.Idle)
            {
                _log.Info("Chord down ignored; a session is already active.");
                return;
            }

            _state = DictationState.Recording;
            _chordDownAtMs = _clock.ElapsedMs;
        }

        // Snapshot before anything else so insertion goes to where dictation
        // started. Single-threaded signal pump: no cancel can interleave here.
        _target = SafeCaptureTarget();

        try
        {
            var session = _transcription.StartSession();
            session.PartialTranscript += OnPartial;
            _session = session;
            _audio.Start(_settings.Load().MicrophoneDeviceId);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to start capture or session.", ex);
            AbandonSession();
            lock (_gate) _state = DictationState.Idle;
            ErrorOccurred?.Invoke("Could not start recording — check your microphone and network.");
            StateChanged?.Invoke(DictationState.Idle);
            return;
        }

        _guardTimer.Change(GuardIntervalMs, GuardIntervalMs);
        StateChanged?.Invoke(DictationState.Recording);
    }

    private void OnCancel()
    {
        lock (_gate)
        {
            if (_state != DictationState.Recording) return;
            _state = DictationState.Idle;
        }

        _guardTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _audio.Stop();
        AbandonSession();
        _target = null;
        _log.Info("Cancelled with Esc; nothing inserted.");
        StateChanged?.Invoke(DictationState.Idle);
    }

    private void OnChordUp()
    {
        bool accidentalTap;
        TargetSnapshot? target;
        ITranscriptionSession? session;

        lock (_gate)
        {
            if (_state != DictationState.Recording) return;
            accidentalTap = _clock.ElapsedMs - _chordDownAtMs < MinHoldMs;
            _state = accidentalTap ? DictationState.Idle : DictationState.Finalizing;
            _releaseAtMs = _clock.ElapsedMs;
            target = _target;
            session = _session;
            _target = null;
            _session = null;
        }

        _guardTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _audio.Stop();

        if (accidentalTap)
        {
            AbandonSession(session);
            _log.Info("Accidental tap; nothing inserted.");
            StateChanged?.Invoke(DictationState.Idle);
            return;
        }

        StateChanged?.Invoke(DictationState.Finalizing);
        LastFlow = FinishAsync(target, session);
    }

    private async Task FinishAsync(TargetSnapshot? target, ITranscriptionSession? session)
    {
        if (session is null)
        {
            TransitionToIdle();
            return;
        }

        string raw;
        try
        {
            using var timeout = new CancellationTokenSource(FinalizeTimeoutMs);
            raw = await session.FinishAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("Final transcript unavailable.", ex);
            string partial = session.LastPartial;
            await DisposeQuietlyAsync(session).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(partial))
            {
                // Never lose dictated words: recover the best partial text.
                _lastResult = partial;
                Recover(partial, RecoveryReason.TranscriptionIncomplete);
                SaveHistory(partial, $"Recovered: {RecoveryReason.TranscriptionIncomplete}", null);
            }
            else
            {
                ErrorOccurred?.Invoke("Transcription failed — nothing was captured.");
                TransitionToIdle();
            }

            return;
        }

        await DisposeQuietlyAsync(session).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            TransitionToIdle();
            return;
        }

        var (text, degradedReason) = await CleanAsync(raw, target).ConfigureAwait(false);
        _lastResult = text;

        // Degradation must be visible in History, not just Trace.
        // Fixed labels only — never exception text or content.
        string suffix = degradedReason is null ? string.Empty : $" (cleanup degraded: {degradedReason})";

        RecoveryReason? refusal = CheckTarget(target);
        if (refusal is not null)
        {
            Recover(text, refusal.Value);
            SaveHistory(text, $"Recovered: {refusal.Value}{suffix}", null);
            return;
        }

        lock (_gate) _state = DictationState.Inserting;
        StateChanged?.Invoke(DictationState.Inserting);

        InjectionResult result;
        try
        {
            result = _injector.Inject(text, target!);
        }
        catch (Exception ex)
        {
            _log.Error("Injection threw.", ex);
            Recover(text, RecoveryReason.InjectionFailed);
            SaveHistory(text, $"Recovered: {RecoveryReason.InjectionFailed}{suffix}", null);
            return;
        }

        if (!result.Success)
        {
            Recover(text, RecoveryReason.InjectionFailed);
            SaveHistory(text, $"Recovered: {RecoveryReason.InjectionFailed}{suffix}", null);
            return;
        }

        long latencyMs = _clock.ElapsedMs - _releaseAtMs;
        _log.Info($"Release-to-paste latency: {latencyMs} ms.");
        SaveHistory(text, $"Inserted{suffix}", latencyMs);
        TextInserted?.Invoke(text);
        TransitionToIdle();
    }

    /// <summary>
    /// Cleanup pass: strict prompt, {rawTranscript, appCategory,
    /// dictionaryTerms} only. Any failure, timeout, or validator rejection
    /// falls back to the raw transcript and returns a fixed degraded-reason
    /// label ("timeout", "validator rejection", "API failure") for History.
    /// </summary>
    private async Task<(string Text, string? DegradedReason)> CleanAsync(string raw, TargetSnapshot? target)
    {
        lock (_gate) _state = DictationState.Cleaning;
        StateChanged?.Invoke(DictationState.Cleaning);

        try
        {
            var request = new CleanupRequest(raw, MapAppCategory(target), _settings.Load().DictionaryTerms);
            using var timeout = new CancellationTokenSource(CleanupTimeoutMs);

            string cleaned;
            try
            {
                cleaned = await _cleanup.CleanAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log.Warn("Cleanup timed out; using raw text (degraded).");
                return (raw, "timeout");
            }

            if (CleanupValidator.IsAcceptable(raw, cleaned))
                return (cleaned.Trim(), null);

            _log.Warn("Cleanup output rejected by validator; using raw text (degraded).");
            return (raw, "validator rejection");
        }
        catch (Exception ex)
        {
            _log.Error("Cleanup pass failed; using raw text (degraded).", ex);
            return (raw, "API failure");
        }
    }

    private static string MapAppCategory(TargetSnapshot? target) => target?.Kind switch
    {
        TargetKind.WindowsTerminal or TargetKind.ClassicConsole => "terminal",
        TargetKind.Editor => "code_editor",
        TargetKind.Standard => "text_field",
        _ => "unknown",
    };

    private void SaveHistory(string text, string status, long? latencyMs)
    {
        try
        {
            _history.Add(new HistoryEntry(Guid.NewGuid(), _clock.UtcNow, text, status, latencyMs));
        }
        catch (Exception ex)
        {
            _log.Error("History write failed.", ex);
        }
    }

    /// <summary>
    /// Shift+Alt+Z: re-insert the last final result at the current focus.
    /// Runs the same refusal checks as normal insertion; no history entry
    /// (the text is already stored).
    /// </summary>
    private void OnPasteLast()
    {
        lock (_gate)
        {
            if (_state != DictationState.Idle) return;
        }

        string? text = _lastResult;
        if (string.IsNullOrEmpty(text))
        {
            ErrorOccurred?.Invoke("Nothing to paste yet — dictate something first.");
            return;
        }

        TargetSnapshot? target = SafeCaptureTarget();
        RecoveryReason? refusal = CheckTarget(target);
        if (refusal is not null)
        {
            Recover(text, refusal.Value);
            return;
        }

        lock (_gate) _state = DictationState.Inserting;
        StateChanged?.Invoke(DictationState.Inserting);

        try
        {
            var result = _injector.Inject(text, target!);
            if (!result.Success)
            {
                Recover(text, RecoveryReason.InjectionFailed);
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Paste-last injection threw.", ex);
            Recover(text, RecoveryReason.InjectionFailed);
            return;
        }

        TextInserted?.Invoke(text);
        TransitionToIdle();
    }

    private RecoveryReason? CheckTarget(TargetSnapshot? target)
    {
        if (target is null) return RecoveryReason.NoTarget;
        if (target.IsPassword) return RecoveryReason.PasswordField;
        if (target.IsHigherIntegrity) return RecoveryReason.HigherIntegrityTarget;

        // Same window AND same focused element (UIA runtime ID); catches Tab
        // moves between fields inside one window, not just window switches.
        if (!_targets.IsTargetStillValid(target)) return RecoveryReason.FocusChanged;

        // Terminals and known editors host custom controls the UIA probe can't
        // vouch for (VS Code chat prompt, terminal panes); trust the process,
        // bounded by the element-identity check above when UIA is available.
        if (!target.IsEditableGuess && !target.IsTrustedProcess) return RecoveryReason.UneditableTarget;
        return null;
    }

    private void Recover(string transcript, RecoveryReason reason)
    {
        bool copied = false;
        try
        {
            copied = _injector.CopyToClipboard(transcript);
        }
        catch (Exception ex)
        {
            _log.Error("Recovery clipboard copy failed.", ex);
        }

        lock (_gate) _state = DictationState.CopyRecovery;
        _log.Warn($"Insertion refused ({reason}); transcript {(copied ? "copied to clipboard" : "NOT copied — clipboard unavailable")}.");
        StateChanged?.Invoke(DictationState.CopyRecovery);
        RecoveryRequested?.Invoke(new RecoveryContext(transcript, reason, copied));
        TransitionToIdle();
    }

    private void TransitionToIdle()
    {
        lock (_gate) _state = DictationState.Idle;
        StateChanged?.Invoke(DictationState.Idle);
    }

    private void OnAudioChunk(byte[] chunk)
    {
        // Volatile-ish read is fine: chunks after session teardown are dropped.
        _session?.AddAudio(chunk);
    }

    private void OnCaptureError(string message)
    {
        lock (_gate)
        {
            if (_state != DictationState.Recording) return;
            _state = DictationState.Idle;
        }

        _guardTimer.Change(Timeout.Infinite, Timeout.Infinite);
        AbandonSession();
        _target = null;
        ErrorOccurred?.Invoke(message);
        StateChanged?.Invoke(DictationState.Idle);
    }

    private void OnPartial(string text) => PartialTranscript?.Invoke(text);

    private void AbandonSession(ITranscriptionSession? session = null)
    {
        session ??= Interlocked.Exchange(ref _session, null);
        if (session is null) return;

        session.PartialTranscript -= OnPartial;
        _ = Task.Run(async () =>
        {
            try
            {
                await session.CancelAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Session teardown failed.", ex);
            }
        });
    }

    private async Task DisposeQuietlyAsync(ITranscriptionSession session)
    {
        session.PartialTranscript -= OnPartial;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("Session dispose failed.", ex);
        }
    }

    private TargetSnapshot? SafeCaptureTarget()
    {
        try
        {
            return _targets.CaptureForeground();
        }
        catch (Exception ex)
        {
            _log.Error("Target capture failed.", ex);
            return null;
        }
    }
}
