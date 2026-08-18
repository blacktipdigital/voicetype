namespace VoiceType.Core.Hosting;

/// <summary>
/// Named-mutex single-instance guard. Two running instances each own a global
/// keyboard hook and paste independently, so the app must acquire this
/// before creating any UI, hook, capture, or
/// provider, hold it for its lifetime, and exit immediately when it is not
/// the primary instance.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultName = "VoiceType.SingleInstance";

    private readonly Mutex _mutex;
    private bool _disposed;

    public SingleInstanceGuard(string name = DefaultName)
    {
        // Local\ scope: per interactive session, which is per user here.
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out bool createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>True only for the first instance; later launches must exit.</summary>
    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsPrimaryInstance)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not owned on this thread; abandoning is fine */ }
        }

        _mutex.Dispose();
    }
}
