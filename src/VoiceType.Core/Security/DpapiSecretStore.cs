using System.IO;
using System.Security.Cryptography;
using System.Text;
using VoiceType.Core.Logging;

namespace VoiceType.Core.Security;

/// <summary>
/// DPAPI CurrentUser ciphertext at %LOCALAPPDATA%\VoiceType\openai.key.bin.
/// Only this Windows user on this machine can decrypt it.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly ILog _log;

    public DpapiSecretStore(ILog log)
        : this(log, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceType", "openai.key.bin"))
    {
    }

    public DpapiSecretStore(ILog log, string path)
    {
        _log = log;
        _path = path;
    }

    public bool HasApiKey => File.Exists(_path);

    public string? GetApiKey()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            byte[] ciphertext = File.ReadAllBytes(_path);
            byte[] plaintext = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                // The returned string is still an unpinned managed copy — .NET
                // gives no way around that — but the byte buffer does not have
                // to survive into a crash dump or the page file as well.
                Array.Clear(plaintext);
            }
        }
        catch (Exception ex)
        {
            _log.Error("API key decrypt failed.", ex);
            return null;
        }
    }

    public void SetApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plaintext = Encoding.UTF8.GetBytes(apiKey);
        try
        {
            File.WriteAllBytes(_path, ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            Array.Clear(plaintext);
        }

        _log.Info("API key stored (DPAPI CurrentUser).");
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
        _log.Info("API key removed.");
    }
}
