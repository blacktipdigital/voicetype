using System.IO;
using VoiceType.Core.Storage;

namespace VoiceType.Core.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VoiceTypeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new() { UtcNow = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero) };
    private readonly JsonHistoryStore _store;
    private string StorePath => Path.Combine(_dir, "history.json");

    public HistoryStoreTests() => _store = new JsonHistoryStore(_clock, new NullLog(), StorePath);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private HistoryEntry Entry(string text, double ageDays = 0) =>
        new(Guid.NewGuid(), _clock.UtcNow.AddDays(-ageDays), text, "Inserted", 1500);

    [Fact]
    public void AddAndGet_NewestFirst()
    {
        _store.Add(Entry("older", ageDays: 1));
        _store.Add(Entry("newer"));

        var all = _store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("newer", all[0].Text);
        Assert.Equal("older", all[1].Text);
    }

    [Fact]
    public void Delete_RemovesOnlyThatEntry()
    {
        var keep = Entry("keep");
        var drop = Entry("drop");
        _store.Add(keep);
        _store.Add(drop);

        _store.Delete(drop.Id);

        Assert.Equal("keep", Assert.Single(_store.GetAll()).Text);
    }

    [Fact]
    public void Purge_RemovesEntriesOlderThanSevenDays()
    {
        _store.Add(Entry("fresh", ageDays: 6.9));
        _store.Add(Entry("expired", ageDays: 7.1));

        _store.Purge();

        Assert.Equal("fresh", Assert.Single(_store.GetAll()).Text);
    }

    [Fact]
    public void WriteAppliesRetention_ExpiredNeverSurvivesAnAdd()
    {
        _store.Add(Entry("expired", ageDays: 8));
        _store.Add(Entry("current"));

        Assert.Equal("current", Assert.Single(_store.GetAll()).Text);
    }

    [Fact]
    public void Cap_KeepsNewestThousand()
    {
        var entries = Enumerable.Range(0, 1_005)
            .Select(i => new HistoryEntry(Guid.NewGuid(), _clock.UtcNow.AddMinutes(-i), $"t{i}", "Inserted", null))
            .ToList();
        foreach (var e in entries.Take(1_004)) _store.Add(e);
        _store.Add(entries[1_004]);

        var all = _store.GetAll();
        Assert.Equal(JsonHistoryStore.MaxEntries, all.Count);
        Assert.Equal("t0", all[0].Text); // newest kept
    }

    [Fact]
    public void AtomicRewrite_LeavesNoTempFile()
    {
        _store.Add(Entry("x"));
        Assert.True(File.Exists(StorePath));
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void DeletedText_LeavesTheFileOnDisk()
    {
        var secret = Entry("do not keep this text");
        _store.Add(secret);
        _store.Add(Entry("other"));

        _store.Delete(secret.Id);

        Assert.DoesNotContain("do not keep this text", File.ReadAllText(StorePath));
    }

    [Fact]
    public void CorruptFile_RecoversEmptyAndPreservesEvidence()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StorePath, "{not valid json!!");

        var all = _store.GetAll();

        Assert.Empty(all);
        Assert.True(File.Exists(StorePath + ".corrupt"));
    }
}
