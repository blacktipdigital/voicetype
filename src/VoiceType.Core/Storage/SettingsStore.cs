using System.IO;
using System.Text.Json;
using VoiceType.Core.Logging;

namespace VoiceType.Core.Storage;

public sealed record VoiceTypeSettings
{
    /// <summary>MMDevice ID of the chosen microphone; null = system default.</summary>
    public string? MicrophoneDeviceId { get; init; }

    /// <summary>Custom dictionary: exact spellings the cleanup pass must use.</summary>
    public string[] DictionaryTerms { get; init; } = Array.Empty<string>();
}

public interface ISettingsStore
{
    VoiceTypeSettings Load();
    void Save(VoiceTypeSettings settings);
}

/// <summary>JSON settings at %LOCALAPPDATA%\VoiceType\settings.json. Never holds secrets.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ILog _log;

    public JsonSettingsStore(ILog log)
        : this(log, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceType", "settings.json"))
    {
    }

    public JsonSettingsStore(ILog log, string path)
    {
        _log = log;
        _path = path;
    }

    public VoiceTypeSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new VoiceTypeSettings();
            return JsonSerializer.Deserialize<VoiceTypeSettings>(File.ReadAllText(_path)) ?? new VoiceTypeSettings();
        }
        catch (Exception ex)
        {
            _log.Error("Settings load failed; using defaults.", ex);
            return new VoiceTypeSettings();
        }
    }

    public void Save(VoiceTypeSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
