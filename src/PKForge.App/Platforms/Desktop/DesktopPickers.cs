using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopDocumentPicker : IDocumentPicker
{
    private static IStorageProvider? StorageProvider =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;

    public async ValueTask<PickedDocument?> PickSaveAsync(CancellationToken cancellationToken = default)
    {
        var provider = StorageProvider;
        if (provider is null) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Save File",
            AllowMultiple = false
        });

        if (files.Count == 0) return null;
        var localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath)) return null;

        return new PickedDocument(Path.GetFullPath(localPath), files[0].Name);
    }

    public async ValueTask<IReadOnlyList<PickedDocument>> PickManyAsync(CancellationToken cancellationToken = default)
    {
        var provider = StorageProvider;
        if (provider is null) return [];

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Files",
            AllowMultiple = true
        });

        var results = new List<PickedDocument>();
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                results.Add(new PickedDocument(Path.GetFullPath(localPath), file.Name));
            }
        }
        return results;
    }
}

public sealed class DesktopFolderPicker : IFolderPicker
{
    private static IStorageProvider? StorageProvider =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;

    public async ValueTask<PickedFolder?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var provider = StorageProvider;
        if (provider is null) return null;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return null;
        var localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath)) return null;

        var fullPath = Path.GetFullPath(localPath);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var display = fullPath.StartsWith(home, StringComparison.Ordinal)
            ? "~" + fullPath[home.Length..]
            : fullPath;

        return new PickedFolder(fullPath, display);
    }
}
