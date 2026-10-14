using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PKForge.App.Services;
using PKForge.App.Views;
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

    public bool CanSelfInstallUpdates => false;

    /// <summary>Where haptics go once the gamepad source runs; it decides whether a pad rumbles.</summary>
    internal Action<HapticKind>? HapticSink { get; set; }

    /// <summary>Called on the UI thread.</summary>
    public void Haptic(HapticKind kind) => HapticSink?.Invoke(kind);

    /// <summary>
    /// Exports into ~/Documents/PKForge/exports after a pad-driven prompt that names the
    /// destination; the system Save dialog opens only when the user asks for it. Nothing is
    /// written when the user cancels, and an earlier export is never replaced.
    /// </summary>
    public Task ExportFilesAsync(string title, IReadOnlyList<string> paths)
    {
        var host = CurrentHostGrid();
        Func<ExportPromptContent, Task<ExportChoice>>? ask = host is null ? null : prompt => AskAsync(host, prompt);
        return DesktopExportPrompt.RunAsync(title, paths, DesktopExportFiles.DefaultDirectory(), ask, ExportActions.Instance);
    }

    private static async Task<ExportChoice> AskAsync(Grid host, ExportPromptContent prompt)
    {
        var options = prompt.Choices.Select(choice => choice switch
        {
            ExportChoice.Export => new PadOption("Export", IconPath: "export"),
            ExportChoice.ExportAndOpen => new PadOption("Export and open folder", IconPath: "folder"),
            ExportChoice.SaveAs => new PadOption("Save as…", IconPath: "file", Detail: "Pick another place in the system file dialog."),
            _ => new PadOption("Cancel", IconPath: "close"),
        }).ToArray();
        var picked = await PadMenu.ShowAsync(host, prompt.Heading, prompt.Destination, options);
        var index = Array.FindIndex(options, option => option.Label == picked);
        return index < 0 ? ExportChoice.Cancel : prompt.Choices[index];
    }

    private sealed class ExportActions : IDesktopExportActions
    {
        public static readonly ExportActions Instance = new();

        public bool CanSaveAs => StorageProvider is not null;

        string IDesktopExportActions.ForDisplay(string path) => DesktopAppPlatform.ForDisplay(path);

        public void ExportToFolder(IReadOnlyList<string> paths, string directory) => DesktopAppPlatform.ExportToFolder(paths, directory);

        public Task OpenFolderAsync(string directory) => DesktopAppPlatform.OpenFolderAsync(directory);

        public Task<bool> SaveAsAsync(string heading, IReadOnlyList<string> paths) => DesktopAppPlatform.SaveAsAsync(heading, paths);
    }

    /// <summary>The root grid of the page in front, where in-app prompts are shown.</summary>
    private static Grid? CurrentHostGrid()
    {
        var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;
        page = page.Navigation.ModalStack.LastOrDefault() ?? page;
        if (page is NavigationPage navigation) page = navigation.CurrentPage;
        return (page as ContentPage)?.Content as Grid;
    }

    private static string ForDisplay(string path) =>
        DesktopExportFiles.ForDisplay(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static void ExportToFolder(IReadOnlyList<string> paths, string directory)
    {
        foreach (var written in DesktopExportFiles.CopyAllUnique(paths, directory))
            AppLog.Info("export", $"Exported {ForDisplay(written)}");
    }

    private static async Task OpenFolderAsync(string directory)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(new Uri(directory)))
                AppLog.Warn("export", "The export folder could not be opened");
        }
        catch (UriFormatException error)
        {
            AppLog.Warn("export", $"The export folder could not be opened: {error.Message}");
        }
    }

    /// <summary>Runs the system Save (one file) or folder (several files) dialog; false when it was cancelled or could not open.</summary>
    private static async Task<bool> SaveAsAsync(string title, IReadOnlyList<string> paths)
    {
        if (StorageProvider is not { } provider) return false;

        IDisposable guard;
        try
        {
            guard = DialogConcurrencyGuard.Enter();
        }
        catch (InvalidOperationException error)
        {
            AppLog.Warn("export", error.Message);
            return false;
        }

        using (guard)
        {
            return paths.Count == 1
                ? await SaveFileAsAsync(provider, title, paths[0])
                : await SaveFilesToFolderAsync(provider, title, paths);
        }
    }

    private static async Task<bool> SaveFileAsAsync(IStorageProvider provider, string title, string sourcePath)
    {
        using var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = Path.GetExtension(sourcePath).TrimStart('.'),
            SuggestedFileName = Path.GetFileName(sourcePath),
            ShowOverwritePrompt = true,
        });
        if (file is null) return false;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            try
            {
                DesktopExportFiles.CopyReplacing(sourcePath, localPath);
                AppLog.Info("export", $"Saved {ForDisplay(localPath)}");
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A portal-mapped path may not allow a temporary file beside it; write through the portal instead.
                AppLog.Warn("export", $"Writing beside {ForDisplay(localPath)} failed, writing in place: {error.Message}");
            }
        }

        await CopyToStorageFileAsync(sourcePath, file);
        AppLog.Info("export", $"Saved {file.Name}");
        return true;
    }

    private static async Task<bool> SaveFilesToFolderAsync(IStorageProvider provider, string title, IReadOnlyList<string> paths)
    {
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        try
        {
            if (folders.Count == 0) return false;
            var folder = folders[0];

            var localPath = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath) && Directory.Exists(localPath))
            {
                foreach (var written in DesktopExportFiles.CopyAllUnique(paths, localPath))
                    AppLog.Info("export", $"Saved {ForDisplay(written)}");
                return true;
            }

            foreach (var path in paths)
            {
                var name = await FreeNameAsync(folder, Path.GetFileName(path));
                using var file = await folder.CreateFileAsync(name)
                    ?? throw new IOException($"Could not create {name} in {folder.Name}.");
                await CopyToStorageFileAsync(path, file);
                AppLog.Info("export", $"Saved {name} in {folder.Name}");
            }
            return true;
        }
        finally
        {
            foreach (var folder in folders) folder.Dispose();
        }
    }

    private static async Task<string> FreeNameAsync(IStorageFolder folder, string fileName)
    {
        foreach (var candidate in DesktopExportFiles.CandidateNames(fileName))
        {
            using var existing = await folder.GetFileAsync(candidate);
            if (existing is null) return candidate;
        }
        throw new IOException($"No free file name for {fileName}.");
    }

    private static async Task CopyToStorageFileAsync(string sourcePath, IStorageFile file)
    {
        await using var target = await file.OpenWriteAsync();
        if (target.CanSeek) target.SetLength(0);
        await using var source = File.OpenRead(sourcePath);
        await source.CopyToAsync(target);
    }
}
