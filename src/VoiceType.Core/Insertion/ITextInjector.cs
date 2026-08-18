using VoiceType.Core.Targeting;

namespace VoiceType.Core.Insertion;

public enum InjectionFailure
{
    None,
    ClipboardWriteFailed,
    SendInputFailed,

    /// <summary>The target stopped matching between the caller's check and the keystroke.</summary>
    TargetChanged,
}

public readonly record struct InjectionResult(bool Success, InjectionFailure Failure)
{
    public static InjectionResult Ok { get; } = new(true, InjectionFailure.None);
    public static InjectionResult Fail(InjectionFailure failure) => new(false, failure);
}

public interface ITextInjector
{
    /// <summary>
    /// Clipboard-backed one-shot paste into the snapshotted target. Never
    /// retries with a second method, never sends Enter. On failure the
    /// transcript stays on the clipboard for recovery.
    /// </summary>
    /// <param name="stillValid">
    /// Re-checked immediately before the keystroke is sent. The caller's own
    /// check happens before clipboard work and a settling delay, so focus can
    /// move in between; without this the paste would land in whatever window
    /// took over. Returning false aborts with <see cref="InjectionFailure.TargetChanged"/>
    /// and leaves the transcript on the clipboard.
    /// </param>
    InjectionResult Inject(string text, TargetSnapshot target, Func<bool>? stillValid = null);

    /// <summary>Copies text to the clipboard for recovery flows.</summary>
    bool CopyToClipboard(string text);
}
