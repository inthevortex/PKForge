namespace PKForge.App.Services;

public interface IAppPlatform
{
    Task ExportFilesAsync(string title, IReadOnlyList<string> paths);
}
