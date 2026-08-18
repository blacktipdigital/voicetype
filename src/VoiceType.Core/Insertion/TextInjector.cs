using VoiceType.Core.Logging;
using VoiceType.Core.Targeting;

namespace VoiceType.Core.Insertion;

public sealed class TextInjector : ITextInjector
{
    private const int PreSendDelayMs = 50;
    private const int RestoreDelayMs = 500;

    private readonly ILog _log;

    public TextInjector(ILog log) => _log = log;

    public InjectionResult Inject(string text, TargetSnapshot target, Func<bool>? stillValid = null)
    {
        ClipboardSnapshot backup = ClipboardLease.Backup();
        if (backup.CaptureFailed)
            _log.Warn("Clipboard backup unavailable; prior contents will not be restored.");
        else if (backup.Degraded)
            _log.Warn("Clipboard backup degraded; some formats could not be copied.");

        if (!ClipboardLease.SetText(text))
        {
            _log.Error("Clipboard write failed; nothing sent to target.");
            return InjectionResult.Fail(InjectionFailure.ClipboardWriteFailed);
        }

        uint ourSequence = ClipboardLease.SequenceNumber;

        KeySender.ReleasePhysicalModifiers();
        Thread.Sleep(PreSendDelayMs);

        // Last gate before the keystroke. Everything above — the clipboard
        // backup (unbounded: it copies whatever formats the user had) and the
        // settling delay — runs after the caller validated the target, so this
        // is the only check that is actually adjacent to the paste. Refusing
        // here leaves the transcript on the clipboard, which is exactly what
        // the recovery flow wants, so the backup is deliberately not restored.
        if (stillValid is not null && !stillValid())
        {
            _log.Warn("Target changed during insertion; transcript left on clipboard.");
            return InjectionResult.Fail(InjectionFailure.TargetChanged);
        }

        if (!KeySender.SendPasteChord(GetChord(target.Kind)))
        {
            // Transcript stays on the clipboard so the user can paste manually.
            _log.Error("SendInput failed; transcript left on clipboard.");
            return InjectionResult.Fail(InjectionFailure.SendInputFailed);
        }

        ScheduleRestore(backup, ourSequence);
        return InjectionResult.Ok;
    }

    public bool CopyToClipboard(string text) => ClipboardLease.SetText(text);

    private void ScheduleRestore(ClipboardSnapshot backup, uint ourSequence)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(RestoreDelayMs).ConfigureAwait(false);
            try
            {
                // Only restore if nobody else wrote to the clipboard since our write.
                if (ClipboardLease.SequenceNumber != ourSequence) return;
                if (backup.CaptureFailed) return;

                if (!ClipboardLease.Restore(backup))
                    _log.Warn("Clipboard restore failed; transcript remains on clipboard.");
            }
            catch (Exception ex)
            {
                _log.Error("Clipboard restore failed.", ex);
            }
        });
    }

    private static PasteChord GetChord(TargetKind kind) => kind switch
    {
        TargetKind.WindowsTerminal => PasteChord.CtrlShiftV,
        TargetKind.ClassicConsole => PasteChord.ShiftInsert,
        _ => PasteChord.CtrlV,
    };
}
