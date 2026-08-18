using VoiceType.Core.Targeting;

namespace VoiceType.Core.Insertion;

public enum InjectionFailure
{
    None,
    ClipboardWriteFailed,
    SendInputFailed,
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
    InjectionResult Inject(string text, TargetSnapshot target);

    /// <summary>Copies text to the clipboard for recovery flows.</summary>
    bool CopyToClipboard(string text);
}
