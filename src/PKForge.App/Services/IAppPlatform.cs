namespace PKForge.App.Services;

public enum HapticKind
{
    Click,
    LongPress
}

public interface IAppPlatform
{
    Task ExportFilesAsync(string title, IReadOnlyList<string> paths);
    void Haptic(HapticKind kind);
}
