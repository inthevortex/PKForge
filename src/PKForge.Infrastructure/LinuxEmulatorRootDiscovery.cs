using System.Text.Json;
using System.Text.RegularExpressions;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// Automatically discovers emulator save roots and ROM directories across Linux desktop setups
/// (native packages, Flatpaks, EmuDeck, RetroDECK, and external drives).
/// </summary>
public sealed class LinuxEmulatorRootDiscovery : IEmulatorRootDiscovery
{
    private const int MaxRetroDeckConfigBytes = 1024 * 1024;

    private readonly string _home;
    private readonly string _media;
    private readonly string _configHome;
    private readonly string _dataHome;

    /// <param name="homeDirectory">The user's home; defaults to the current user's.</param>
    /// <param name="mediaPrefix">Where removable drives are mounted; defaults to /run/media.</param>
    /// <param name="environment">Reads XDG_CONFIG_HOME and XDG_DATA_HOME; defaults to the process environment.</param>
    public LinuxEmulatorRootDiscovery(string? homeDirectory = null, string? mediaPrefix = null,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        _home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _media = mediaPrefix ?? "/run/media";
        _configHome = XdgDirectory(environment("XDG_CONFIG_HOME")) ?? Path.Combine(_home, ".config");
        _dataHome = XdgDirectory(environment("XDG_DATA_HOME")) ?? Path.Combine(_home, ".local", "share");
    }

    /// <summary>The XDG base directory spec ignores a relative path, as if the variable were unset.</summary>
    private static string? XdgDirectory(string? value) =>
        !string.IsNullOrEmpty(value) && Path.IsPathRooted(value) ? value : null;

    /// <summary>
    /// Probes on the thread pool: a spun-down disk or a hung mount under /run/media can stall
    /// each file-system call for seconds.
    /// </summary>
    public ValueTask<IReadOnlyList<DiscoveredRoot>> DiscoverRootsAsync(CancellationToken cancellationToken = default) =>
        new(Task.Run(() => Discover(cancellationToken), cancellationToken));

    private IReadOnlyList<DiscoveredRoot> Discover(CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        void Probe(Action<List<DiscoveredRoot>> discover, bool userFacing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = new List<DiscoveredRoot>();
            discover(found);
            candidates.AddRange(found.Select(root => new Candidate(root, userFacing)));
        }

        Probe(DiscoverRetroArch, userFacing: false);
        Probe(DiscoverMelonDs, userFacing: false);
        Probe(DiscoverMgba, userFacing: false);
        Probe(DiscoverDolphin, userFacing: false);
        Probe(DiscoverThreeDs, userFacing: false);
        Probe(DiscoverEden, userFacing: false);
        Probe(DiscoverRyujinx, userFacing: false);
        Probe(DiscoverEmuDeckAndRetroDeck, userFacing: true);
        Probe(list => DiscoverMountedDrives(list, cancellationToken), userFacing: false);
        cancellationToken.ThrowIfCancellationRequested();

        return [.. Deduplicate(candidates).Select(root => root with { IsRemovable = IsOnRemovableMedia(root.TreeId) })];
    }

    /// <summary>Whether the folder is under the removable-media mount point, directly or through a symlink anywhere along its path.</summary>
    private bool IsOnRemovableMedia(string path) =>
        IsUnder(path, _media) || IsUnder(ResolveLinks(path), _media);

    /// <summary>The path with a symlink at any of its components replaced by that link's final target.</summary>
    private static string ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(full) ?? "";
        foreach (var part in full[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(resolved, part);
            try
            {
                next = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // An unreadable component stays as named.
            }
            resolved = next;
        }
        return resolved;
    }

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <param name="UserFacing">Found under an EmuDeck or RetroDECK folder, the path those users browse.</param>
    private readonly record struct Candidate(DiscoveredRoot Root, bool UserFacing);

    private void DiscoverRetroArch(List<DiscoveredRoot> list)
    {
        string[] configFiles =
        [
            Path.Combine(_configHome, "retroarch", "retroarch.cfg"),
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
                Path.Combine(_configHome, "retroarch", "saves"),
                Path.Combine(_home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "saves"),
                Path.Combine(_configHome, "retroarch")
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
            Path.Combine(_configHome, "melonDS", "melonDS.toml"),
            Path.Combine(_configHome, "melonDS", "melonDS.ini"),
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
                Path.Combine(_dataHome, "melonDS"),
                Path.Combine(_home, ".var", "app", "net.kuribo64.melonDS", "data", "melonDS"),
                Path.Combine(_configHome, "melonDS")
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
            Path.Combine(_configHome, "mgba", "config.ini"),
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
                    list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, expanded, "mGBA Saves", "config.ini"));
                    return;
                }
            }
        }

        string[] qtFiles =
        [
            Path.Combine(_configHome, "mgba", "qt.ini"),
            Path.Combine(_home, ".var", "app", "io.mgba.mGBA", "config", "mgba", "qt.ini")
        ];

        foreach (var qt in qtFiles)
        {
            if (!File.Exists(qt)) continue;
            var mruDir = ParseMruDirectory(qt);
            if (!string.IsNullOrEmpty(mruDir) && Directory.Exists(mruDir))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, mruDir, $"mGBA ROMs ({Path.GetFileName(mruDir)})", "qt.ini MRU"));
                return;
            }
        }

        string[] defaultPaths =
        [
            Path.Combine(_dataHome, "mgba"),
            Path.Combine(_home, ".var", "app", "io.mgba.mGBA", "data", "mgba"),
            Path.Combine(_configHome, "mgba")
        ];
        foreach (var path in defaultPaths)
        {
            if (Directory.Exists(path))
            {
                list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, path, "mGBA Saves", "Default path"));
                break;
            }
        }
    }

    private void DiscoverDolphin(List<DiscoveredRoot> list)
    {
        string[] defaultGcPaths =
        [
            Path.Combine(_dataHome, "dolphin-emu", "GC"),
            Path.Combine(_home, ".var", "app", "org.DolphinEmu.dolphin-emu", "data", "dolphin-emu", "GC"),
            Path.Combine(_dataHome, "dolphin-emu"),
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

    // Each emulator's current Flatpak id first, then other ids an install may carry: a missing
    // folder costs one failed stat.
    private static readonly string[] AzaharFlatpakIds = ["org.azahar_emu.Azahar", "org.azahar_emu.azahar"];
    private static readonly string[] EdenFlatpakIds = ["dev.eden_emu.eden", "dev.eden.eden_emulator"];
    private static readonly string[] RyujinxFlatpakIds = ["org.ryujinx.Ryujinx", "io.github.ryubing.Ryujinx"];

    private void DiscoverThreeDs(List<DiscoveredRoot> list)
    {
        AddAzahar(list, Path.Combine(_configHome, "azahar-emu"), Path.Combine(_dataHome, "azahar-emu"), "Azahar");
        foreach (var id in AzaharFlatpakIds)
        {
            var app = Path.Combine(_home, ".var", "app", id);
            AddAzahar(list, Path.Combine(app, "config", "azahar-emu"), Path.Combine(app, "data", "azahar-emu"), "Azahar (Flatpak)");
        }

        var candidates = new (string Path, string Name)[]
        {
            (Path.Combine(_dataHome, "lime3ds", "sdmc"), "Lime3DS"),
            (Path.Combine(_home, ".var", "app", "io.github.lime3ds.Lime3DS", "data", "lime3ds", "sdmc"), "Lime3DS (Flatpak)"),
            (Path.Combine(_dataHome, "citra-emu", "sdmc"), "Citra"),
            (Path.Combine(_home, ".var", "app", "org.citra_emu.citra", "data", "citra-emu", "sdmc"), "Citra (Flatpak)")
        };

        foreach (var (path, name) in candidates)
        {
            if (Directory.Exists(path))
                list.Add(new DiscoveredRoot(EmulatorKind.Azahar, path, $"{name} SDMC", "Default path"));
        }
    }

    /// <summary>
    /// A custom storage location set in Azahar's settings replaces the default SD card folder,
    /// which then holds nothing current and is not offered.
    /// </summary>
    private void AddAzahar(List<DiscoveredRoot> list, string configDirectory, string dataDirectory, string name)
    {
        var custom = ParseAzaharCustomSdmc(Path.Combine(configDirectory, "qt-config.ini"));
        var sdmc = custom is null ? Path.Combine(dataDirectory, "sdmc") : ExpandHome(custom);
        if (Directory.Exists(sdmc))
            list.Add(new DiscoveredRoot(EmulatorKind.Azahar, sdmc, $"{name} SDMC", custom is null ? "Default path" : "qt-config.ini"));
    }

    private static string? ParseAzaharCustomSdmc(string qtConfigPath)
    {
        const string section = "Data Storage";
        var useCustom = ParseIniValue(qtConfigPath, section, "use_custom_storage");
        if (!bool.TryParse(useCustom, out var enabled) || !enabled) return null;
        var sdmc = ParseIniValue(qtConfigPath, section, "sdmc_directory");
        return string.IsNullOrEmpty(sdmc) ? null : Path.TrimEndingDirectorySeparator(sdmc);
    }

    /// <summary>
    /// One root per install: its save folder when present, otherwise its data folder, which
    /// the scanner walks down to the saves itself.
    /// </summary>
    private void DiscoverEden(List<DiscoveredRoot> list)
    {
        var installs = new List<(string DataDirectory, string Name)> { (Path.Combine(_dataHome, "eden"), "Eden") };
        installs.AddRange(EdenFlatpakIds.Select(id => (Path.Combine(_home, ".var", "app", id, "data", "eden"), "Eden (Flatpak)")));
        installs.Add((Path.Combine(_dataHome, "yuzu"), "Yuzu"));
        installs.Add((Path.Combine(_home, ".var", "app", "org.yuzu_emu.yuzu", "data", "yuzu"), "Yuzu (Flatpak)"));
        installs.Add((Path.Combine(_dataHome, "suyu"), "Suyu"));
        installs.Add((Path.Combine(_dataHome, "sudachi"), "Sudachi"));

        foreach (var (dataDirectory, name) in installs)
        {
            var saves = Path.Combine(dataDirectory, "nand", "user", "save");
            var root = Directory.Exists(saves) ? saves : dataDirectory;
            if (Directory.Exists(root))
                list.Add(new DiscoveredRoot(EmulatorKind.Eden, root, $"{name} Saves", "Default path"));
        }
    }

    /// <summary>
    /// One root per install, at its data folder rather than bis/user/save: the title index the
    /// scanner names games from lives beside the saves, under bis/system.
    /// </summary>
    private void DiscoverRyujinx(List<DiscoveredRoot> list)
    {
        var installs = new List<(string Path, string Name)> { (Path.Combine(_configHome, "Ryujinx"), "Ryujinx") };
        installs.AddRange(RyujinxFlatpakIds.Select(id => (Path.Combine(_home, ".var", "app", id, "config", "Ryujinx"), "Ryujinx (Flatpak)")));

        foreach (var (path, name) in installs)
        {
            if (Directory.Exists(path))
                list.Add(new DiscoveredRoot(EmulatorKind.Ryujinx, path, name, "Default path"));
        }
    }

    private void DiscoverEmuDeckAndRetroDeck(List<DiscoveredRoot> list)
    {
        string[] emudeckSettingsFiles =
        [
            Path.Combine(_configHome, "EmuDeck", "settings.sh"),
            Path.Combine(_home, ".config", "EmuDeck", "settings.sh"),
            Path.Combine(_home, "emudeck", "settings.sh")
        ];
        var emudeckSettings = emudeckSettingsFiles.FirstOrDefault(File.Exists);
        if (emudeckSettings is not null)
        {
            var romsPath = ParseShellVariable(emudeckSettings, "romsPath");
            if (!string.IsNullOrEmpty(romsPath))
            {
                var expanded = ExpandHome(romsPath);
                if (Directory.Exists(expanded))
                    list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, expanded, "EmuDeck ROMs", "settings.sh"));
            }

            var savesPath = ParseShellVariable(emudeckSettings, "savesPath");
            if (!string.IsNullOrEmpty(savesPath))
            {
                var raSaves = Path.Combine(ExpandHome(savesPath), "retroarch", "saves");
                if (Directory.Exists(raSaves))
                    list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, raSaves, "EmuDeck RetroArch Saves", "settings.sh"));
            }
        }

        var defaultRoms = Path.Combine(_home, "Emulation", "roms");
        if (Directory.Exists(defaultRoms))
            list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, defaultRoms, "EmuDeck ROMs", "EmuDeck"));

        var defaultRaSaves = Path.Combine(_home, "Emulation", "saves", "retroarch", "saves");
        if (Directory.Exists(defaultRaSaves))
            list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, defaultRaSaves, "EmuDeck RetroArch Saves", "EmuDeck"));

        DiscoverRetroDeck(list);
    }

    /// <summary>
    /// RetroDECK records its folders in retrodeck.json; the default layout is probed only when
    /// that file names no existing folder.
    /// </summary>
    private void DiscoverRetroDeck(List<DiscoveredRoot> list)
    {
        var config = Path.Combine(_home, ".var", "app", "net.retrodeck.retrodeck", "config", "retrodeck", "retrodeck.json");
        var (configuredRoms, configuredSaves) = ParseRetroDeckPaths(config);

        var roms = configuredRoms is null ? null : ExpandHome(configuredRoms);
        if (roms is not null && Directory.Exists(roms))
            list.Add(new DiscoveredRoot(EmulatorKind.RomFolder, roms, "RetroDECK ROMs", "retrodeck.json"));
        else
            AddFirstExisting(list, EmulatorKind.RomFolder, "RetroDECK ROMs",
                Path.Combine(_home, "retrodeck", "roms"),
                Path.Combine(_home, "RetroDeck", "roms"),
                Path.Combine(_home, ".var", "app", "net.retrodeck.retrodeck", "data", "roms"));

        var saves = configuredSaves is null ? null : ExpandHome(configuredSaves);
        if (saves is not null && Directory.Exists(saves))
            list.Add(new DiscoveredRoot(EmulatorKind.RetroArch, saves, "RetroDECK Saves", "retrodeck.json"));
        else
            AddFirstExisting(list, EmulatorKind.RetroArch, "RetroDECK Saves",
                Path.Combine(_home, "retrodeck", "saves"),
                Path.Combine(_home, "RetroDeck", "saves"));
    }

    private static void AddFirstExisting(List<DiscoveredRoot> list, EmulatorKind kind, string displayName, params string[] paths)
    {
        var found = paths.FirstOrDefault(Directory.Exists);
        if (found is not null)
            list.Add(new DiscoveredRoot(kind, found, displayName, "RetroDECK"));
    }

    private static (string? Roms, string? Saves) ParseRetroDeckPaths(string configPath)
    {
        try
        {
            if (!LocalTreeSource.IsRegularFile(configPath) || new FileInfo(configPath).Length > MaxRetroDeckConfigBytes)
                return (null, null);
            using var document = JsonDocument.Parse(File.ReadAllBytes(configPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("paths", out var paths)
                || paths.ValueKind != JsonValueKind.Object)
                return (null, null);
            return (StringProperty(paths, "roms_path"), StringProperty(paths, "saves_path"));
        }
        catch
        {
            return (null, null);
        }

        static string? StringProperty(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } text
                ? text
                : null;
    }

    private void DiscoverMountedDrives(List<DiscoveredRoot> list, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_media)) return;

        try
        {
            void ProbeDrive(string drive)
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

            // Traverse 1 level (e.g. SteamOS /run/media/mmcblk0p1 or direct mounts)
            // and 2 levels (e.g. standard Linux /run/media/<user>/<drivename>)
            var topLevel = Directory.GetDirectories(_media);
            foreach (var dir in topLevel)
            {
                if (cancellationToken.IsCancellationRequested) return;
                ProbeDrive(dir);

                try
                {
                    var subDrives = Directory.GetDirectories(dir);
                    foreach (var subDrive in subDrives)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        ProbeDrive(subDrive);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Keeps one root per folder, however many paths reach it. An emulator kind beats
    /// <see cref="EmulatorKind.RomFolder"/>, which would list the same saves a second time;
    /// then an EmuDeck or RetroDECK path beats the emulator's own native or Flatpak path;
    /// then the first one found wins. The kept root keeps the path it was found under: that
    /// path is the folder's identity for backups and save protection, so it must match what
    /// the user would pick by hand.
    /// </summary>
    private static IReadOnlyList<DiscoveredRoot> Deduplicate(List<Candidate> candidates)
    {
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var folder = FolderKey(candidates[i].Root.TreeId);
            if (!kept.TryGetValue(folder, out var current) || Preference(candidates[i]) > Preference(candidates[current]))
                kept[folder] = i;
        }

        return [.. kept.Values.Order().Select(i => candidates[i].Root)];
    }

    private static int Preference(Candidate candidate) =>
        (candidate.Root.Kind == EmulatorKind.RomFolder ? 0 : 2) + (candidate.UserFacing ? 1 : 0);

    /// <summary>The folder's device and inode, which no symlink anywhere along its path can disguise.</summary>
    private static string FolderKey(string path) =>
        LocalTreeSource.TryGetInode(path, out var dev, out var ino)
            ? $"{dev}:{ino}"
            : PosixSaveFileAccess.ResolveFinalTarget(path);

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
            if (!LocalTreeSource.IsRegularFile(configPath)) return null;
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
        }
        return null;
    }

    /// <summary>
    /// Reads one key of one section from a Qt settings file. Section names are matched after
    /// Qt's percent-encoding is undone ("Data%20Storage"), and a key's "\default" twin is
    /// a different key.
    /// </summary>
    internal static string? ParseIniValue(string iniPath, string section, string key)
    {
        try
        {
            if (!LocalTreeSource.IsRegularFile(iniPath)) return null;
            var inSection = false;
            foreach (var line in File.ReadLines(iniPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inSection = string.Equals(Uri.UnescapeDataString(trimmed[1..^1]), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                var equals = trimmed.IndexOf('=');
                if (equals <= 0 || !string.Equals(trimmed[..equals].Trim(), key, StringComparison.Ordinal)) continue;
                var value = trimmed[(equals + 1)..].Trim();
                return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
            }
        }
        catch
        {
        }
        return null;
    }

    public static string? ParseMruDirectory(string qtIniPath)
    {
        try
        {
            if (!LocalTreeSource.IsRegularFile(qtIniPath)) return null;
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
        }
        return null;
    }

    public static string? ParseShellVariable(string shellScriptPath, string variableName)
    {
        try
        {
            if (!LocalTreeSource.IsRegularFile(shellScriptPath)) return null;
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
        }
        return null;
    }
}
