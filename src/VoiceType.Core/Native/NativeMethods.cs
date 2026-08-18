using System.Runtime.InteropServices;
using System.Text;

namespace VoiceType.Core.Native;

internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public int ptX;
    public int ptY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;

    public static int Size => Marshal.SizeOf<INPUT>();
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SID_AND_ATTRIBUTES
{
    public nint Sid;
    public uint Attributes;
}

internal static class NativeMethods
{
    // Hook / message loop
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const uint WM_QUIT = 0x0012;
    internal const uint LLKHF_INJECTED = 0x10;

    // Virtual keys
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_INSERT = 0x2D;
    internal const int VK_V = 0x56;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
    internal const int VK_LSHIFT = 0xA0;
    internal const int VK_RSHIFT = 0xA1;
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;
    internal const int VK_LMENU = 0xA4;
    internal const int VK_RMENU = 0xA5;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_SHIFT = 0x10;

    // SendInput
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Process / token
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenIntegrityLevel = 25;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    // Foreground window / target
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    // SendInput / key state
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // Clipboard (writes/reads go through the OLE lease; only the change counter is raw)
    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    // Process / integrity
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, nint tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll")]
    internal static extern nint GetSidSubAuthority(nint pSid, uint nSubAuthority);

    [DllImport("advapi32.dll")]
    internal static extern nint GetSidSubAuthorityCount(nint pSid);
}
