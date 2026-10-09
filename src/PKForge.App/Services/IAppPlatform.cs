namespace PKForge.App.Services;

public enum HapticKind
{
    Click,
    LongPress
}

public interface IAppPlatform
{
    string DeviceSummary();
    void LogNative(string level, string line);
    bool CanSelfInstallUpdates { get; }
    Task ExportFilesAsync(string title, IReadOnlyList<string> paths);
    void Haptic(HapticKind kind);
}
