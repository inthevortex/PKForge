using System.Text.Json;
using System.Text.Json.Serialization;

namespace PKForge.Infrastructure;

/// <summary>
/// Persistent JSON file scan cache store for desktop.
/// Reads and atomically writes scan cache entries to disk.
/// </summary>
/// <param name="filePath">The cache file; defaults to <see cref="DefaultCachePath"/>.</param>
/// <param name="buildEpoch">
/// The epoch the scanner puts at the end of each key. When set, entries from other builds are
/// dropped on load and save: they can never be hit again.
/// </param>
public sealed class JsonFileScanCacheStore(string? filePath = null, long? buildEpoch = null) : IScanCacheStore
{
    /// <summary>The most entries kept on disk, matching the scanner's in-memory cap.</summary>
    public const int MaxEntries = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly Lock Gate = new();
    private readonly string _filePath = filePath ?? DefaultCachePath();
    private readonly string? _epochSuffix = buildEpoch is { } epoch ? "#" + epoch : null;

    public static string DefaultCachePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PKForge", "scan-cache.json");
    }

    public Dictionary<string, ScanCacheEntry> Load()
    {
        lock (Gate)
        {
            var result = new Dictionary<string, ScanCacheEntry>(StringComparer.Ordinal);
            foreach (var (key, entry) in ReadDisk())
            {
                if (IsCurrentEpoch(key))
                    result[key] = entry;
            }
            return result;
        }
    }

    public void Save(IReadOnlyDictionary<string, ScanCacheEntry> entries)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Merge with the entries already on disk so another scanner's keys survive; the
                // caller's entries go last so the cap trims the older, disk-only ones first.
                var toWrite = new Dictionary<string, ScanCacheEntry>(StringComparer.Ordinal);
                foreach (var (key, entry) in ReadDisk())
                {
                    if (IsCurrentEpoch(key) && !entries.ContainsKey(key))
                        toWrite[key] = entry;
                }
                foreach (var (key, entry) in entries)
                {
                    if (IsCurrentEpoch(key))
                        toWrite[key] = entry;
                }
                foreach (var key in toWrite.Keys.Take(Math.Max(0, toWrite.Count - MaxEntries)).ToArray())
                    toWrite.Remove(key);

                var fileName = Path.GetFileName(_filePath);
                var tempPath = Path.Combine(dir ?? ".", $".{fileName}.{Guid.NewGuid():N}.tmp");

                var json = JsonSerializer.Serialize(toWrite, JsonOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch
            {
            }
        }
    }

    private bool IsCurrentEpoch(string key) =>
        _epochSuffix is null || key.EndsWith(_epochSuffix, StringComparison.Ordinal);

    /// <summary>The entries on disk, or none when the file is missing, unreadable or corrupt.</summary>
    private Dictionary<string, ScanCacheEntry> ReadDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ScanCacheEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
