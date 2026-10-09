using System.Text.Json;
using System.Text.Json.Serialization;

namespace PKForge.Infrastructure;

/// <summary>
/// Persistent JSON file scan cache store for desktop.
/// Reads and atomically writes scan cache entries to disk.
/// </summary>
public sealed class JsonFileScanCacheStore : IScanCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _filePath;

    public JsonFileScanCacheStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultCachePath();
    }

    public static string DefaultCachePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PKForge", "scan-cache.json");
    }

    public Dictionary<string, ScanCacheEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            var result = JsonSerializer.Deserialize<Dictionary<string, ScanCacheEntry>>(json, JsonOptions);
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyDictionary<string, ScanCacheEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var fileName = Path.GetFileName(_filePath);
            var tempPath = Path.Combine(dir ?? ".", $".{fileName}.{Guid.NewGuid():N}.tmp");

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            // Best effort persistent caching
        }
    }
}
