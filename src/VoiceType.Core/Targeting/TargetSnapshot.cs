namespace VoiceType.Core.Targeting;

public enum TargetKind
{
    Unknown,
    Standard,
    Editor,
    WindowsTerminal,
    ClassicConsole,
}

/// <summary>
/// Snapshot of the insertion target captured at chord-down. Deliberately
/// excludes the window title. Owned by the coordinator; the overlay never
/// touches it. <paramref name="FocusedRuntimeId"/> is the UIA runtime ID of
/// the focused element, used to detect same-window focus moves (e.g. Tab to
/// another field) before insertion; null when the UIA probe failed.
/// </summary>
public sealed record TargetSnapshot(
    nint Hwnd,
    int ProcessId,
    string ProcessName,
    TargetKind Kind,
    bool IsHigherIntegrity,
    bool IsPassword,
    bool IsEditableGuess,
    int[]? FocusedRuntimeId)
{
    public bool IsTerminal => Kind is TargetKind.WindowsTerminal or TargetKind.ClassicConsole;

    /// <summary>Terminals and known editors host custom controls UIA can't vouch for; the process itself is trusted.</summary>
    public bool IsTrustedProcess => IsTerminal || Kind == TargetKind.Editor;
}
