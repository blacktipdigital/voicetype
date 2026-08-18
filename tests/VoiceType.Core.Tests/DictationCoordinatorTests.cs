using VoiceType.Core.Dictation;
using VoiceType.Core.Insertion;
using VoiceType.Core.Targeting;

namespace VoiceType.Core.Tests;

public class DictationCoordinatorTests
{
    private readonly FakeHotkeySource _hotkeys = new();
    private readonly FakeClock _clock = new();
    private readonly FakeTargetService _targets = new();
    private readonly FakeTextInjector _injector = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriptionProvider _transcription = new();
    private readonly FakeCleanupProvider _cleanup = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeHistoryStore _history = new();

    private DictationCoordinator CreateCoordinator() =>
        new(_hotkeys, _audio, _targets, _transcription, _cleanup, _injector, _secrets, _settings, _history, _clock, new NullLog());

    private async Task RunSession(DictationCoordinator coordinator, long holdMs = 1000)
    {
        _hotkeys.PressChord();
        _clock.Advance(holdMs);
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;
    }

    [Fact]
    public async Task HappyPath_InsertsExactlyOnce()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "hello there";

        await RunSession(coordinator);

        var call = Assert.Single(_injector.InjectCalls);
        Assert.Equal("hello there", call.Text);
        Assert.Empty(_injector.CopyCalls);
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Equal(1, _audio.StartCount);
        Assert.Equal(1, _audio.StopCount);
        Assert.True(_transcription.Session.Disposed);
    }

    [Fact]
    public async Task AudioChunks_FlowIntoSessionWhileRecording()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        _hotkeys.PressChord();
        _audio.RaiseChunk(new byte[] { 1, 2 });
        _audio.RaiseChunk(new byte[] { 3, 4 });
        _clock.Advance(1000);
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;

        Assert.Equal(2, _transcription.Session.Audio.Count);
    }

    [Fact]
    public async Task ShortTap_CancelsSessionWithoutInserting()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        await RunSession(coordinator, holdMs: DictationCoordinator.MinHoldMs - 1);
        await Task.Delay(50); // background session teardown

        Assert.Empty(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
        Assert.True(_transcription.Session.Cancelled);
        Assert.Equal(1, _audio.StopCount);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Esc_CancelsSessionAndCapture()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        _hotkeys.PressChord();
        _clock.Advance(1000);
        _hotkeys.PressEsc();
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;
        await Task.Delay(50); // background session teardown

        Assert.Empty(_injector.InjectCalls);
        Assert.True(_transcription.Session.Cancelled);
        Assert.Equal(1, _audio.StopCount);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void NoApiKey_NeverStartsRecording()
    {
        using var coordinator = CreateCoordinator();
        _secrets.Key = null;
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        _hotkeys.PressChord();

        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Equal(0, _audio.StartCount);
        Assert.Equal(0, _transcription.SessionsStarted);
        Assert.NotNull(error);
    }

    [Fact]
    public void SessionStartFailure_ReportsErrorAndReturnsToIdle()
    {
        using var coordinator = CreateCoordinator();
        _transcription.ThrowOnStart = true;
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        _hotkeys.PressChord();

        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task MicStartFailure_ReportsErrorAndReturnsToIdle()
    {
        using var coordinator = CreateCoordinator();
        _audio.ThrowOnStart = true;
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        _hotkeys.PressChord();
        await Task.Delay(50); // background session teardown

        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.NotNull(error);
        Assert.True(_transcription.Session.Cancelled);
    }

    [Fact]
    public async Task ProviderTimeout_WithPartial_RecoversPartialText()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinishThrows = new TimeoutException();
        _transcription.Session.LastPartial = "partial words";
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(RecoveryReason.TranscriptionIncomplete, recovery!.Reason);
        Assert.Equal("partial words", recovery.Transcript);
        Assert.Single(_injector.CopyCalls);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task ProviderTimeout_WithoutPartial_ErrorsToIdle()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinishThrows = new TimeoutException();
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
        Assert.NotNull(error);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task PartialTranscript_IsForwardedWhileRecording()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        var partials = new List<string>();
        coordinator.PartialTranscript += partials.Add;

        _hotkeys.PressChord();
        _transcription.Session.RaisePartial("hel");
        _transcription.Session.RaisePartial("hello");
        _clock.Advance(1000);
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;

        Assert.Equal(new[] { "hel", "hello" }, partials);
    }

    [Fact]
    public async Task PasswordTarget_NeverPastes_CopiesForRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(isPassword: true);
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Single(_injector.CopyCalls);
        Assert.Equal(RecoveryReason.PasswordField, recovery!.Reason);
        Assert.True(recovery.CopiedToClipboard);
    }

    [Fact]
    public async Task FocusChanged_NeverPastes_CopiesForRecovery()
    {
        // Covers window switches AND same-window field moves (two fields in
        // one browser window): both invalidate the element-identity check.
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _targets.StillValid = false;
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(RecoveryReason.FocusChanged, recovery!.Reason);
    }

    [Fact]
    public async Task HigherIntegrityTarget_NeverPastes_CopiesForRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(isHigherIntegrity: true);
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(RecoveryReason.HigherIntegrityTarget, recovery!.Reason);
    }

    [Fact]
    public async Task NoTarget_CopiesForRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = null;
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(RecoveryReason.NoTarget, recovery!.Reason);
    }

    [Fact]
    public async Task UneditableUnknownControl_CopiesOnly()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(kind: TargetKind.Unknown, isEditable: false);
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(RecoveryReason.UneditableTarget, recovery!.Reason);
    }

    [Theory]
    [InlineData(TargetKind.WindowsTerminal)]
    [InlineData(TargetKind.ClassicConsole)]
    [InlineData(TargetKind.Editor)]
    public async Task UneditableTerminalOrEditor_StillPastes(TargetKind kind)
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(kind: kind, isEditable: false);

        await RunSession(coordinator);

        Assert.Single(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
    }

    [Fact]
    public async Task InjectionFailure_CopiesForRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _injector.NextResult = InjectionResult.Fail(InjectionFailure.SendInputFailed);
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        await RunSession(coordinator);

        Assert.Single(_injector.InjectCalls);
        Assert.Single(_injector.CopyCalls);
        Assert.Equal(RecoveryReason.InjectionFailed, recovery!.Reason);
    }

    [Fact]
    public async Task ChordDownWhileActive_IsIgnored()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        _hotkeys.PressChord();
        _hotkeys.PressChord(); // rapid repeat must not start a second session
        Assert.Equal(1, _targets.CaptureCount);
        Assert.Equal(1, _audio.StartCount);
        Assert.Equal(1, _transcription.SessionsStarted);

        _clock.Advance(1000);
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;

        Assert.Single(_injector.InjectCalls);
    }

    [Fact]
    public async Task ReleaseWithoutPress_DoesNothing()
    {
        using var coordinator = CreateCoordinator();
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;

        Assert.Empty(_injector.InjectCalls);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task EmptyTranscript_InsertsNothing()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "   ";

        await RunSession(coordinator);

        Assert.Empty(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task CaptureErrorMidRecording_CancelsToIdleWithError()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        _hotkeys.PressChord();
        _audio.RaiseError("Microphone capture failed.");
        await Task.Delay(50); // background session teardown

        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.NotNull(error);
        Assert.True(_transcription.Session.Cancelled);
        Assert.Empty(_injector.InjectCalls);
    }

    [Fact]
    public async Task ElevatedForegroundMidSession_GuardEndsSessionWithRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;

        _hotkeys.PressChord();
        _clock.Advance(1000);
        _targets.ForegroundElevated = true;
        _targets.StillValid = false; // elevated window took focus
        coordinator.GuardTick();
        await coordinator.LastFlow;

        Assert.Empty(_injector.InjectCalls);
        Assert.Single(_injector.CopyCalls);
        Assert.Equal(RecoveryReason.FocusChanged, recovery!.Reason);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task ElevatedForegroundWithinTapWindow_CancelsSilently()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        _hotkeys.PressChord();
        _clock.Advance(100);
        _targets.ForegroundElevated = true;
        coordinator.GuardTick();
        await coordinator.LastFlow;

        Assert.Empty(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void GuardTickWhileIdle_DoesNothing()
    {
        using var coordinator = CreateCoordinator();
        _targets.ForegroundElevated = true;

        coordinator.GuardTick();

        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Empty(_injector.InjectCalls);
        Assert.Empty(_injector.CopyCalls);
    }

    [Fact]
    public async Task StateTransitions_FollowRecordingFinalizingInsertingIdle()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        var states = new List<DictationState>();
        coordinator.StateChanged += states.Add;

        await RunSession(coordinator);

        Assert.Equal(
            new[]
            {
                DictationState.Recording,
                DictationState.Finalizing,
                DictationState.Cleaning,
                DictationState.Inserting,
                DictationState.Idle,
            },
            states);
    }

    // ---- M2: cleanup, history, paste-last ----

    [Fact]
    public async Task CleanupOutput_IsInserted_WhenValid()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "um hello there period";
        _cleanup.Handler = _ => "Hello there.";

        await RunSession(coordinator);

        Assert.Equal("Hello there.", Assert.Single(_injector.InjectCalls).Text);
    }

    [Fact]
    public async Task CleanupRequest_CarriesCategoryAndDictionary()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(kind: TargetKind.WindowsTerminal, isEditable: false);
        _settings.Settings = _settings.Settings with { DictionaryTerms = new[] { "Northwind", "PostgreSQL" } };

        await RunSession(coordinator);

        var request = Assert.Single(_cleanup.Requests);
        Assert.Equal("terminal", request.AppCategory);
        Assert.Equal(new[] { "Northwind", "PostgreSQL" }, request.DictionaryTerms);
    }

    [Fact]
    public async Task CleanupRejectedByValidator_FallsBackToRaw()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "this is a fairly long raw transcript with many words in it";
        _cleanup.Handler = _ => "x"; // ratio far below 0.45 → rejected

        await RunSession(coordinator);

        Assert.Equal(_transcription.Session.FinalText, Assert.Single(_injector.InjectCalls).Text);
    }

    [Fact]
    public async Task CleanupFailure_FallsBackToRaw_HistoryShowsApiFailure()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "raw words";
        _cleanup.Throws = new InvalidOperationException("api down");

        await RunSession(coordinator);

        Assert.Equal("raw words", Assert.Single(_injector.InjectCalls).Text);
        var entry = Assert.Single(_history.Entries);
        Assert.Equal("Inserted (cleanup degraded: API failure)", entry.DeliveryStatus);
        Assert.DoesNotContain("api down", entry.DeliveryStatus); // no exception text stored
    }

    [Fact]
    public async Task CleanupTimeout_FallsBackToRaw_HistoryShowsTimeout()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "raw words";
        _cleanup.Throws = new OperationCanceledException();

        await RunSession(coordinator);

        Assert.Equal("raw words", Assert.Single(_injector.InjectCalls).Text);
        Assert.Equal("Inserted (cleanup degraded: timeout)", Assert.Single(_history.Entries).DeliveryStatus);
    }

    [Fact]
    public async Task ValidatorRejection_HistoryShowsValidatorRejection()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "this is a fairly long raw transcript with many words in it";
        _cleanup.Handler = _ => "x"; // far below the 0.30 floor

        await RunSession(coordinator);

        Assert.Equal(
            "Inserted (cleanup degraded: validator rejection)",
            Assert.Single(_history.Entries).DeliveryStatus);
    }

    [Fact]
    public async Task Regression_ExactCleanedResult_IsInsertedAndSaved()
    {
        // The 0.364-ratio pair from the M2 review: the cleaned text, not the
        // raw transcript, must be inserted and stored, with no degraded mark.
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "um so basically let's meet on Tuesday no wait Wednesday period";
        _cleanup.Handler = _ => "Let's meet on Wednesday.";

        await RunSession(coordinator);

        Assert.Equal("Let's meet on Wednesday.", Assert.Single(_injector.InjectCalls).Text);
        var entry = Assert.Single(_history.Entries);
        Assert.Equal("Let's meet on Wednesday.", entry.Text);
        Assert.Equal("Inserted", entry.DeliveryStatus);
    }

    [Fact]
    public async Task SuccessfulInsertion_WritesHistoryWithLatency()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _cleanup.Handler = _ => "final text here";

        await RunSession(coordinator);

        var entry = Assert.Single(_history.Entries);
        Assert.Equal("final text here", entry.Text);
        Assert.Equal("Inserted", entry.DeliveryStatus);
        Assert.NotNull(entry.LatencyMs);
    }

    [Fact]
    public async Task Refusal_WritesHistoryWithRecoveredStatus()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make(isPassword: true);

        await RunSession(coordinator);

        var entry = Assert.Single(_history.Entries);
        Assert.StartsWith("Recovered", entry.DeliveryStatus);
    }

    [Fact]
    public async Task EmptyTranscript_WritesNoHistory()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _transcription.Session.FinalText = "  ";

        await RunSession(coordinator);

        Assert.Empty(_history.Entries);
    }

    [Fact]
    public async Task PasteLast_ReinsertsPreviousResult()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _cleanup.Handler = _ => "remembered text";

        await RunSession(coordinator);
        _hotkeys.PressPasteLast();

        Assert.Equal(2, _injector.InjectCalls.Count);
        Assert.Equal("remembered text", _injector.InjectCalls[1].Text);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void PasteLast_WithNothingDictated_ShowsError()
    {
        using var coordinator = CreateCoordinator();
        string? error = null;
        coordinator.ErrorOccurred += m => error = m;

        _hotkeys.PressPasteLast();

        Assert.Empty(_injector.InjectCalls);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task PasteLast_IntoPasswordField_RefusesWithRecovery()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _cleanup.Handler = _ => "secret-adjacent text";
        await RunSession(coordinator);

        _targets.Snapshot = Snapshots.Make(isPassword: true);
        RecoveryContext? recovery = null;
        coordinator.RecoveryRequested += ctx => recovery = ctx;
        _hotkeys.PressPasteLast();

        Assert.Single(_injector.InjectCalls); // only the original insertion
        Assert.Equal(RecoveryReason.PasswordField, recovery!.Reason);
    }

    [Fact]
    public async Task PasteLast_WhileRecording_IsIgnored()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();
        _cleanup.Handler = _ => "text";
        await RunSession(coordinator);

        _hotkeys.PressChord();
        _hotkeys.PressPasteLast();

        Assert.Single(_injector.InjectCalls); // no paste-last during recording
        _clock.Advance(1000);
        _hotkeys.ReleaseChord();
        await coordinator.LastFlow;
    }

    [Fact]
    public async Task SessionCanRestartAfterCompletion()
    {
        using var coordinator = CreateCoordinator();
        _targets.Snapshot = Snapshots.Make();

        await RunSession(coordinator);
        _transcription.Session = new FakeTranscriptionSession();
        await RunSession(coordinator);

        Assert.Equal(2, _injector.InjectCalls.Count);
        Assert.Equal(2, _transcription.SessionsStarted);
    }
}
