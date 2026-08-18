namespace VoiceType.Core.Insertion;

/// <summary>
/// Runs a delegate on a short-lived STA thread. OLE clipboard access
/// (System.Windows.Clipboard) requires STA, and callers here run on
/// thread-pool threads.
/// </summary>
internal static class StaRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        })
        {
            IsBackground = true,
            Name = "VoiceType.ClipboardSta",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(Timeout))
            throw new TimeoutException("STA clipboard operation timed out.");
        if (error is not null)
            throw error;
        return result;
    }
}
