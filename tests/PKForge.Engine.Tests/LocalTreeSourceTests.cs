using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class LocalTreeSourceTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "pkforge-localtree-test-" + Guid.NewGuid().ToString("N"));
    private readonly LocalTreeSource _tree = new();

    public LocalTreeSourceTests()
    {
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            try
            {
                Directory.Delete(_temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Root_ExpandsTildeToUserProfile()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expanded = _tree.Root("~/Games/Pokemon");
        Assert.Equal(Path.Combine(userHome, "Games/Pokemon"), expanded);

        var normalPath = "/opt/games/saves";
        Assert.Equal(normalPath, _tree.Root(normalPath));
    }

    [Fact]
    public void RootName_ReturnsFolderBaseName()
    {
        Assert.Equal("saves", _tree.RootName("/home/user/games/saves"));
        Assert.Equal("saves", _tree.RootName("/home/user/games/saves/"));
        Assert.Equal("/", _tree.RootName("/"));
    }

    [Fact]
    public void List_FiltersHiddenEntriesAndReturnsVisibleFilesAndDirs()
    {
        var visibleFile = Path.Combine(_temp, "emerald.sav");
        var hiddenFile = Path.Combine(_temp, ".ds_store");
        var visibleDir = Path.Combine(_temp, "gba");
        var hiddenDir = Path.Combine(_temp, ".cache");

        File.WriteAllBytes(visibleFile, [1, 2, 3]);
        File.WriteAllBytes(hiddenFile, [4, 5, 6]);
        Directory.CreateDirectory(visibleDir);
        Directory.CreateDirectory(hiddenDir);

        var entries = _tree.List(_temp);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "emerald.sav" && !e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "gba" && e.IsDirectory);
        Assert.DoesNotContain(entries, e => e.Name == ".ds_store");
        Assert.DoesNotContain(entries, e => e.Name == ".cache");
    }

    [Fact]
    public void OpenRead_ReadsExistingFileAndReturnsNullForMissing()
    {
        var filePath = Path.Combine(_temp, "save.bin");
        byte[] expected = [0x12, 0x34, 0x56, 0x78];
        File.WriteAllBytes(filePath, expected);

        using (var stream = _tree.OpenRead(filePath))
        {
            Assert.NotNull(stream);
            var buffer = new byte[expected.Length];
            var read = stream!.Read(buffer, 0, buffer.Length);
            Assert.Equal(expected.Length, read);
            Assert.Equal(expected, buffer);
        }

        var missingStream = _tree.OpenRead(Path.Combine(_temp, "nonexistent.bin"));
        Assert.Null(missingStream);
    }

    [Fact]
    public void List_ResolvesDirectorySymlinks()
    {
        var realDir = Path.Combine(_temp, "real_roms");
        Directory.CreateDirectory(realDir);
        File.WriteAllBytes(Path.Combine(realDir, "fire.sav"), [1, 2]);

        var linkDir = Path.Combine(_temp, "symlink_roms");
        try
        {
            Directory.CreateSymbolicLink(linkDir, realDir);
        }
        catch (Exception)
        {
            return;
        }

        var rootEntries = _tree.List(_temp);
        var linkEntry = Assert.Single(rootEntries, e => e.Name == "symlink_roms");
        Assert.True(linkEntry.IsDirectory);

        var targetEntries = _tree.List(linkDir);
        var fileEntry = Assert.Single(targetEntries, e => e.Name == "fire.sav");
        Assert.False(fileEntry.IsDirectory);
    }

    [Fact]
    public void List_TreatsSymlinkToSaveFileAsFileWithTargetTimestamp()
    {
        if (!OperatingSystem.IsLinux()) return;

        var realDir = Path.Combine(_temp, "real");
        Directory.CreateDirectory(realDir);
        var realSave = Path.Combine(realDir, "a.sav");
        File.WriteAllBytes(realSave, [1, 2, 3]);
        var targetModified = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(realSave, targetModified);

        var linkDir = Path.Combine(_temp, "saves");
        Directory.CreateDirectory(linkDir);
        File.CreateSymbolicLink(Path.Combine(linkDir, "link.sav"), realSave);

        var entry = Assert.Single(_tree.List(linkDir));
        Assert.Equal("link.sav", entry.Name);
        Assert.False(entry.IsDirectory);
        Assert.Equal(new DateTimeOffset(targetModified), entry.LastModified);

        using var stream = _tree.OpenRead(entry.Node);
        Assert.NotNull(stream);
        Assert.Equal(3, stream!.Length);
    }

    [Fact]
    public void List_SkipsDanglingSymlinks()
    {
        if (!OperatingSystem.IsLinux()) return;

        File.CreateSymbolicLink(Path.Combine(_temp, "gone.sav"), Path.Combine(_temp, "missing.sav"));
        File.WriteAllBytes(Path.Combine(_temp, "kept.sav"), [1]);

        var entry = Assert.Single(_tree.List(_temp));
        Assert.Equal("kept.sav", entry.Name);
    }

    [Fact]
    public async Task ListAndOpenRead_SkipFifosAndDevices()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/dev/zero")) return;

        var fifo = Path.Combine(_temp, "pipe.sav");
        Assert.Equal(0, MakeFifo(fifo, 0x180));
        var device = Path.Combine(_temp, "zero.sav");
        File.CreateSymbolicLink(device, "/dev/zero");
        File.WriteAllBytes(Path.Combine(_temp, "kept.sav"), [1]);

        var entry = Assert.Single(_tree.List(_temp));
        Assert.Equal("kept.sav", entry.Name);

        var open = Task.Run(() => (Fifo: _tree.OpenRead(fifo), Device: _tree.OpenRead(device)));
        Assert.Same(open, await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(10))));
        var (fifoStream, deviceStream) = await open;
        Assert.Null(fifoStream);
        Assert.Null(deviceStream);
    }

    [Fact]
    public void IsRegularFile_FollowsSymlinksToRegularFilesOnly()
    {
        var file = Path.Combine(_temp, "a.sav");
        File.WriteAllBytes(file, [1]);

        Assert.True(LocalTreeSource.IsRegularFile(file));
        Assert.False(LocalTreeSource.IsRegularFile(_temp));
        Assert.False(LocalTreeSource.IsRegularFile(Path.Combine(_temp, "missing.sav")));

        if (!OperatingSystem.IsLinux()) return;
        var link = Path.Combine(_temp, "link.sav");
        File.CreateSymbolicLink(link, file);
        Assert.True(LocalTreeSource.IsRegularFile(link));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, uint mode);

    [Fact]
    public void LoopProtection_DetectsCircularDirectorySymlinks()
    {
        if (!OperatingSystem.IsLinux()) return;

        var dirA = Path.Combine(_temp, "dirA");
        Directory.CreateDirectory(dirA);

        var loopLink = Path.Combine(dirA, "loop");
        try
        {
            Directory.CreateSymbolicLink(loopLink, dirA);
        }
        catch (Exception)
        {
            return;
        }

        var hasInodeA = LocalTreeSource.TryGetInode(dirA, out var devA, out var inoA);
        var hasInodeLoop = LocalTreeSource.TryGetInode(loopLink, out var devLoop, out var inoLoop);

        Assert.True(hasInodeA);
        Assert.True(hasInodeLoop);
        Assert.Equal(devA, devLoop);
        Assert.Equal(inoA, inoLoop);

        _tree.Root(dirA);

        var entries = _tree.List(dirA);
        Assert.DoesNotContain(entries, e => e.Name == "loop");
    }
}
