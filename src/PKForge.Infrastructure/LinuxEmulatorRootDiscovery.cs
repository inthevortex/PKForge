using System.Text.RegularExpressions;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// Automatically discovers emulator save roots and ROM directories across Linux desktop setups
/// (native packages, Flatpaks, EmuDeck, RetroDECK, and external drives).
/// </summary>
public sealed class LinuxEmulatorRootDiscovery(string? homeDirectory = null, string? mediaPrefix = null) : IEmulatorRootDiscovery
{
    private readonly string _home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _media = mediaPrefix ?? "/run/media";

    public ValueTask<IReadOnlyList<DiscoveredRoot>> DiscoverRootsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DiscoveredRoot>();

        DiscoverRetroArch(list);
        DiscoverMelonDs(list);
        DiscoverMgba(list);
        DiscoverDolphin(list);
        DiscoverThreeDs(list);
        DiscoverEden(list);
        DiscoverRyujinx(list);
        DiscoverEmuDeckAndRetroDeck(list);
        DiscoverMountedDrives(list);

        var deduplicated = Deduplicate(list);
        return ValueTask.FromResult<IReadOnlyList<DiscoveredRoot>>(deduplicated);
    }

    private void DiscoverRetroArch(List<DiscoveredRoot> list)
    {
        string[] configFiles =
        [
            Path.Combine(_home, ".config", "retroarch", "retroarch.cfg"),
            Path.Combine(_home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "retroarch.cfg")
        ];

        var configFound = false;
        foreach (var cfg in configFiles)
        {
            if (!File.Exists(cfg)) continue;
            var savesDir = ParseConfigValue(cfg, "savefile_directory");
            if (!string.IsNullOrEmpty(savesDir) && !string.Equals(savesDir, "default", StringComparison.OrdinalIgnoreCase))
            {
                var expanded = ExpandHome(savesDir);
                if (Directory.Exists(expanded))
                {
                    list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, expanded, "RetroArch Saves", "retroarch.cfg"));
                    configFound = true;
                }
            }
        }

        if (!configFound)
        {
            string[] defaultPaths =
            [
                Path.Combine(_home, ".config", "retroarch", "saves"),
                Path.Combine(_home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "saves"),
                Path.Combine(_home, ".config", "retroarch")
            ];
            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, path, "RetroArch Saves", "Default path"));
                    break;
                }
            }
        }
    }

    private void DiscoverMelonDs(List<DiscoveredRoot> list)
    {
        string[] configFiles =
        [
            Path.Combine(_home, ".config", "melonDS", "melonDS.toml"),
            Path.Combine(_home, ".config", "melonDS", "melonDS.ini"),
            Path.Combine(_home, ".var", "app", "net.kuribo64.melonDS", "config", "melonDS", "melonDS.toml"),
            Path.Combine(_home, ".var", "app", "net.kuribo64.melonDS", "config", "melonDS", "melonDS.ini")
        ];

        var configFound = false;
        foreach (var cfg in configFiles)
        {
            if (!File.Exists(cfg)) continue;
            var savePath = ParseConfigValue(cfg, "SaveFilePath");
            if (!string.IsNullOrEmpty(savePath))
            {
                var expanded = ExpandHome(savePath);
                if (Directory.Exists(expanded))
                {
                    list.Add(new DiscoveredRoot(EmulatorKind.MelonDS, expanded, "melonDS Saves", Path.GetFileName(cfg)));
                    configFound = true;
                    break;
                }
            }
        }

        if (!configFound)
        {
            string[] defaultPaths =
            [
                Path.Combine(_home, ".local", "share", "melonDS"),
                Path.Combine(_home, ".var", "app", "net.kuribo64.melonDS", "data", "melonDS"),
                Path.Combine(_home, ".config", "melonDS")
            ];
            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    list.Add(new DiscoveredRoot(EmulatorKind.MelonDS, path, "melonDS Saves", "Default path"));
                    break;
                }
            }
        }
    }

    private void DiscoverMgba(List<DiscoveredRoot> list)
    {
        string[] configFiles =
        [
            Path.Combine(_home, ".config", "mgba", "config.ini"),
            Path.Combine(_home, ".var", "app", "io.mgba.mGBA", "config", "mgba", "config.ini")
        ];

        foreach (var cfg in configFiles)
        {
            if (!File.Exists(cfg)) continue;
            var savePath = ParseConfigValue(cfg, "savegamePath");
            if (!string.IsNullOrEmpty(savePath))
            {
                var expanded = ExpandHome(savePath);
                if (Directory.Exists(expanded))
                {
                    list.Add(new DiscoveredRoot(EmulatorKind.MGba, expanded, "mGBA Saves", "config.ini"));
                    return;
                }
            }
        }

        string[] qtFiles =
        [
            Path.Combine(_home, ".config", "mgba", "qt.ini"),
            Path.Combine(_home, ".var", "app", "io.mgba.mGBA", "config", "mgba", "qt.ini")
        ];

        foreach (var qt in qtFiles)
        {
            if (!File.Exists(qt)) continue;
            var mruDir = ParseMruDirectory(qt);
            if (!string.IsNullOrEmpty(mruDir) && Directory.Exists(mruDir))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.MGba, mruDir, $"mGBA ROMs ({Path.GetFileName(mruDir)})", "qt.ini MRU"));
                return;
            }
        }

        string[] defaultPaths =
        [
            Path.Combine(_home, ".local", "share", "mgba"),
            Path.Combine(_home, ".config", "mgba")
        ];
        foreach (var path in defaultPaths)
        {
            if (Directory.Exists(path))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.MGba, path, "mGBA Saves", "Default path"));
                break;
            }
        }
    }

    private void DiscoverDolphin(List<DiscoveredRoot> list)
    {
        string[] defaultGcPaths =
        [
            Path.Combine(_home, ".local", "share", "dolphin-emu", "GC"),
            Path.Combine(_home, ".var", "app", "org.DolphinEmu.dolphin-emu", "data", "dolphin-emu", "GC"),
            Path.Combine(_home, ".local", "share", "dolphin-emu"),
            Path.Combine(_home, ".var", "app", "org.DolphinEmu.dolphin-emu", "data", "dolphin-emu")
        ];

        foreach (var path in defaultGcPaths)
        {
            if (Directory.Exists(path))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.Dolphin, path, "Dolphin GC", "Default path"));
                break;
            }
        }
    }

    private void DiscoverThreeDs(List<DiscoveredRoot> list)
    {
        var candidates = new (string Path, string Name)[]
        {
            (Path.Combine(_home, ".local", "share", "azahar-emu", "sdmc"), "Azahar"),
            (Path.Combine(_home, ".var", "app", "org.azahar_emu.azahar", "data", "azahar-emu", "sdmc"), "Azahar (Flatpak)"),
            (Path.Combine(_home, ".local", "share", "lime3ds", "sdmc"), "Lime3DS"),
            (Path.Combine(_home, ".var", "app", "io.github.lime3ds.Lime3DS", "data", "lime3ds", "sdmc"), "Lime3DS (Flatpak)"),
            (Path.Combine(_home, ".local", "share", "citra-emu", "sdmc"), "Citra"),
            (Path.Combine(_home, ".var", "app", "org.citra_emu.citra", "data", "citra-emu", "sdmc"), "Citra (Flatpak)")
        };

        foreach (var (path, name) in candidates)
        {
            if (Directory.Exists(path))
                list.Add(new DiscoveredRoot(EmulatorKind.Azahar, path, $"{name} SDMC", "Default path"));
        }
    }

    private void DiscoverEden(List<DiscoveredRoot> list)
    {
        var candidates = new (string Path, string Name)[]
        {
            (Path.Combine(_home, ".local", "share", "eden", "nand", "user", "save"), "Eden"),
            (Path.Combine(_home, ".local", "share", "eden"), "Eden"),
            (Path.Combine(_home, ".var", "app", "dev.eden.eden_emulator", "data", "eden", "nand", "user", "save"), "Eden (Flatpak)"),
            (Path.Combine(_home, ".local", "share", "yuzu", "nand", "user", "save"), "Yuzu"),
            (Path.Combine(_home, ".local", "share", "yuzu"), "Yuzu"),
            (Path.Combine(_home, ".var", "app", "org.yuzu_emu.yuzu", "data", "yuzu", "nand", "user", "save"), "Yuzu (Flatpak)"),
            (Path.Combine(_home, ".local", "share", "suyu", "nand", "user", "save"), "Suyu"),
            (Path.Combine(_home, ".local", "share", "sudachi", "nand", "user", "save"), "Sudachi")
        };

        foreach (var (path, name) in candidates)
        {
            if (Directory.Exists(path))
                list.Add(new DiscoveredRoot(EmulatorKind.Eden, path, $"{name} Saves", "Default path"));
        }
    }

    private void DiscoverRyujinx(List<DiscoveredRoot> list)
    {
        var candidates = new (string Path, string Name)[]
        {
            (Path.Combine(_home, ".config", "Ryujinx", "bis", "user", "save"), "Ryujinx User Saves"),
            (Path.Combine(_home, ".config", "Ryujinx"), "Ryujinx"),
            (Path.Combine(_home, ".var", "app", "org.ryujinx.Ryujinx", "config", "Ryujinx", "bis", "user", "save"), "Ryujinx (Flatpak)"),
            (Path.Combine(_home, ".var", "app", "io.github.ryubing.Ryujinx", "config", "Ryujinx", "bis", "user", "save"), "Ryujinx (Flatpak)")
        };

        foreach (var (path, name) in candidates)
        {
            if (Directory.Exists(path))
                list.Add(new DiscoveredRoot(EmulatorKind.Ryujinx, path, name, "Default path"));
        }
    }

    private void DiscoverEmuDeckAndRetroDeck(List<DiscoveredRoot> list)
    {
        // 1. EmuDeck settings.sh
        var emudeckSettings = Path.Combine(_home, "emudeck", "settings.sh");
        if (File.Exists(emudeckSettings))
        {
            var romsPath = ParseShellVariable(emudeckSettings, "romsPath");
            if (!string.IsNullOrEmpty(romsPath))
            {
                var expanded = ExpandHome(romsPath);
                if (Directory.Exists(expanded))
                    list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, expanded, "EmuDeck ROMs", "settings.sh"));
            }
        }

        // 2. Default EmuDeck paths
        var defaultRoms = Path.Combine(_home, "Emulation", "roms");
        if (Directory.Exists(defaultRoms))
            list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, defaultRoms, "EmuDeck ROMs", "EmuDeck"));

        var defaultRaSaves = Path.Combine(_home, "Emulation", "saves", "retroarch", "saves");
        if (Directory.Exists(defaultRaSaves))
            list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, defaultRaSaves, "EmuDeck RetroArch Saves", "EmuDeck"));

        // 3. RetroDECK
        string[] retrodeckPaths =
        [
            Path.Combine(_home, "retrodeck", "roms"),
            Path.Combine(_home, "RetroDeck", "roms"),
            Path.Combine(_home, ".var", "app", "net.retrodeck.retrodeck", "data", "roms")
        ];
        foreach (var rPath in retrodeckPaths)
        {
            if (Directory.Exists(rPath))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, rPath, "RetroDECK ROMs", "RetroDECK"));
                break;
            }
        }
    }

    private void DiscoverMountedDrives(List<DiscoveredRoot> list)
    {
        if (!Directory.Exists(_media)) return;

        try
        {
            // Traverse up to 2 levels under mediaPrefix (e.g. /run/media/<user>/<drivename>)
            var users = Directory.GetDirectories(_media);
            foreach (var userDir in users)
            {
                var drives = Directory.GetDirectories(userDir);
                foreach (var drive in drives)
                {
                    var driveName = Path.GetFileName(drive);
                    string[] relativeRomRoots =
                    [
                        Path.Combine(drive, "Emulation", "roms"),
                        Path.Combine(drive, "Game Setups", "Emulation", "roms"),
                        Path.Combine(drive, "roms"),
                    ];

                    foreach (var romRoot in relativeRomRoots)
                    {
                        if (Directory.Exists(romRoot))
                        {
                            list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, romRoot, $"ROMs ({driveName})", "Mounted Drive"));
                            break;
                        }
                    }

                    var raSaves = Path.Combine(drive, "Emulation", "saves", "retroarch", "saves");
                    if (Directory.Exists(raSaves))
                        list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, raSaves, $"RetroArch Saves ({driveName})", "Mounted Drive"));
                }
            }
        }
        catch
        {
            // Inaccessible media mount
        }
    }

    private static IReadOnlyList<DiscoveredRoot> Deduplicate(List<DiscoveredRoot> list)
    {
        var result = new List<DiscoveredRoot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in list)
        {
            var realPath = PosixSaveFileAccess.ResolveFinalTarget(item.TreeId);
            if (seen.Add(realPath))
            {
                result.Add(item with { TreeId = realPath });
            }
        }

        return result;
    }

    private string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(_home, path.Length > 2 ? path[2..] : "");
        return path;
    }

    public static string? ParseConfigValue(string configPath, string key)
    {
        try
        {
            if (!File.Exists(configPath)) return null;
            var pattern = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=\s*[""']?([^""'\r\n]+)[""']?", RegexOptions.IgnoreCase);
            foreach (var line in File.ReadLines(configPath))
            {
                var match = pattern.Match(line);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    public static string? ParseMruDirectory(string qtIniPath)
    {
        try
        {
            if (!File.Exists(qtIniPath)) return null;
            var inMru = false;
            foreach (var line in File.ReadLines(qtIniPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Equals("[mru]", StringComparison.OrdinalIgnoreCase))
                {
                    inMru = true;
                    continue;
                }
                if (inMru)
                {
                    if (trimmed.StartsWith('[')) break;
                    var match = Regex.Match(trimmed, @"^\d+=(.+)$");
                    if (match.Success)
                    {
                        var filePath = match.Groups[1].Value.Trim();
                        var dir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir)) return dir;
                    }
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    public static string? ParseShellVariable(string shellScriptPath, string variableName)
    {
        try
        {
            if (!File.Exists(shellScriptPath)) return null;
            var pattern = new Regex(@"^\s*(?:export\s+)?" + Regex.Escape(variableName) + @"\s*=\s*[""']?([^""'\r\n]+)[""']?", RegexOptions.IgnoreCase);
            foreach (var line in File.ReadLines(shellScriptPath))
            {
                var match = pattern.Match(line);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }
}
