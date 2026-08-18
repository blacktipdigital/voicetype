namespace VoiceType.Core.Targeting;

public interface ITargetService
{
    /// <summary>Captures the current foreground window and focused element. Null when there is no usable foreground window.</summary>
    TargetSnapshot? CaptureForeground();

    /// <summary>
    /// True when the snapshot's window is still foreground AND the same UIA
    /// element still has focus (catches Tab moves inside one window). When
    /// the snapshot has no runtime ID (UIA probe failed at capture), only
    /// trusted processes (terminals, known editors) remain valid.
    /// </summary>
    bool IsTargetStillValid(TargetSnapshot snapshot);

    /// <summary>True when the current foreground window belongs to a higher-integrity process.</summary>
    bool IsForegroundElevated();
}
