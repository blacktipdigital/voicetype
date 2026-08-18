using System.Windows;

namespace VoiceType.Core.Insertion;

/// <summary>
/// Detaches clipboard contents into a standalone DataObject so they survive
/// the clipboard being overwritten. Copies every format the source exposes
/// (text, HTML, RTF, DIB images, file drops, private formats); a format
/// whose data cannot be materialized is skipped and counted, never fatal.
/// </summary>
internal static class DataObjectCopier
{
    public static (DataObject Copy, int CopiedFormats, int FailedFormats) Copy(IDataObject source)
    {
        var copy = new DataObject();
        int copied = 0;
        int failed = 0;

        string[] formats;
        try
        {
            formats = source.GetFormats(autoConvert: false);
        }
        catch
        {
            return (copy, 0, 1);
        }

        foreach (string format in formats.Distinct())
        {
            try
            {
                if (!source.GetDataPresent(format, autoConvert: false)) continue;
                object? data = source.GetData(format, autoConvert: false);
                if (data is null) continue;
                copy.SetData(format, data, autoConvert: false);
                copied++;
            }
            catch
            {
                failed++;
            }
        }

        return (copy, copied, failed);
    }
}
