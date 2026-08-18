using System.IO;
using System.Text.Json;
using VoiceType.Core.Logging;
using VoiceType.Core.Time;

namespace VoiceType.Core.Storage;

public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Text,
    string DeliveryStatus,
    long? LatencyMs);

public interface IHistoryStore
{
    /// <summary>Adds an entry; purge and cap run on every write.</summary>
    void Add(HistoryEntry entry);

    /// <summary>Newest first.</summary>
    IReadOnlyList<HistoryEntry> GetAll();

    void Delete(Guid id);

    /// <summary>Removes entries older than 7 days, caps at 1,000, rewrites the file atomically so deleted text releases disk space.</summary>
    void Purge();
}

/// <summary>
/// Text-only history at %LOCALAPPDATA%\VoiceType\history.json. Stores final
/// transcripts, never audio. A corrupted file is renamed aside and the store
/// restarts empty instead of failing.
/// </summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    public const int RetentionDays = 7;
    public const int MaxEntries = 1_000;

    private readonly object _sync = new();
    private readonly string _path;
    private readonly IClock _clock;
    private readonly ILog _log;

    public JsonHistoryStore(IClock clock, ILog log)
        : this(clock, log, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceType", "history.json"))
    {
    }

    public JsonHistoryStore(IClock clock, ILog log, string path)
    {
        _clock = clock;
        _log = log;
        _path = path;
    }

    public void Add(HistoryEntry entry)
    {
        lock (_sync)
        {
            var entries = LoadUnsafe();
            entries.Add(entry);
            WritePurgedUnsafe(entries);
        }
    }

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        lock (_sync)
        {
            return LoadUnsafe()
                .OrderByDescending(e => e.TimestampUtc)
                .ToList();
        }
    }

    public void Delete(Guid id)
    {
        lock (_sync)
        {
            var entries = LoadUnsafe();
            entries.RemoveAll(e => e.Id == id);
            WritePurgedUnsafe(entries);
        }
    }

    public void Purge()
    {
        lock (_sync)
        {
            WritePurgedUnsafe(LoadUnsafe());
        }
    }

    private List<HistoryEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_path)) return new List<HistoryEntry>();
            return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path)) ?? new List<HistoryEntry>();
        }
        catch (Exception ex)
        {
            _log.Error("History file unreadable; starting fresh.", ex);
            try { File.Move(_path, _path + ".corrupt", overwrite: true); } catch { /* best effort */ }
            return new List<HistoryEntry>();
        }
    }

    private void WritePurgedUnsafe(List<HistoryEntry> entries)
    {
        DateTimeOffset cutoff = _clock.UtcNow.AddDays(-RetentionDays);
        var kept = entries
            .Where(e => e.TimestampUtc >= cutoff)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(MaxEntries)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(kept, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
