using System.Runtime.InteropServices;
using System.Threading.Channels;
using VoiceType.Core.Logging;
using VoiceType.Core.Native;

namespace VoiceType.Core.Hotkeys;

/// <summary>
/// WH_KEYBOARD_LL hook on a dedicated message-loop thread. The callback only
/// feeds the chord detector and queues signals; it does no blocking work and
/// records no keystrokes. Signals are raised in order from a single pump task.
/// </summary>
public sealed class KeyboardHookSource : IHotkeySource
{
    /// <summary>Interval for the physical-key watchdog that recovers key-ups UIPI hid from the hook.</summary>
    public const int WatchdogIntervalMs = 100;

    private readonly ILog _log;
    private readonly ChordDetector _detector = new();
    private readonly object _detectorSync = new();
    private readonly Channel<ChordSignal> _signals = Channel.CreateUnbounded<ChordSignal>(
        new UnboundedChannelOptions { SingleReader = true });

    private Timer? _watchdog;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private LowLevelKeyboardProc? _hookProc; // field keeps the delegate alive for the native hook
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action? ChordDown;
    public event Action? ChordUp;
    public event Action? CancelRequested;
    public event Action? PasteLastRequested;

    public KeyboardHookSource(ILog log) => _log = log;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookThread is not null) return;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => PumpSignalsAsync(_cts.Token));

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "VoiceType.KeyboardHook",
        };
        _hookThread.Start();

        _watchdog = new Timer(_ => WatchdogTick(), null, WatchdogIntervalMs, WatchdogIntervalMs);
    }

    public void Stop()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        if (_hookThread is null) return;
        NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        _hookThread.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _signals.Writer.TryComplete();
    }

    private void HookThreadMain()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _hookProc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _hookProc, NativeMethods.GetModuleHandle(null), 0);

        if (_hookHandle == 0)
        {
            _log.Error($"SetWindowsHookEx failed, error {Marshal.GetLastWin32Error()}.");
            return;
        }

        _log.Info("Keyboard hook installed.");
        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
        _log.Info("Keyboard hook removed.");
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
            if (!injected)
            {
                int message = (int)wParam;
                bool isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                bool isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    ChordDecision decision;
                    lock (_detectorSync)
                    {
                        decision = _detector.Process((int)data.vkCode, isDown);
                    }

                    if (decision.Signal != ChordSignal.None)
                        _signals.Writer.TryWrite(decision.Signal);
                    if (decision.Swallow)
                        return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void WatchdogTick()
    {
        IReadOnlyList<ChordDecision> decisions;
        lock (_detectorSync)
        {
            decisions = _detector.ReconcilePhysical(
                vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0);
        }

        foreach (var decision in decisions)
            _signals.Writer.TryWrite(decision.Signal);
    }

    private async Task PumpSignalsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var signal in _signals.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    switch (signal)
                    {
                        case ChordSignal.ChordDown: ChordDown?.Invoke(); break;
                        case ChordSignal.ChordUp: ChordUp?.Invoke(); break;
                        case ChordSignal.Cancel: CancelRequested?.Invoke(); break;
                        case ChordSignal.PasteLast: PasteLastRequested?.Invoke(); break;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Hotkey handler threw.", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
