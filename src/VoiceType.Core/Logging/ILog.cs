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
        System.Diagnostics.Trace.WriteLine($"[VoiceType][ERROR] {message}{(exception is null ? string.Empty : $" :: {exception.GetType().Name}: {exception.Message}")}");
}
