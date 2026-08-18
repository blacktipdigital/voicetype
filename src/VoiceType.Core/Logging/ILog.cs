namespace VoiceType.Core.Logging;

/// <summary>
/// Minimal logging seam. Implementations must never log transcript content,
/// audio, keystrokes, window titles, or secrets.
/// </summary>
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class TraceLog : ILog
{
    public void Info(string message) => System.Diagnostics.Trace.WriteLine($"[VoiceType][INFO] {message}");
    public void Warn(string message) => System.Diagnostics.Trace.WriteLine($"[VoiceType][WARN] {message}");
    public void Error(string message, Exception? exception = null) =>
        // Type name only. Exception messages from JSON and HTTP layers can embed
        // fragments of the payload being parsed, which for this app means
        // transcript text — the contract above forbids logging that.
        System.Diagnostics.Trace.WriteLine($"[VoiceType][ERROR] {message}{(exception is null ? string.Empty : $" :: {exception.GetType().Name}")}");
}
