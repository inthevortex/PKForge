using PKForge.Infrastructure;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// Persists emulator scan results into Android's <see cref="Microsoft.Maui.Storage.Preferences"/>
/// using the historical "scan_cache_v5" key so existing Android installs retain their cache.
/// </summary>
public sealed class PreferencesScanCacheStore : IScanCacheStore
{
    // Detection support can expand between releases. Keep cached negative
    // results versioned so a newly supported save is always retried once.
    private const string Key = "scan_cache_v5"; // v5: cards re-read after the out-of-range party count fix

    public Dictionary<string, ScanCacheEntry> Load()
    {
        try
        {
            var raw = Microsoft.Maui.Storage.Preferences.Default.Get(Key, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScanCacheEntry>>(raw) ?? [];
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
            Microsoft.Maui.Storage.Preferences.Default.Set(Key, System.Text.Json.JsonSerializer.Serialize(entries));
        }
        catch
        {
        }
    }
}
