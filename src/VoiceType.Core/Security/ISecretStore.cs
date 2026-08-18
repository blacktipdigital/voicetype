namespace VoiceType.Core.Security;

/// <summary>
/// API-key storage seam. The key must never appear in JSON, source, logs,
/// or any packaged artifact — ciphertext on disk only.
/// </summary>
public interface ISecretStore
{
    bool HasApiKey { get; }
    string? GetApiKey();
    void SetApiKey(string apiKey);
    void Clear();
}
