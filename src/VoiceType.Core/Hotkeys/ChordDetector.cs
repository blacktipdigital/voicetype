namespace VoiceType.Core.Hotkeys;

public enum ChordSignal
{
    None,
    ChordDown,
    ChordUp,
    Cancel,
    PasteLast,
}

public readonly record struct ChordDecision(ChordSignal Signal, bool Swallow);

/// <summary>
/// Pure chord state for the fixed Ctrl+Win hold-to-talk chord plus Esc cancel.
/// Fed one key transition at a time; owns no threads and reads no other keys,
/// so the low-level hook records nothing beyond the chord keys themselves.
/// </summary>
public sealed class ChordDetector
{
    private const int VkEscape = 0x1B;
    private const int VkZ = 0x5A;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLCtrl = 0xA2;
    private const int VkRCtrl = 0xA3;
    private const int VkLAlt = 0xA4;
    private const int VkRAlt = 0xA5;

    private bool _lctrl;
    private bool _rctrl;
    private bool _lwin;
    private bool _rwin;
    private bool _lshift;
    private bool _rshift;
    private bool _lalt;
    private bool _ralt;
    private bool _chordActive;

    private bool ShiftDown => _lshift || _rshift;
    private bool AltDown => _lalt || _ralt;

    public ChordDecision Process(int vkCode, bool isDown)
    {
        switch (vkCode)
        {
            case VkLCtrl: _lctrl = isDown; break;
            case VkRCtrl: _rctrl = isDown; break;
            case VkLWin: _lwin = isDown; break;
            case VkRWin: _rwin = isDown; break;
            case VkLShift: _lshift = isDown; break;
            case VkRShift: _rshift = isDown; break;
            case VkLAlt: _lalt = isDown; break;
            case VkRAlt: _ralt = isDown; break;
            case VkEscape when _chordActive:
                // Swallow Esc (down and up) while the chord is held so the
                // cancel keystroke never reaches the target app.
                return new ChordDecision(isDown ? ChordSignal.Cancel : ChordSignal.None, Swallow: true);
            case VkZ when ShiftDown && AltDown:
                // Shift+Alt+Z pastes the last result; swallow Z so no stray
                // character reaches the target app.
                return new ChordDecision(isDown ? ChordSignal.PasteLast : ChordSignal.None, Swallow: true);
            default:
                return new ChordDecision(ChordSignal.None, false);
        }

        bool bothDown = (_lctrl || _rctrl) && (_lwin || _rwin);
        if (bothDown && !_chordActive)
        {
            _chordActive = true;
            return new ChordDecision(ChordSignal.ChordDown, false);
        }

        if (!bothDown && _chordActive)
        {
            _chordActive = false;
            return new ChordDecision(ChordSignal.ChordUp, false);
        }

        return new ChordDecision(ChordSignal.None, false);
    }

    /// <summary>
    /// Watchdog entry point. Windows UIPI hides key-ups from this hook while
    /// an elevated window has focus, which would leave chord keys tracked as
    /// down forever. For every tracked-down key the probe reports as
    /// physically up, this synthesizes the missed key-up through the normal
    /// state machine and returns the resulting signals.
    /// </summary>
    public IReadOnlyList<ChordDecision> ReconcilePhysical(Func<int, bool> isPhysicallyDown)
    {
        List<ChordDecision>? results = null;

        Reconcile(VkLCtrl, _lctrl);
        Reconcile(VkRCtrl, _rctrl);
        Reconcile(VkLWin, _lwin);
        Reconcile(VkRWin, _rwin);
        Reconcile(VkLShift, _lshift);
        Reconcile(VkRShift, _rshift);
        Reconcile(VkLAlt, _lalt);
        Reconcile(VkRAlt, _ralt);

        return results ?? (IReadOnlyList<ChordDecision>)Array.Empty<ChordDecision>();

        void Reconcile(int vk, bool trackedDown)
        {
            if (!trackedDown || isPhysicallyDown(vk)) return;
            var decision = Process(vk, isDown: false);
            if (decision.Signal != ChordSignal.None)
                (results ??= new List<ChordDecision>()).Add(decision);
        }
    }
}
