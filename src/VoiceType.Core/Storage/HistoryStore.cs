using System.IO;
using System.Text;
using System.Text.Json;
using VoiceType.Core.Logging;
using VoiceType.Core.Security;
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
/// transcripts, never audio, and encrypts them at rest with DPAPI so another
/// process running as the same user cannot read them off disk. A file that
/// cannot be read or decrypted is renamed aside and the store restarts empty
/// instead of failing. Plaintext files written by earlier versions are read
/// once and re-encrypted on the next write.
/// </summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    public const int RetentionDays = 7;
    public const int MaxEntries = 1_000;

    private readonly object _sync = new();
    private readonly string _path;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly IDataProtector _protector;

    public JsonHistoryStore(IClock clock, ILog log)
        : this(clock, log, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceType", "history.json"))
    {
    }

    public JsonHistoryStore(IClock clock, ILog log, string path)
        : this(clock, log, path, new DpapiDataProtector())
    {
    }

    public JsonHistoryStore(IClock clock, ILog log, string path, IDataProtector protector)
    {
        _clock = clock;
        _log = log;
        _path = path;
        _protector = protector;
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
        byte[] plaintext = Array.Empty<byte>();
        try
        {
            if (!File.Exists(_path)) return new List<HistoryEntry>();

            byte[] stored = File.ReadAllBytes(_path);
            if (stored.Length == 0) return new List<HistoryEntry>();

            // Pre-encryption files start with a bare JSON array. Read them as
            // they are; the next write re-encrypts them.
            plaintext = LooksLikePlaintextJson(stored) ? stored : _protector.Unprotect(stored);

            return JsonSerializer.Deserialize<List<HistoryEntry>>(plaintext) ?? new List<HistoryEntry>();
        }
        catch (Exception ex)
        {
            _log.Error("History file unreadable; starting fresh.", ex);
            try { File.Move(_path, _path + ".corrupt", overwrite: true); } catch { /* best effort */ }
            return new List<HistoryEntry>();
        }
        finally
        {
            // Never leave decrypted transcripts sitting in a reachable buffer.
            if (plaintext.Length > 0) Array.Clear(plaintext);
        }
    }

    /// <summary>True for a legacy unencrypted file: a JSON array after any BOM and leading whitespace.</summary>
    private static bool LooksLikePlaintextJson(byte[] stored)
    {
        int i = 0;
        if (stored.Length >= 3 && stored[0] == 0xEF && stored[1] == 0xBB && stored[2] == 0xBF) i = 3;
        while (i < stored.Length && stored[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return i < stored.Length && stored[i] == (byte)'[';
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

        byte[] plaintext = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(kept, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            File.WriteAllBytes(tmp, _protector.Protect(plaintext));
        }
        finally
        {
            Array.Clear(plaintext);
        }

        File.Move(tmp, _path, overwrite: true);
    }
}
