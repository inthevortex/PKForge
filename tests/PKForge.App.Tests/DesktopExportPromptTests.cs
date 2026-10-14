using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesktopExportPromptTests : IDisposable
{
    private readonly string _exports = Path.Combine(Path.GetTempPath(), "pkforge-export-prompt-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_exports)) Directory.Delete(_exports, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeActions : IDesktopExportActions
    {
        public List<string> Log { get; } = [];
        public bool CanSaveAs { get; set; } = true;
        public Queue<bool> SaveAsResults { get; } = new();

        public string ForDisplay(string path) => path;

        public void ExportToFolder(IReadOnlyList<string> paths, string directory) => Log.Add($"export {paths.Count}");

        public Task OpenFolderAsync(string directory)
        {
            Log.Add("open");
            return Task.CompletedTask;
        }

        public Task<bool> SaveAsAsync(string heading, IReadOnlyList<string> paths)
        {
            Log.Add($"save as {heading}");
            return Task.FromResult(SaveAsResults.Count > 0 && SaveAsResults.Dequeue());
        }
    }

    private static Func<ExportPromptContent, Task<ExportChoice>> Answers(List<ExportPromptContent> shown, params ExportChoice[] answers)
    {
        var queue = new Queue<ExportChoice>(answers);
        return prompt =>
        {
            shown.Add(prompt);
            return Task.FromResult(queue.Dequeue());
        };
    }

    [Fact]
    public void Describe_OneFileNamesTheFreePathItWillGet()
    {
        Directory.CreateDirectory(_exports);
        File.WriteAllText(Path.Combine(_exports, "pikachu.pk9"), "taken");

        var prompt = DesktopExportPrompt.Describe("", ["/somewhere/pikachu.pk9"], _exports, new FakeActions());

        Assert.Equal("Export", prompt.Heading);
        Assert.EndsWith("pikachu (2).pk9", prompt.Destination, StringComparison.Ordinal);
        Assert.StartsWith("Saves to ", prompt.Destination, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_SeveralFilesNameTheFolderAndTheCount()
    {
        var prompt = DesktopExportPrompt.Describe("PKForge logs", ["/a/one.zip", "/a/two.zip"], _exports, new FakeActions());

        Assert.Equal("PKForge logs", prompt.Heading);
        Assert.StartsWith("Saves 2 files to ", prompt.Destination, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_SaveAsIsOfferedOnlyWhenTheSystemDialogIsAvailable()
    {
        var withDialog = DesktopExportPrompt.Describe("t", ["/a/one.zip"], _exports, new FakeActions());
        var withoutDialog = DesktopExportPrompt.Describe("t", ["/a/one.zip"], _exports, new FakeActions { CanSaveAs = false });

        Assert.Equal([ExportChoice.Export, ExportChoice.ExportAndOpen, ExportChoice.SaveAs, ExportChoice.Cancel], withDialog.Choices);
        Assert.Equal([ExportChoice.Export, ExportChoice.ExportAndOpen, ExportChoice.Cancel], withoutDialog.Choices);
    }

    [Fact]
    public async Task Run_NothingToExportShowsNothing()
    {
        var actions = new FakeActions();
        var shown = new List<ExportPromptContent>();

        await DesktopExportPrompt.RunAsync("t", [], _exports, Answers(shown), actions);

        Assert.Empty(shown);
        Assert.Empty(actions.Log);
    }

    [Fact]
    public async Task Run_WithNoWayToAskExportsStraightToTheFolder()
    {
        var actions = new FakeActions();

        await DesktopExportPrompt.RunAsync("t", ["/a/one.zip"], _exports, null, actions);

        Assert.Equal(["export 1"], actions.Log);
    }

    [Fact]
    public async Task Run_CancelWritesNothing()
    {
        var actions = new FakeActions();
        var shown = new List<ExportPromptContent>();

        await DesktopExportPrompt.RunAsync("t", ["/a/one.zip"], _exports, Answers(shown, ExportChoice.Cancel), actions);

        Assert.Single(shown);
        Assert.Empty(actions.Log);
    }

    [Fact]
    public async Task Run_ExportWritesWithoutOpeningTheFolder()
    {
        var actions = new FakeActions();

        await DesktopExportPrompt.RunAsync("t", ["/a/one.zip"], _exports, Answers([], ExportChoice.Export), actions);

        Assert.Equal(["export 1"], actions.Log);
    }

    [Fact]
    public async Task Run_ExportAndOpenWritesThenOpensTheFolder()
    {
        var actions = new FakeActions();

        await DesktopExportPrompt.RunAsync("t", ["/a/one.zip", "/a/two.zip"], _exports, Answers([], ExportChoice.ExportAndOpen), actions);

        Assert.Equal(["export 2", "open"], actions.Log);
    }

    [Fact]
    public async Task Run_ACancelledSaveDialogReturnsToThePrompt()
    {
        var actions = new FakeActions();
        actions.SaveAsResults.Enqueue(false);
        var shown = new List<ExportPromptContent>();

        await DesktopExportPrompt.RunAsync("Logs", ["/a/one.zip"], _exports,
            Answers(shown, ExportChoice.SaveAs, ExportChoice.Export), actions);

        Assert.Equal(2, shown.Count);
        Assert.Equal(["save as Logs", "export 1"], actions.Log);
    }

    [Fact]
    public async Task Run_ACompletedSaveDialogEndsTheFlowWithoutExportingToTheFolder()
    {
        var actions = new FakeActions();
        actions.SaveAsResults.Enqueue(true);

        await DesktopExportPrompt.RunAsync("", ["/a/one.zip"], _exports, Answers([], ExportChoice.SaveAs), actions);

        Assert.Equal(["save as Export"], actions.Log);
    }

    [Fact]
    public async Task Run_SaveAsIsIgnoredWhenItWasNotOffered()
    {
        var actions = new FakeActions { CanSaveAs = false };

        await DesktopExportPrompt.RunAsync("t", ["/a/one.zip"], _exports, Answers([], ExportChoice.SaveAs), actions);

        Assert.Empty(actions.Log);
    }
}
