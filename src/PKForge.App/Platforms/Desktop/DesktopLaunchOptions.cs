using System.Globalization;

namespace PKForge.App.Platforms.Desktop;

public enum DesktopWindowMode
{
    Fullscreen,
    Windowed,
}

/// <summary>A window size in physical pixels.</summary>
public readonly record struct LaunchWindowSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// The desktop head's command line: <c>--fullscreen</c> (the default), <c>--windowed</c>,
/// <c>--size WxH</c> (physical pixels; implies windowed unless <c>--fullscreen</c> is given)
/// and <c>--wayland</c>. Problems become <see cref="Warnings"/> instead of failing the launch.
/// </summary>
public sealed record DesktopLaunchOptions(
    DesktopWindowMode Mode,
    LaunchWindowSize? Size,
    bool Wayland,
    IReadOnlyList<string> Warnings)
{
    public const int MinSide = 320;
    public const int MaxSide = 16384;

    public static DesktopLaunchOptions Default { get; } = new(DesktopWindowMode.Fullscreen, null, false, []);

    public static DesktopLaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        DesktopWindowMode? mode = null;
        LaunchWindowSize? size = null;
        var wayland = false;
        var warnings = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--fullscreen":
                    mode = DesktopWindowMode.Fullscreen;
                    break;
                case "--windowed":
                    mode = DesktopWindowMode.Windowed;
                    break;
                case "--wayland":
                    wayland = true;
                    break;
                case "--size":
                    if (i + 1 >= args.Count)
                    {
                        warnings.Add("--size needs a value such as 1280x800; ignoring it.");
                        break;
                    }
                    size = ParseSizeOrWarn(args[++i], warnings) ?? size;
                    break;
                default:
                    if (arg.StartsWith("--size=", StringComparison.Ordinal))
                        size = ParseSizeOrWarn(arg["--size=".Length..], warnings) ?? size;
                    else if (arg.StartsWith('-'))
                        warnings.Add($"Unknown launch option '{arg}'; ignoring it.");
                    break;
            }
        }

        var resolved = mode ?? (size is null ? DesktopWindowMode.Fullscreen : DesktopWindowMode.Windowed);
        return new DesktopLaunchOptions(resolved, size, wayland, warnings);
    }

    /// <summary>Reads <c>WxH</c> (or <c>WXH</c>) with both sides between <see cref="MinSide"/> and <see cref="MaxSide"/>.</summary>
    public static LaunchWindowSize? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('x', 'X');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)) return null;
        if (width is < MinSide or > MaxSide || height is < MinSide or > MaxSide) return null;
        return new LaunchWindowSize(width, height);
    }

    private static LaunchWindowSize? ParseSizeOrWarn(string text, List<string> warnings)
    {
        var size = ParseSize(text);
        if (size is null)
            warnings.Add($"--size '{text}' is not WxH with each side between {MinSide} and {MaxSide}; ignoring it.");
        return size;
    }
}
