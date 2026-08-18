using System.IO;
using VoiceType.Core.Dictation;
using VoiceType.Core.Storage;

namespace VoiceType.Core.Tests;

/// <summary>
/// Proves a dictionary term persisted through the
/// real JsonSettingsStore survives an app restart (new store instance over
/// the same file) and reaches CleanupRequest through DictationCoordinator.
/// Fakes everywhere else — zero API calls.
/// </summary>
public class SettingsIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VoiceTypeTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DictionaryTerm_PersistsAcrossRestart_AndReachesCleanupRequest()
    {
        // First "run": persist the term the way SettingsWindow now does.
        var firstRun = new JsonSettingsStore(new NullLog(), SettingsPath);
        firstRun.Save(firstRun.Load() with { DictionaryTerms = new[] { "Northwind Labs" } });

        Assert.Contains("Northwind Labs", File.ReadAllText(SettingsPath));

        // "Restart": a fresh store instance over the same file feeds the coordinator.
        var secondRun = new JsonSettingsStore(new NullLog(), SettingsPath);
        var hotkeys = new FakeHotkeySource();
        var clock = new FakeClock();
        var targets = new FakeTargetService { Snapshot = Snapshots.Make() };
        var cleanup = new FakeCleanupProvider();

        using var coordinator = new DictationCoordinator(
            hotkeys, new FakeAudioCapture(), targets, new FakeTranscriptionProvider(),
            cleanup, new FakeTextInjector(), new FakeSecretStore(), secondRun,
            new FakeHistoryStore(), clock, new NullLog());

        hotkeys.PressChord();
        clock.Advance(1000);
        hotkeys.ReleaseChord();
        await coordinator.LastFlow;

        var request = Assert.Single(cleanup.Requests);
        Assert.Contains("Northwind Labs", request.DictionaryTerms);
    }
}
