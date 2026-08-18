using VoiceType.Core.Hotkeys;

namespace VoiceType.Core.Tests;

public class ChordDetectorTests
{
    private const int LCtrl = 0xA2;
    private const int RCtrl = 0xA3;
    private const int LWin = 0x5B;
    private const int RWin = 0x5C;
    private const int Esc = 0x1B;
    private const int KeyA = 0x41;

    [Fact]
    public void CtrlThenWin_FiresChordDown()
    {
        var detector = new ChordDetector();
        Assert.Equal(ChordSignal.None, detector.Process(LCtrl, true).Signal);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(LWin, true).Signal);
    }

    [Fact]
    public void WinThenCtrl_FiresChordDown()
    {
        var detector = new ChordDetector();
        Assert.Equal(ChordSignal.None, detector.Process(LWin, true).Signal);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(LCtrl, true).Signal);
    }

    [Fact]
    public void RightSideModifiers_AlsoFormChord()
    {
        var detector = new ChordDetector();
        detector.Process(RCtrl, true);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(RWin, true).Signal);
    }

    [Theory]
    [InlineData(LCtrl)]
    [InlineData(LWin)]
    public void ReleasingEitherKey_FiresChordUp(int releasedKey)
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);
        Assert.Equal(ChordSignal.ChordUp, detector.Process(releasedKey, false).Signal);
    }

    [Fact]
    public void EscDuringChord_CancelsAndSwallows()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);

        var down = detector.Process(Esc, true);
        Assert.Equal(ChordSignal.Cancel, down.Signal);
        Assert.True(down.Swallow);

        var up = detector.Process(Esc, false);
        Assert.Equal(ChordSignal.None, up.Signal);
        Assert.True(up.Swallow);
    }

    [Fact]
    public void EscWithoutChord_PassesThrough()
    {
        var detector = new ChordDetector();
        var decision = detector.Process(Esc, true);
        Assert.Equal(ChordSignal.None, decision.Signal);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void OtherKeys_AreIgnoredAndNeverSwallowed()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);
        var decision = detector.Process(KeyA, true);
        Assert.Equal(ChordSignal.None, decision.Signal);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void SecondCtrlWhileChordHeld_DoesNotRefireChordDown()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);
        Assert.Equal(ChordSignal.None, detector.Process(RCtrl, true).Signal);
    }

    [Fact]
    public void ChordCanRepeatAfterFullRelease()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);
        detector.Process(LWin, false);
        detector.Process(LCtrl, false);

        detector.Process(LCtrl, true);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(LWin, true).Signal);
    }

    // Shift+Alt+Z paste-last

    private const int LShift = 0xA0;
    private const int LAlt = 0xA4;
    private const int KeyZ = 0x5A;

    [Fact]
    public void ShiftAltZ_FiresPasteLast_AndSwallowsZ()
    {
        var detector = new ChordDetector();
        detector.Process(LShift, true);
        detector.Process(LAlt, true);

        var down = detector.Process(KeyZ, true);
        Assert.Equal(ChordSignal.PasteLast, down.Signal);
        Assert.True(down.Swallow);

        var up = detector.Process(KeyZ, false);
        Assert.Equal(ChordSignal.None, up.Signal);
        Assert.True(up.Swallow);
    }

    [Fact]
    public void ZWithoutBothModifiers_PassesThrough()
    {
        var detector = new ChordDetector();
        detector.Process(LShift, true);
        var decision = detector.Process(KeyZ, true); // no Alt
        Assert.Equal(ChordSignal.None, decision.Signal);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void ShiftAltZ_DoesNotDisturbDictationChord()
    {
        var detector = new ChordDetector();
        detector.Process(LShift, true);
        detector.Process(LAlt, true);
        detector.Process(KeyZ, true);
        detector.Process(KeyZ, false);
        detector.Process(LAlt, false);
        detector.Process(LShift, false);

        detector.Process(LCtrl, true);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(LWin, true).Signal);
    }

    // ReconcilePhysical: the watchdog path for key-ups UIPI hid from the hook.

    [Fact]
    public void Reconcile_MissedKeyUps_FiresChordUp()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);

        // User released both keys while an elevated window had focus.
        var signals = detector.ReconcilePhysical(_ => false);

        Assert.Contains(signals, d => d.Signal == ChordSignal.ChordUp);
    }

    [Fact]
    public void Reconcile_KeysStillHeld_DoesNothing()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);

        Assert.Empty(detector.ReconcilePhysical(_ => true));
    }

    [Fact]
    public void Reconcile_NothingTracked_DoesNothing()
    {
        var detector = new ChordDetector();
        Assert.Empty(detector.ReconcilePhysical(_ => false));
    }

    [Fact]
    public void Reconcile_ThenNewChord_FiresChordDownAgain()
    {
        var detector = new ChordDetector();
        detector.Process(LCtrl, true);
        detector.Process(LWin, true);
        detector.ReconcilePhysical(_ => false); // clears the stale state

        detector.Process(LCtrl, true);
        Assert.Equal(ChordSignal.ChordDown, detector.Process(LWin, true).Signal);
    }
}
