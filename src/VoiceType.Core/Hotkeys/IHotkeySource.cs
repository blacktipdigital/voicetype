namespace VoiceType.Core.Hotkeys;

public interface IHotkeySource : IDisposable
{
    event Action? ChordDown;
    event Action? ChordUp;
    event Action? CancelRequested;
    event Action? PasteLastRequested;

    void Start();
    void Stop();
}
