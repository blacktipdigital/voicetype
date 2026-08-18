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
            return Encoding.UTF8.GetString(plaintext);
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
        byte[] ciphertext = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, ciphertext);
        _log.Info("API key stored (DPAPI CurrentUser).");
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
        _log.Info("API key removed.");
    }
}
