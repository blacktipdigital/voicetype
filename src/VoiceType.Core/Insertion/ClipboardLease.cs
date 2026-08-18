using System.Windows;
using VoiceType.Core.Native;

namespace VoiceType.Core.Insertion;

/// <summary>Detached copy of the clipboard taken before VoiceType writes to it.</summary>
internal sealed class ClipboardSnapshot
{
    /// <summary>Backup failed entirely: never clear or overwrite on restore — leave the transcript in place.</summary>
    public static readonly ClipboardSnapshot Unavailable = new(null, captureFailed: true, degraded: false);

    /// <summary>Clipboard held nothing: restore re-clears it.</summary>
    public static readonly ClipboardSnapshot Empty = new(null, captureFailed: false, degraded: false);

    public ClipboardSnapshot(DataObject? data, bool captureFailed, bool degraded)
    {
        Data = data;
        CaptureFailed = captureFailed;
        Degraded = degraded;
    }

    public DataObject? Data { get; }
    public bool CaptureFailed { get; }

    /// <summary>Some formats could not be copied; the rest restore normally.</summary>
    public bool Degraded { get; }
}

/// <summary>
/// OLE clipboard lease: backs up all clipboard formats via a detached
/// DataObject, writes the transcript, and restores the original contents.
/// All access runs on STA threads as OLE requires.
/// </summary>
internal static class ClipboardLease
{
    public static uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    public static ClipboardSnapshot Backup()
    {
        try
        {
            return StaRunner.Run(() =>
            {
                IDataObject? source = Clipboard.GetDataObject();
                string[]? formats;
                try { formats = source?.GetFormats(autoConvert: false); }
                catch { return ClipboardSnapshot.Unavailable; }

                if (source is null || formats is null || formats.Length == 0)
                    return ClipboardSnapshot.Empty;

                var (copy, copied, failedCount) = DataObjectCopier.Copy(source);
                if (copied == 0)
                    return ClipboardSnapshot.Unavailable;

                return new ClipboardSnapshot(copy, captureFailed: false, degraded: failedCount > 0);
            });
        }
        catch
        {
            return ClipboardSnapshot.Unavailable;
        }
    }

    public static bool SetText(string text)
    {
        try
        {
            return StaRunner.Run(() =>
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true);
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Restores the snapshot. Never called when capture failed — the transcript stays instead.</summary>
    public static bool Restore(ClipboardSnapshot snapshot)
    {
        if (snapshot.CaptureFailed) return false;

        try
        {
            return StaRunner.Run(() =>
            {
                if (snapshot.Data is null)
                    Clipboard.Clear();
                else
                    Clipboard.SetDataObject(snapshot.Data, copy: true);
                return true;
            });
        }
        catch
        {
            return false;
        }
    }
}
