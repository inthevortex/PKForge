using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopDocumentPicker : IDocumentPicker
{
    internal static IStorageProvider? StorageProvider
    {
        get
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow?.StorageProvider is { } sp) return sp;
                foreach (var window in desktop.Windows)
                {
                    if (window?.StorageProvider is { } wsp) return wsp;
                }
            }
            return null;
        }
    }

    public async ValueTask<PickedDocument?> PickSaveAsync(CancellationToken cancellationToken = default)
    {
        using var guard = DialogConcurrencyGuard.Enter();
        var provider = StorageProvider;
        if (provider is null) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Save File",
            AllowMultiple = false
        });

        if (files.Count == 0) return null;
        var localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
            throw new InvalidOperationException($"The selected file '{files[0].Name}' does not have an accessible local path.");

        return new PickedDocument(Path.GetFullPath(localPath), files[0].Name);
    }

    public async ValueTask<IReadOnlyList<PickedDocument>> PickManyAsync(CancellationToken cancellationToken = default)
    {
        using var guard = DialogConcurrencyGuard.Enter();
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
            if (string.IsNullOrEmpty(localPath))
                throw new InvalidOperationException($"The selected file '{file.Name}' does not have an accessible local path.");

            results.Add(new PickedDocument(Path.GetFullPath(localPath), file.Name));
        }
        return results;
    }
}

public sealed class DesktopFolderPicker : IFolderPicker
{
    private static IStorageProvider? StorageProvider => DesktopDocumentPicker.StorageProvider;

    public async ValueTask<PickedFolder?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        using var guard = DialogConcurrencyGuard.Enter();
        var provider = StorageProvider;
        if (provider is null) return null;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return null;
        var localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
            throw new InvalidOperationException($"The selected folder '{folders[0].Name}' does not have an accessible local path.");

        var fullPath = Path.GetFullPath(localPath);
        var display = DesktopExportFiles.ForDisplay(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return new PickedFolder(fullPath, display);
    }
}
