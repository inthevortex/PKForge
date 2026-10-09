using PKForge.Domain;

namespace PKForge.Infrastructure;

public sealed record ScanCacheEntry(long ModifiedTicks, DetectedSave? Save);

public interface IScanCacheStore
{
    Dictionary<string, ScanCacheEntry> Load();
    void Save(IReadOnlyDictionary<string, ScanCacheEntry> entries);
}

public sealed class MemoryScanCacheStore : IScanCacheStore
{
    private readonly Dictionary<string, ScanCacheEntry> _entries = new();
    public Dictionary<string, ScanCacheEntry> Load() => new(_entries);
    public void Save(IReadOnlyDictionary<string, ScanCacheEntry> entries)
    {
        _entries.Clear();
        foreach (var (k, v) in entries)
            _entries[k] = v;
    }
}
