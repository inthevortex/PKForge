using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PKForge.App.Services;
using PKForge.Infrastructure;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopAppPlatform : IAppPlatform
{
    private static IStorageProvider? StorageProvider
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

    public string DeviceSummary() => $"Linux Desktop, {Environment.OSVersion}";

    public void LogNative(string level, string line) => Console.Error.WriteLine($"[{level}] {line}");

    public bool CanSelfInstallUpdates => false;

    public void Haptic(HapticKind kind)
    {
        // Handled via SDL rumble in Step 7
    }

    public static string GetExportDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            docs = Path.Combine(home, "Documents");
        }
        return Path.Combine(docs, "PKForge", "exports");
    }

    public async Task ExportFilesAsync(string title, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        var exportDir = GetExportDirectory();
        Directory.CreateDirectory(exportDir);

        var provider = StorageProvider;
        if (paths.Count == 1)
        {
            var sourcePath = paths[0];
            var fileName = Path.GetFileName(sourcePath);
            var defaultDest = Path.Combine(exportDir, fileName);
            File.Copy(sourcePath, defaultDest, overwrite: true);

            if (provider != null)
            {
                try
                {
                    using var guard = DialogConcurrencyGuard.Enter();
                    var ext = Path.GetExtension(sourcePath);
                    var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? "Save File" : title,
                        DefaultExtension = ext.TrimStart('.'),
                        SuggestedFileName = fileName
                    });

                    if (file != null)
                    {
                        var localPath = file.TryGetLocalPath();
                        if (!string.IsNullOrEmpty(localPath) && !string.Equals(Path.GetFullPath(localPath), Path.GetFullPath(defaultDest), StringComparison.Ordinal))
                        {
                            File.Copy(sourcePath, localPath, overwrite: true);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Fallback to default exportDir copy if dialog concurrency guard triggers
                }
            }
        }
        else
        {
            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path);
                var dest = Path.Combine(exportDir, fileName);
                File.Copy(path, dest, overwrite: true);
            }

            if (provider != null)
            {
                try
                {
                    using var guard = DialogConcurrencyGuard.Enter();
                    var folder = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? "Select Export Folder" : title,
                        AllowMultiple = false
                    });

                    if (folder.Count > 0)
                    {
                        var localPath = folder[0].TryGetLocalPath();
                        if (!string.IsNullOrEmpty(localPath) && Directory.Exists(localPath) && !string.Equals(Path.GetFullPath(localPath), Path.GetFullPath(exportDir), StringComparison.Ordinal))
                        {
                            foreach (var path in paths)
                            {
                                var fileName = Path.GetFileName(path);
                                var dest = Path.Combine(localPath, fileName);
                                File.Copy(path, dest, overwrite: true);
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Fallback to exportDir copy
                }
            }
        }
    }
}
