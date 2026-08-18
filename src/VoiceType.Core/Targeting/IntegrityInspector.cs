using System.Runtime.InteropServices;
using VoiceType.Core.Native;

namespace VoiceType.Core.Targeting;

internal static class IntegrityInspector
{
    private const int MediumRid = 0x2000;

    /// <summary>
    /// True when the target process runs at a higher integrity level than this
    /// process, or when its level cannot be determined (safe default: refuse
    /// injection and fall back to copy recovery).
    /// </summary>
    public static bool IsHigherIntegrityThanSelf(int targetPid)
    {
        int selfLevel = GetIntegrityLevel(NativeMethods.GetCurrentProcess()) ?? MediumRid;

        nint hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, targetPid);
        if (hProcess == 0)
            return true; // cannot even open the process — treat as elevated

        try
        {
            int? targetLevel = GetIntegrityLevel(hProcess);
            return targetLevel is null || targetLevel.Value > selfLevel;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static int? GetIntegrityLevel(nint hProcess)
    {
        if (!NativeMethods.OpenProcessToken(hProcess, NativeMethods.TOKEN_QUERY, out nint hToken))
            return null;

        try
        {
            NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenIntegrityLevel, 0, 0, out uint size);
            if (size == 0) return null;

            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenIntegrityLevel, buffer, size, out _))
                    return null;

                // TOKEN_MANDATORY_LABEL starts with SID_AND_ATTRIBUTES
                var label = Marshal.PtrToStructure<SID_AND_ATTRIBUTES>(buffer);
                if (label.Sid == 0) return null;

                nint countPtr = NativeMethods.GetSidSubAuthorityCount(label.Sid);
                if (countPtr == 0) return null;
                byte count = Marshal.ReadByte(countPtr);
                if (count == 0) return null;

                nint ridPtr = NativeMethods.GetSidSubAuthority(label.Sid, (uint)(count - 1));
                if (ridPtr == 0) return null;
                return Marshal.ReadInt32(ridPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hToken);
        }
    }
}
