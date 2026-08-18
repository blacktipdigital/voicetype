using VoiceType.Core.Native;

namespace VoiceType.Core.Insertion;

public enum PasteChord
{
    CtrlV,
    CtrlShiftV,
    ShiftInsert,
}

internal static class KeySender
{
    /// <summary>
    /// Sends key-up events for any chord modifiers the user is still holding
    /// (they may release Ctrl before Win, or the reverse). Without this the
    /// paste chord combines with the held keys — e.g. a held Win turns Ctrl+V
    /// into Win+Ctrl+V. Our own hook ignores injected events, so these do not
    /// re-enter the chord detector.
    /// </summary>
    public static void ReleasePhysicalModifiers()
    {
        int[] modifiers =
        {
            NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
            NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL,
            NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
            NativeMethods.VK_LMENU, NativeMethods.VK_RMENU,
        };

        var ups = modifiers
            .Where(vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
            .Select(vk => MakeInput(vk, down: false))
            .ToArray();

        if (ups.Length > 0)
            NativeMethods.SendInput((uint)ups.Length, ups, INPUT.Size);
    }

    public static bool SendPasteChord(PasteChord chord)
    {
        INPUT[] inputs = chord switch
        {
            PasteChord.CtrlShiftV => new[]
            {
                MakeInput(NativeMethods.VK_CONTROL, true),
                MakeInput(NativeMethods.VK_SHIFT, true),
                MakeInput(NativeMethods.VK_V, true),
                MakeInput(NativeMethods.VK_V, false),
                MakeInput(NativeMethods.VK_SHIFT, false),
                MakeInput(NativeMethods.VK_CONTROL, false),
            },
            PasteChord.ShiftInsert => new[]
            {
                MakeInput(NativeMethods.VK_SHIFT, true),
                MakeInput(NativeMethods.VK_INSERT, true),
                MakeInput(NativeMethods.VK_INSERT, false),
                MakeInput(NativeMethods.VK_SHIFT, false),
            },
            _ => new[]
            {
                MakeInput(NativeMethods.VK_CONTROL, true),
                MakeInput(NativeMethods.VK_V, true),
                MakeInput(NativeMethods.VK_V, false),
                MakeInput(NativeMethods.VK_CONTROL, false),
            },
        };

        return NativeMethods.SendInput((uint)inputs.Length, inputs, INPUT.Size) == inputs.Length;
    }

    private static INPUT MakeInput(int vk, bool down)
    {
        uint flags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP;
        if (vk is NativeMethods.VK_INSERT or NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };
    }
}
