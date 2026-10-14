namespace PKForge.App.Services;

public enum HapticKind
{
    Click,
    LongPress
}

public interface IAppPlatform
{
    /// <summary>True where PKForge downloads and installs its own updates; elsewhere an update opens its release page.</summary>
    bool CanSelfInstallUpdates { get; }
    Task ExportFilesAsync(string title, IReadOnlyList<string> paths);
    void Haptic(HapticKind kind);
}
