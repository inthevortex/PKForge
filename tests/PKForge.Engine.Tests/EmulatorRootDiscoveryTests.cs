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

        var discovery = Discovery();
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

        var discovery = Discovery();
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

        var discovery = Discovery();
        var roots = await discovery.DiscoverRootsAsync();

        var mgbaRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.RomFolder);
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

        var discovery = Discovery();
        var roots = await discovery.DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.Ryujinx && r.TreeId == Path.Combine(_home, ".config", "Ryujinx"));
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

        var discovery = Discovery();
        var roots = await discovery.DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == emuDeckRoms && r.SourceName == "EmuDeck");
        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == retroDeckRoms && r.SourceName == "RetroDECK");
    }

    [Fact]
    public async Task MountedDrives_DiscoversRomFolders()
    {
        var driveRoms = Path.Combine(_media, "testuser", "StorageDrive", "Emulation", "roms");
        Directory.CreateDirectory(driveRoms);

        var discovery = Discovery();
        var roots = await discovery.DiscoverRootsAsync();

        var driveRoot = Assert.Single(roots, r => r.Kind == EmulatorKind.RomFolder && r.SourceName == "Mounted Drive");
        Assert.Equal(driveRoms, driveRoot.TreeId);
        Assert.Contains("StorageDrive", driveRoot.DisplayName);
    }

    [Fact]
    public async Task MountedDrives_AreMarkedRemovableAndHomeFoldersAreNot()
    {
        var driveRoms = Path.Combine(_media, "testuser", "StorageDrive", "Emulation", "roms");
        Directory.CreateDirectory(driveRoms);
        var homeRoms = Path.Combine(_home, "Emulation", "roms");
        Directory.CreateDirectory(homeRoms);

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.True(Assert.Single(roots, r => r.TreeId == driveRoms).IsRemovable);
        Assert.False(Assert.Single(roots, r => r.TreeId == homeRoms).IsRemovable);
    }

    [Fact]
    public async Task HomeFolderLinkedToAMountedDrive_IsMarkedRemovable()
    {
        if (!OperatingSystem.IsLinux()) return;

        // EmuDeck on an SD card: ~/Emulation is a link to the card, its subfolders are real.
        var cardEmulation = Path.Combine(_media, "mmcblk0p1", "Emulation");
        Directory.CreateDirectory(Path.Combine(cardEmulation, "roms"));
        Directory.CreateSymbolicLink(Path.Combine(_home, "Emulation"), cardEmulation);

        var roots = await Discovery().DiscoverRootsAsync();

        var emuDeck = Assert.Single(roots, r => r.Kind == EmulatorKind.RomFolder);
        Assert.Equal(Path.Combine(_home, "Emulation", "roms"), emuDeck.TreeId);
        Assert.True(emuDeck.IsRemovable);
    }

    [Fact]
    public async Task Deduplicate_CollapsesSymlinkedRootAndKeepsThePathItWasFoundUnder()
    {
        if (!OperatingSystem.IsLinux()) return;

        var realDir = Path.Combine(_home, "real_saves");
        Directory.CreateDirectory(realDir);
        var symlinkDir = Path.Combine(_home, "symlink_saves");
        Directory.CreateSymbolicLink(symlinkDir, realDir);
        WriteRetroArchConfig(symlinkDir);

        var driveSaves = Path.Combine(_media, "deck", "Emulation", "saves", "retroarch", "saves");
        Directory.CreateDirectory(Path.GetDirectoryName(driveSaves)!);
        Directory.CreateSymbolicLink(driveSaves, realDir);

        var roots = await Discovery().DiscoverRootsAsync();

        var retroArch = Assert.Single(roots, r => r.Kind == EmulatorKind.RetroArch);
        Assert.Equal(symlinkDir, retroArch.TreeId);
        Assert.DoesNotContain(roots, r => r.TreeId == realDir);
    }

    [Fact]
    public async Task Deduplicate_PrefersTheEmuDeckPathOverTheFlatpakDefault()
    {
        if (!OperatingSystem.IsLinux()) return;

        var flatpakSaves = Path.Combine(_home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "saves");
        Directory.CreateDirectory(flatpakSaves);
        var emuDeckSaves = Path.Combine(_home, "Emulation", "saves", "retroarch", "saves");
        Directory.CreateDirectory(Path.GetDirectoryName(emuDeckSaves)!);
        Directory.CreateSymbolicLink(emuDeckSaves, flatpakSaves);

        var roots = await Discovery().DiscoverRootsAsync();

        var retroArch = Assert.Single(roots);
        Assert.Equal(EmulatorKind.RetroArch, retroArch.Kind);
        Assert.Equal(emuDeckSaves, retroArch.TreeId);
    }

    [Fact]
    public async Task Deduplicate_PrefersTheEmuDeckPathWhenAParentFolderIsTheSymlink()
    {
        if (!OperatingSystem.IsLinux()) return;

        var storage = Path.Combine(_home, "storage");
        var realSaves = Path.Combine(storage, "retroarch", "saves");
        Directory.CreateDirectory(realSaves);
        WriteRetroArchConfig(realSaves);

        var emulation = Path.Combine(_home, "Emulation");
        Directory.CreateDirectory(emulation);
        Directory.CreateSymbolicLink(Path.Combine(emulation, "saves"), storage);

        var roots = await Discovery().DiscoverRootsAsync();

        var retroArch = Assert.Single(roots, r => r.Kind == EmulatorKind.RetroArch);
        Assert.Equal(Path.Combine(emulation, "saves", "retroarch", "saves"), retroArch.TreeId);
    }

    [Fact]
    public async Task Deduplicate_KeepsTheEmulatorKindOverRomFolderForOneFolder()
    {
        if (!OperatingSystem.IsLinux()) return;

        var realDir = Path.Combine(_home, "shared");
        Directory.CreateDirectory(realDir);
        WriteRetroArchConfig(realDir);

        var emuDeckRoms = Path.Combine(_home, "Emulation", "roms");
        Directory.CreateDirectory(Path.GetDirectoryName(emuDeckRoms)!);
        Directory.CreateSymbolicLink(emuDeckRoms, realDir);

        var roots = await Discovery().DiscoverRootsAsync();

        var root = Assert.Single(roots);
        Assert.Equal(EmulatorKind.RetroArch, root.Kind);
        Assert.Equal(realDir, root.TreeId);
    }

    [Fact]
    public async Task DiscoverRootsAsync_HonoursCancellation()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".config", "retroarch", "saves"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Discovery().DiscoverRootsAsync(cancelled.Token));
    }

    [Fact]
    public async Task XdgDirectories_AreHonouredWhenAbsolute()
    {
        var configHome = Path.Combine(_temp, "xdg-config");
        var dataHome = Path.Combine(_temp, "xdg-data");
        var ryujinx = Path.Combine(configHome, "Ryujinx");
        var eden = Path.Combine(dataHome, "eden", "nand", "user", "save");
        Directory.CreateDirectory(ryujinx);
        Directory.CreateDirectory(eden);
        Directory.CreateDirectory(Path.Combine(_home, ".config", "Ryujinx"));
        Directory.CreateDirectory(Path.Combine(_home, ".local", "share", "eden", "nand", "user", "save"));

        var roots = await Discovery(new() { ["XDG_CONFIG_HOME"] = configHome, ["XDG_DATA_HOME"] = dataHome }).DiscoverRootsAsync();

        Assert.Equal(ryujinx, Assert.Single(roots, r => r.Kind == EmulatorKind.Ryujinx).TreeId);
        Assert.Equal(eden, Assert.Single(roots, r => r.Kind == EmulatorKind.Eden).TreeId);
    }

    [Fact]
    public async Task XdgDirectories_FallBackToTheHomeDefaultsWhenRelative()
    {
        var ryujinx = Path.Combine(_home, ".config", "Ryujinx");
        var dolphin = Path.Combine(_home, ".local", "share", "dolphin-emu", "GC");
        Directory.CreateDirectory(ryujinx);
        Directory.CreateDirectory(dolphin);

        var roots = await Discovery(new() { ["XDG_CONFIG_HOME"] = "relative/config", ["XDG_DATA_HOME"] = "" }).DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.Ryujinx && r.TreeId == ryujinx);
        Assert.Contains(roots, r => r.Kind == EmulatorKind.Dolphin && r.TreeId == dolphin);
    }

    [Fact]
    public async Task EmuDeck_ReadsSettingsFromTheConfigFolder()
    {
        var emulation = Path.Combine(_temp, "sdcard", "Emulation");
        var roms = Path.Combine(emulation, "roms");
        var raSaves = Path.Combine(emulation, "saves", "retroarch", "saves");
        Directory.CreateDirectory(roms);
        Directory.CreateDirectory(raSaves);
        var settings = Path.Combine(_home, ".config", "EmuDeck");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "settings.sh"), $"""
            #!/bin/bash
            emulationPath={emulation}
            romsPath={roms}
            savesPath="{Path.Combine(emulation, "saves")}"
            """);

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == roms && r.SourceName == "settings.sh");
        Assert.Contains(roots, r => r.Kind == EmulatorKind.RetroArch && r.TreeId == raSaves && r.SourceName == "settings.sh");
    }

    [Fact]
    public async Task EmuDeck_FallsBackToTheLegacySettingsFile()
    {
        var roms = Path.Combine(_temp, "sdcard", "roms");
        Directory.CreateDirectory(roms);
        var settings = Path.Combine(_home, "emudeck");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "settings.sh"), $"export romsPath='{roms}'");

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.RomFolder && r.TreeId == roms && r.SourceName == "settings.sh");
    }

    [Fact]
    public async Task Flatpaks_AreFoundUnderTheirPublishedIds()
    {
        var app = Path.Combine(_home, ".var", "app");
        var azahar = Path.Combine(app, "org.azahar_emu.Azahar", "data", "azahar-emu", "sdmc");
        var eden = Path.Combine(app, "dev.eden_emu.eden", "data", "eden", "nand", "user", "save");
        Directory.CreateDirectory(azahar);
        Directory.CreateDirectory(eden);

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.Contains(roots, r => r.Kind == EmulatorKind.Azahar && r.TreeId == azahar && r.DisplayName == "Azahar (Flatpak) SDMC");
        Assert.Contains(roots, r => r.Kind == EmulatorKind.Eden && r.TreeId == eden && r.DisplayName == "Eden (Flatpak) Saves");
    }

    [Fact]
    public async Task RetroDeck_ReadsRomAndSaveFoldersFromItsConfig()
    {
        var rdHome = Path.Combine(_temp, "sdcard", "retrodeck");
        var roms = Path.Combine(rdHome, "roms");
        var saves = Path.Combine(rdHome, "saves");
        Directory.CreateDirectory(roms);
        Directory.CreateDirectory(saves);
        Directory.CreateDirectory(Path.Combine(_home, "retrodeck", "roms"));
        var config = Path.Combine(_home, ".var", "app", "net.retrodeck.retrodeck", "config", "retrodeck");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "retrodeck.json"), $$"""
            {
              "version": "0.9.0b",
              "paths": {
                "rd_home_path": "{{rdHome}}",
                "roms_path": "{{roms}}",
                "saves_path": "{{saves}}"
              }
            }
            """);

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.Equal(roms, Assert.Single(roots, r => r.DisplayName == "RetroDECK ROMs").TreeId);
        var savesRoot = Assert.Single(roots, r => r.DisplayName == "RetroDECK Saves");
        Assert.Equal(saves, savesRoot.TreeId);
        Assert.Equal(EmulatorKind.RetroArch, savesRoot.Kind);
        Assert.Equal("retrodeck.json", savesRoot.SourceName);
    }

    [Fact]
    public async Task RetroDeck_FallsBackToTheDefaultLayoutWhenItsConfigIsUnreadable()
    {
        var saves = Path.Combine(_home, "retrodeck", "saves");
        Directory.CreateDirectory(saves);
        var config = Path.Combine(_home, ".var", "app", "net.retrodeck.retrodeck", "config", "retrodeck");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "retrodeck.json"), "{ not json");

        var roots = await Discovery().DiscoverRootsAsync();

        var savesRoot = Assert.Single(roots, r => r.DisplayName == "RetroDECK Saves");
        Assert.Equal(saves, savesRoot.TreeId);
        Assert.Equal("RetroDECK", savesRoot.SourceName);
    }

    [Fact]
    public async Task Azahar_UsesTheCustomSdmcFolderFromQtConfig()
    {
        var defaultSdmc = Path.Combine(_home, ".local", "share", "azahar-emu", "sdmc");
        var customSdmc = Path.Combine(_temp, "games", "3ds sd");
        Directory.CreateDirectory(defaultSdmc);
        Directory.CreateDirectory(customSdmc);
        WriteAzaharConfig($"""
            [UI]
            sdmc_directory=/somewhere/else
            [Data%20Storage]
            use_custom_storage\default=false
            use_custom_storage=true
            sdmc_directory\default=false
            sdmc_directory="{customSdmc}/"
            nand_directory\default=true
            nand_directory=
            """);

        var roots = await Discovery().DiscoverRootsAsync();

        var azahar = Assert.Single(roots, r => r.Kind == EmulatorKind.Azahar);
        Assert.Equal(customSdmc, azahar.TreeId);
        Assert.Equal("qt-config.ini", azahar.SourceName);
    }

    [Fact]
    public async Task Azahar_KeepsTheDefaultSdmcWhenCustomStorageIsOff()
    {
        var defaultSdmc = Path.Combine(_home, ".local", "share", "azahar-emu", "sdmc");
        Directory.CreateDirectory(defaultSdmc);
        Directory.CreateDirectory(Path.Combine(_temp, "custom"));
        WriteAzaharConfig($"""
            [Data%20Storage]
            use_custom_storage\default=true
            use_custom_storage=false
            sdmc_directory={Path.Combine(_temp, "custom")}
            """);

        var roots = await Discovery().DiscoverRootsAsync();

        var azahar = Assert.Single(roots, r => r.Kind == EmulatorKind.Azahar);
        Assert.Equal(defaultSdmc, azahar.TreeId);
        Assert.Equal("Default path", azahar.SourceName);
    }

    [Fact]
    public async Task SwitchEmulators_OfferOneRootPerInstall()
    {
        var edenData = Path.Combine(_home, ".local", "share", "eden");
        var edenSaves = Path.Combine(edenData, "nand", "user", "save");
        var yuzuData = Path.Combine(_home, ".local", "share", "yuzu");
        var ryujinx = Path.Combine(_home, ".config", "Ryujinx");
        Directory.CreateDirectory(edenSaves);
        Directory.CreateDirectory(yuzuData);
        Directory.CreateDirectory(Path.Combine(ryujinx, "bis", "user", "save"));

        var roots = await Discovery().DiscoverRootsAsync();

        Assert.Equal([edenSaves, yuzuData], roots.Where(r => r.Kind == EmulatorKind.Eden).Select(r => r.TreeId));
        Assert.Equal(ryujinx, Assert.Single(roots, r => r.Kind == EmulatorKind.Ryujinx).TreeId);
    }

    /// <summary>A fixed environment, so the machine's own XDG variables never leak into a test.</summary>
    private LinuxEmulatorRootDiscovery Discovery(Dictionary<string, string>? environment = null) =>
        new(_home, _media, name => environment?.GetValueOrDefault(name));

    private void WriteAzaharConfig(string content)
    {
        var dir = Path.Combine(_home, ".config", "azahar-emu");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "qt-config.ini"), content);
    }

    private void WriteRetroArchConfig(string savesDirectory)
    {
        var cfgDir = Path.Combine(_home, ".config", "retroarch");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "retroarch.cfg"), $"savefile_directory = \"{savesDirectory}\"");
    }
}
