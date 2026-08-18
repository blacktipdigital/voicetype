using System.Windows;
using VoiceType.Core.Audio;
using VoiceType.Core.Cleanup;
using VoiceType.Core.Dictation;
using VoiceType.Core.Hosting;
using VoiceType.Core.Hotkeys;
using VoiceType.Core.Insertion;
using VoiceType.Core.Logging;
using VoiceType.Core.Security;
using VoiceType.Core.Storage;
using VoiceType.Core.Targeting;
using VoiceType.Core.Time;
using VoiceType.Core.Transcription;
using WinForms = System.Windows.Forms;

namespace VoiceType.App;

public partial class App : System.Windows.Application
{
    private KeyboardHookSource? _hotkeys;
    private WasapiAudioCapture? _audio;
    private DictationCoordinator? _coordinator;
    private TextInjector? _injector;
    private DpapiSecretStore? _secrets;
    private JsonSettingsStore? _settings;
    private JsonHistoryStore? _historyStore;
    private System.Threading.Timer? _historyPurgeTimer;
    private WinForms.NotifyIcon? _tray;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;

    private SingleInstanceGuard? _instanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check BEFORE any tray icon, hook, capture, or
        // provider exists: a second instance would double-hook and double-paste.
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsPrimaryInstance)
        {
            _instanceGuard.Dispose();
            _instanceGuard = null;
            Shutdown();
            return;
        }

        var log = new TraceLog();
        var clock = new SystemClock();
        _hotkeys = new KeyboardHookSource(log);
        _audio = new WasapiAudioCapture(log);
        _injector = new TextInjector(log);
        _secrets = new DpapiSecretStore(log);
        _settings = new JsonSettingsStore(log);
        _historyStore = new JsonHistoryStore(clock, log);
        _coordinator = new DictationCoordinator(
            _hotkeys,
            _audio,
            new TargetService(log),
            new OpenAiRealtimeTranscriptionProvider(_secrets, log),
            new OpenAiResponsesCleanupProvider(_secrets, log),
            _injector,
            _secrets,
            _settings,
            _historyStore,
            clock,
            log);

        // Retention: purge at startup and every six hours while running
        // (the store also purges on every write).
        _historyStore.Purge();
        _historyPurgeTimer = new System.Threading.Timer(
            _ => _historyStore?.Purge(), null,
            TimeSpan.FromHours(6), TimeSpan.FromHours(6));

        _overlay = new OverlayWindow();

        _coordinator.StateChanged += state =>
            Dispatcher.BeginInvoke(() => _overlay?.ShowState(state));

        _coordinator.PartialTranscript += text =>
            Dispatcher.BeginInvoke(() => _overlay?.ShowPartial(text));

        _coordinator.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => _overlay?.ShowError(message));

        _audio.LevelChanged += level =>
            Dispatcher.BeginInvoke(() => _overlay?.SetLevel(level));

        _coordinator.RecoveryRequested += context =>
            Dispatcher.BeginInvoke(() =>
            {
                var window = new RecoveryWindow(context, text => _injector!.CopyToClipboard(text), OpenHistory);
                window.Show();
                window.Activate();
            });

        _tray = BuildTrayIcon();
        _hotkeys.Start();

        // First run: the key must be set (with the data disclosure) before
        // dictation can do anything.
        if (!_secrets.HasApiKey)
            OpenSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _historyPurgeTimer?.Dispose();
        _coordinator?.Dispose();
        _hotkeys?.Dispose();
        _audio?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private void OpenHistory()
    {
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }

        _historyWindow = new HistoryWindow(_historyStore!, text => _injector!.CopyToClipboard(text));
        _historyWindow.Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_secrets!, _settings!, _audio!);
        _settingsWindow.Show();
    }

    private WinForms.NotifyIcon BuildTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("VoiceType — hold Ctrl+Win to dictate, Shift+Alt+Z to paste last") { Enabled = false });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("History…", null, (_, _) => Dispatcher.BeginInvoke(OpenHistory));
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.BeginInvoke(OpenSettings));
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        return new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "VoiceType — hold Ctrl+Win to dictate",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }
}
