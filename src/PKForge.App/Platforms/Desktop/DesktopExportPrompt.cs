namespace PKForge.App.Platforms.Desktop;

internal enum ExportChoice { Export, ExportAndOpen, SaveAs, Cancel }

/// <summary>What the export prompt shows: its heading, where the files will go, and the choices offered.</summary>
internal sealed record ExportPromptContent(string Heading, string Destination, IReadOnlyList<ExportChoice> Choices);

/// <summary>The side effects of an export, kept apart from the decisions the prompt makes.</summary>
internal interface IDesktopExportActions
{
    /// <summary>Whether the system Save dialog can be offered.</summary>
    bool CanSaveAs { get; }

    string ForDisplay(string path);

    void ExportToFolder(IReadOnlyList<string> paths, string directory);

    Task OpenFolderAsync(string directory);

    /// <summary>Runs the system Save dialog; false when it was cancelled or could not open.</summary>
    Task<bool> SaveAsAsync(string heading, IReadOnlyList<string> paths);
}

/// <summary>
/// The desktop export flow: a prompt that names the destination folder, with the system Save
/// dialog only on request. Nothing is written when the user cancels, and a cancelled Save
/// dialog returns to the prompt.
/// </summary>
internal static class DesktopExportPrompt
{
    public static ExportPromptContent Describe(string title, IReadOnlyList<string> paths, string directory, IDesktopExportActions actions)
    {
        var heading = string.IsNullOrWhiteSpace(title) ? "Export" : title;
        var destination = paths.Count == 1
            ? $"Saves to {actions.ForDisplay(DesktopExportFiles.UniquePath(directory, Path.GetFileName(paths[0])))}"
            : $"Saves {paths.Count} files to {actions.ForDisplay(directory)}";
        List<ExportChoice> choices = [ExportChoice.Export, ExportChoice.ExportAndOpen];
        if (actions.CanSaveAs)
            choices.Add(ExportChoice.SaveAs);
        choices.Add(ExportChoice.Cancel);
        return new ExportPromptContent(heading, destination, choices);
    }

    /// <summary>
    /// Exports <paramref name="paths"/> into <paramref name="directory"/> as the user decides
    /// through <paramref name="ask"/>; with no way to ask, the files go straight to the folder.
    /// </summary>
    public static async Task RunAsync(
        string title,
        IReadOnlyList<string> paths,
        string directory,
        Func<ExportPromptContent, Task<ExportChoice>>? ask,
        IDesktopExportActions actions)
    {
        if (paths.Count == 0) return;

        if (ask is null)
        {
            actions.ExportToFolder(paths, directory);
            return;
        }

        while (true)
        {
            // Described afresh each time: a name taken meanwhile changes where a single file goes.
            var prompt = Describe(title, paths, directory, actions);
            switch (await ask(prompt))
            {
                case ExportChoice.Export:
                    actions.ExportToFolder(paths, directory);
                    return;
                case ExportChoice.ExportAndOpen:
                    actions.ExportToFolder(paths, directory);
                    await actions.OpenFolderAsync(directory);
                    return;
                case ExportChoice.SaveAs when prompt.Choices.Contains(ExportChoice.SaveAs):
                    if (await actions.SaveAsAsync(prompt.Heading, paths)) return;
                    break;
                default:
                    return;
            }
        }
    }
}
