using System.Security.Cryptography;

namespace VoiceType.Core.Security;

/// <summary>
/// At-rest protection seam for local files that hold dictated content.
/// Kept as an interface so Core stays testable without touching the real
/// Windows DPAPI store.
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plaintext);

    /// <summary>Throws when the payload was not produced by this protector for this user.</summary>
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>DPAPI CurrentUser: only this Windows account on this machine can read the payload.</summary>
public sealed class DpapiDataProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
}

/// <summary>Pass-through for tests and for verifying migration behaviour. Never wire this into the app.</summary>
public sealed class NullDataProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;

    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
