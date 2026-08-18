using System.Text;
using System.Windows.Automation;
using VoiceType.Core.Logging;
using VoiceType.Core.Native;

namespace VoiceType.Core.Targeting;

public sealed class TargetService : ITargetService
{
    private static readonly HashSet<string> EditorProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Cursor", "windsurf", "notepad", "notepad++", "devenv", "sublime_text",
    };

    private readonly ILog _log;

    public TargetService(ILog log) => _log = log;

    public TargetSnapshot? CaptureForeground()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        string processName = GetProcessName((int)pid);
        string className = GetWindowClassName(hwnd);
        bool higherIntegrity = IntegrityInspector.IsHigherIntegrityThanSelf((int)pid);
        var probe = ProbeFocusedElement();
        var kind = Classify(processName, className, probe.IsEditable);

        return new TargetSnapshot(
            hwnd, (int)pid, processName, kind, higherIntegrity,
            probe.IsPassword, probe.IsEditable, probe.RuntimeId);
    }

    public bool IsTargetStillValid(TargetSnapshot snapshot)
    {
        if (NativeMethods.GetForegroundWindow() != snapshot.Hwnd) return false;

        if (snapshot.FocusedRuntimeId is null)
            return snapshot.IsTrustedProcess;

        try
        {
            var current = AutomationElement.FocusedElement;
            int[]? currentId = current?.GetRuntimeId();
            return currentId is not null && currentId.SequenceEqual(snapshot.FocusedRuntimeId);
        }
        catch (Exception ex)
        {
            _log.Warn($"UIA focus re-check failed: {ex.GetType().Name}; refusing insertion.");
            return false;
        }
    }

    public bool IsForegroundElevated()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        return IntegrityInspector.IsHigherIntegrityThanSelf((int)pid);
    }

    private readonly record struct FocusProbe(bool IsPassword, bool IsEditable, int[]? RuntimeId);

    private FocusProbe ProbeFocusedElement()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return new FocusProbe(false, false, null);

            bool isPassword = element.Current.IsPassword;
            bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out object valueObj);
            bool valueReadOnly = hasValue && valueObj is ValuePattern value && value.Current.IsReadOnly;
            bool isEdit = element.Current.ControlType == ControlType.Edit;
            bool isEditable = EditableHeuristic.IsEditable(hasValue, valueReadOnly, isEdit);
            int[]? runtimeId = element.GetRuntimeId();

            return new FocusProbe(isPassword, isEditable, runtimeId);
        }
        catch (Exception ex)
        {
            // UIA probe failure means "unknown control": not provably safe,
            // so report not-editable with no identity. The coordinator then
            // refuses everything except trusted processes.
            _log.Warn($"UIA focus probe failed: {ex.GetType().Name}.");
            return new FocusProbe(false, false, null);
        }
    }

    private static TargetKind Classify(string processName, string className, bool isEditable)
    {
        if (processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
            return TargetKind.WindowsTerminal;
        if (className == "ConsoleWindowClass")
            return TargetKind.ClassicConsole;
        if (EditorProcesses.Contains(processName))
            return TargetKind.Editor;
        if (isEditable)
            return TargetKind.Standard;
        return TargetKind.Unknown;
    }

    private string GetProcessName(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex)
        {
            _log.Warn($"Process name lookup failed for pid {pid}: {ex.GetType().Name}.");
            return string.Empty;
        }
    }

    private static string GetWindowClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        int len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }
}
