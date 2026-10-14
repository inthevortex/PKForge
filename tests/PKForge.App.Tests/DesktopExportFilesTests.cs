using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesktopExportFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkforge-export-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _exports;
    private readonly string _sources;

    public DesktopExportFilesTests()
    {
        _exports = Path.Combine(_root, "exports");
        _sources = Path.Combine(_root, "sources");
        Directory.CreateDirectory(_sources);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Source(string name, byte[] data)
    {
        var path = Path.Combine(_sources, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void UniqueName_ReturnsTheNameWhenItIsFree()
    {
        Assert.Equal("pikachu.pk9", DesktopExportFiles.UniqueName("pikachu.pk9", _ => false));
    }

    [Fact]
    public void UniqueName_AddsTheFirstFreeNumberBeforeTheExtension()
    {
        var taken = new HashSet<string> { "logs.zip", "logs (2).zip" };
        Assert.Equal("logs (3).zip", DesktopExportFiles.UniqueName("logs.zip", taken.Contains));
    }

    [Fact]
    public void UniqueName_HandlesNamesWithoutExtension()
    {
        var taken = new HashSet<string> { "report" };
        Assert.Equal("report (2)", DesktopExportFiles.UniqueName("report", taken.Contains));
    }

    [Fact]
    public void CopyUnique_CreatesTheFolderAndCopiesTheBytes()
    {
        var source = Source("box-01.txt", [1, 2, 3]);

        var written = DesktopExportFiles.CopyUnique(source, _exports);

        Assert.Equal(Path.Combine(_exports, "box-01.txt"), written);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(written));
    }

    [Fact]
    public void CopyUnique_NeverReplacesAnEarlierExport()
    {
        Directory.CreateDirectory(_exports);
        var earlier = Path.Combine(_exports, "save-modified.sav");
        File.WriteAllBytes(earlier, [9, 9]);
        var source = Source("save-modified.sav", [1, 2, 3]);

        var first = DesktopExportFiles.CopyUnique(source, _exports);
        var second = DesktopExportFiles.CopyUnique(source, _exports);

        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(earlier));
        Assert.Equal(Path.Combine(_exports, "save-modified (2).sav"), first);
        Assert.Equal(Path.Combine(_exports, "save-modified (3).sav"), second);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(second));
    }

    [Fact]
    public void CopyUnique_LeavesNoTemporaryFiles()
    {
        var source = Source("mon.pk8", [7]);

        DesktopExportFiles.CopyUnique(source, _exports);

        Assert.Equal(new string?[] { "mon.pk8" }, Directory.GetFiles(_exports).Select(Path.GetFileName));
    }

    [Fact]
    public void CopyAllUnique_GivesSameNamedSourcesDistinctNames()
    {
        var a = Source("mon.pk9", [1]);
        Directory.CreateDirectory(Path.Combine(_sources, "other"));
        var b = Path.Combine(_sources, "other", "mon.pk9");
        File.WriteAllBytes(b, [2]);

        var written = DesktopExportFiles.CopyAllUnique([a, b], _exports);

        Assert.Equal(new[] { Path.Combine(_exports, "mon.pk9"), Path.Combine(_exports, "mon (2).pk9") }, written);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(written[0]));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(written[1]));
    }

    [Fact]
    public void CopyAllUnique_RemovesItsCopiesWhenOneFails()
    {
        var a = Source("one.pk9", [1]);
        var missing = Path.Combine(_sources, "missing.pk9");

        Assert.ThrowsAny<IOException>(() => DesktopExportFiles.CopyAllUnique([a, missing], _exports));

        Assert.Empty(Directory.GetFiles(_exports));
    }

    [Fact]
    public void CopyReplacing_ReplacesTheChosenFileAndLeavesNoTemporaryFiles()
    {
        var target = Path.Combine(_root, "chosen.txt");
        File.WriteAllBytes(target, [0]);
        var source = Source("team.txt", [4, 5]);

        DesktopExportFiles.CopyReplacing(source, target);

        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(target));
        Assert.Equal(new string?[] { "chosen.txt" }, Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void CopyReplacing_KeepsTheExistingFileWhenTheSourceIsMissing()
    {
        var target = Path.Combine(_root, "chosen.txt");
        File.WriteAllBytes(target, [0]);

        Assert.ThrowsAny<IOException>(() => DesktopExportFiles.CopyReplacing(Path.Combine(_sources, "missing.txt"), target));

        Assert.Equal(new byte[] { 0 }, File.ReadAllBytes(target));
        Assert.Equal(new string?[] { "chosen.txt" }, Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("/home/ash/Documents/PKForge/exports/a.zip", "/home/ash", "~/Documents/PKForge/exports/a.zip")]
    [InlineData("/home/ash", "/home/ash/", "~")]
    [InlineData("/home/ashley/a.zip", "/home/ash", "/home/ashley/a.zip")]
    [InlineData("/home/alice/saves", "/home/al", "/home/alice/saves")]
    [InlineData("/tmp/a.zip", "", "/tmp/a.zip")]
    [InlineData("/tmp/a.zip", "/", "/tmp/a.zip")]
    public void ForDisplay_ShortensTheHomeFolder(string path, string home, string expected)
    {
        Assert.Equal(expected, DesktopExportFiles.ForDisplay(path, home));
    }
}
