using PKForge.Domain;
using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class EmulatorRootDiscoveryTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "pkforge-discovery-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _home;
    private readonly string _media;

    public EmulatorRootDiscoveryTests()
    {
        _home = Path.Combine(_temp, "home");
        _media = Path.Combine(_temp, "media");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_media);
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
    public async Task RetroArchDiscovery_ParsesConfigSavesDirectory()
    {
        var cfgDir = Path.Combine(_home, ".config", "retroarch");
        Directory.CreateDirectory(cfgDir);

        var targetSaves = Path.Combine(_home, "retro_saves");
        Directory.CreateDirectory(targetSaves);

        var cfgContent = $"""
            # RetroArch configuration
            video_fullscreen = "false"
            savefile_directory = "{targetSaves}"
            savestate_directory = "default"
            """;
        File.WriteAllText(Path.Combine(cfgDir, "retroarch.cfg"), cfgContent);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        var raRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.RetroArch);
        Assert.Equal(targetSaves, raRoot.TreeId);
        Assert.Equal("retroarch.cfg", raRoot.SourceName);
    }

    [Fact]
    public async Task MelonDsDiscovery_ParsesIniSaveFilePath()
    {
        var cfgDir = Path.Combine(_home, ".config", "melonDS");
        Directory.CreateDirectory(cfgDir);

        var targetSaves = Path.Combine(_home, "melonds_saves");
        Directory.CreateDirectory(targetSaves);

        var iniContent = $"""
            [General]
            SaveFilePath = "{targetSaves}"
            DirectBoot = 1
            """;
        File.WriteAllText(Path.Combine(cfgDir, "melonDS.ini"), iniContent);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        var melonRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.MelonDS);
        Assert.Equal(targetSaves, melonRoot.TreeId);
        Assert.Equal("melonDS.ini", melonRoot.SourceName);
    }

    [Fact]
    public async Task MgbaDiscovery_ParsesConfigIniAndQtIni()
    {
        var cfgDir = Path.Combine(_home, ".config", "mgba");
        Directory.CreateDirectory(cfgDir);

        var targetSaves = Path.Combine(_home, "mgba_saves");
        Directory.CreateDirectory(targetSaves);

        var iniContent = $"""
            [ports.qt]
            savegamePath={targetSaves}
            """;
        File.WriteAllText(Path.Combine(cfgDir, "config.ini"), iniContent);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        var mgbaRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.MGba);
        Assert.Equal(targetSaves, mgbaRoot.TreeId);
        Assert.Equal("config.ini", mgbaRoot.SourceName);
    }

    [Fact]
    public async Task RyujinxAndEdenAndThreeDs_DiscoversStandardPaths()
    {
        var ryuSave = Path.Combine(_home, ".config", "Ryujinx", "bis", "user", "save");
        Directory.CreateDirectory(ryuSave);

        var yuzuSave = Path.Combine(_home, ".local", "share", "yuzu", "nand", "user", "save");
        Directory.CreateDirectory(yuzuSave);

        var citraSdmc = Path.Combine(_home, ".local", "share", "citra-emu", "sdmc");
        Directory.CreateDirectory(citraSdmc);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.Ryujinx && r.TreeId == ryuSave);
        Assert.Contains(roots, r => r.Kind == EmulatorKind.Eden && r.TreeId == yuzuSave);
        Assert.Contains(roots, r => r.Kind == EmulatorKind.Azahar && r.TreeId == citraSdmc);
    }

    [Fact]
    public async Task EmuDeckAndRetroDeck_DiscoversRomFolders()
    {
        var emuDeckRoms = Path.Combine(_home, "Emulation", "roms");
        Directory.CreateDirectory(emuDeckRoms);

        var retroDeckRoms = Path.Combine(_home, "RetroDeck", "roms");
        Directory.CreateDirectory(retroDeckRoms);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == emuDeckRoms && r.SourceName == "EmuDeck");
        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == retroDeckRoms && r.SourceName == "RetroDECK");
    }

    [Fact]
    public async Task MountedDrives_DiscoversRomFolders()
    {
        var driveRoms = Path.Combine(_media, "testuser", "StorageDrive", "Emulation", "roms");
        Directory.CreateDirectory(driveRoms);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        var driveRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.RomFolder && r.SourceName == "Mounted Drive");
        Assert.Equal(driveRoms, driveRoot.TreeId);
        Assert.Contains("StorageDrive", driveRoot.DisplayName);
    }

    [Fact]
    public async Task Deduplicate_CollapsesIdenticalAndSymlinkedPaths()
    {
        var realDir = Path.Combine(_home, "real_roms");
        Directory.CreateDirectory(realDir);

        var symlinkDir = Path.Combine(_home, "symlink_roms");
        try
        {
            Directory.CreateSymbolicLink(symlinkDir, realDir);
        }
        catch
        {
            return;
        }

        var cfgDir = Path.Combine(_home, ".config", "retroarch");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "retroarch.cfg"), $"savefile_directory = \"{symlinkDir}\"");

        var emuDeckRoms = Path.Combine(_home, "Emulation", "roms");
        Directory.CreateDirectory(Path.GetDirectoryName(emuDeckRoms)!);
        Directory.CreateSymbolicLink(emuDeckRoms, realDir);

        var discovery = new LinuxEmulatorRootDiscovery(_home, _media);
        var roots = await discovery.DiscoverRootsAsync();

        var matching = roots.Where(r => r.TreeId == realDir).ToList();
        Assert.Single(matching);
    }
}
