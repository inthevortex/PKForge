using PKForge.Domain;
using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class JsonFileScanCacheStoreTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "pkforge-scancache-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public JsonFileScanCacheStoreTests()
    {
        Directory.CreateDirectory(_temp);
        _file = Path.Combine(_temp, "cache", "scan-cache.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            try
            {
                Directory.Delete(_temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string Key(string file, long epoch) => $"/saves/{file}#{EmulatorKind.RetroArch}#{epoch}";

    private static ScanCacheEntry Entry(long ticks, string? label = null) =>
        new(ticks, label is null ? null : new DetectedSave("/saves/" + label, label, label, EmulatorKind.RetroArch, false, null));

    [Fact]
    public void Save_MergesWithEntriesAnotherScannerWrote()
    {
        new JsonFileScanCacheStore(_file, buildEpoch: 7).Save(new Dictionary<string, ScanCacheEntry> { [Key("a.srm", 7)] = Entry(1, "a") });
        new JsonFileScanCacheStore(_file, buildEpoch: 7).Save(new Dictionary<string, ScanCacheEntry> { [Key("b.srm", 7)] = Entry(2) });

        var loaded = new JsonFileScanCacheStore(_file, buildEpoch: 7).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("a", loaded[Key("a.srm", 7)].Save!.GameLabel);
        Assert.Null(loaded[Key("b.srm", 7)].Save);
    }

    [Fact]
    public void SaveAndLoad_DropEntriesFromOtherBuilds()
    {
        new JsonFileScanCacheStore(_file, buildEpoch: 100).Save(new Dictionary<string, ScanCacheEntry>
        {
            [Key("old.srm", 100)] = Entry(1),
            [Key("older.srm", 2100)] = Entry(1),
        });

        var current = new JsonFileScanCacheStore(_file, buildEpoch: 200);
        Assert.Empty(current.Load());

        current.Save(new Dictionary<string, ScanCacheEntry>
        {
            [Key("new.srm", 200)] = Entry(3),
            [Key("stale.srm", 100)] = Entry(3),
        });

        var onDisk = new JsonFileScanCacheStore(_file).Load();
        Assert.Equal([Key("new.srm", 200)], onDisk.Keys);
    }

    [Fact]
    public void Save_TrimsToTheCapAfterMergingAndKeepsTheCallersEntries()
    {
        var store = new JsonFileScanCacheStore(_file, buildEpoch: 1);
        store.Save(Enumerable.Range(0, JsonFileScanCacheStore.MaxEntries)
            .ToDictionary(i => Key($"disk{i}.srm", 1), i => Entry(i + 1)));

        var fresh = Enumerable.Range(0, 20).ToDictionary(i => Key($"fresh{i}.srm", 1), i => Entry(i + 1));
        store.Save(fresh);

        var loaded = store.Load();
        Assert.Equal(JsonFileScanCacheStore.MaxEntries, loaded.Count);
        Assert.All(fresh.Keys, key => Assert.Contains(key, loaded.Keys));
        Assert.DoesNotContain(Key("disk0.srm", 1), loaded.Keys);
        Assert.Contains(Key($"disk{JsonFileScanCacheStore.MaxEntries - 1}.srm", 1), loaded.Keys);
    }

    [Fact]
    public void Save_TrimsAnOversizedInputToTheNewestEntries()
    {
        var store = new JsonFileScanCacheStore(_file);
        store.Save(Enumerable.Range(0, JsonFileScanCacheStore.MaxEntries + 10)
            .ToDictionary(i => Key($"s{i}.srm", 1), i => Entry(i + 1)));

        var loaded = store.Load();
        Assert.Equal(JsonFileScanCacheStore.MaxEntries, loaded.Count);
        Assert.DoesNotContain(Key("s9.srm", 1), loaded.Keys);
        Assert.Contains(Key("s10.srm", 1), loaded.Keys);
    }

    [Fact]
    public void CorruptFile_LoadsEmptyAndIsReplacedOnSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ not json");
        var store = new JsonFileScanCacheStore(_file, buildEpoch: 5);

        Assert.Empty(store.Load());

        store.Save(new Dictionary<string, ScanCacheEntry> { [Key("a.srm", 5)] = Entry(9) });

        var loaded = new JsonFileScanCacheStore(_file, buildEpoch: 5).Load();
        Assert.Equal(9, Assert.Single(loaded).Value.ModifiedTicks);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!, "*.tmp"));
    }
}
