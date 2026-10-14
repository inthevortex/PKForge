using System.Reflection;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// The app version stamped into an assembly by the build: the display version from
/// <c>ApplicationDisplayVersion</c> and the build number from <c>ApplicationVersion</c>.
/// </summary>
public sealed record AppVersionInfo(string VersionString, Version Version, string BuildString)
{
    public const string BuildMetadataKey = "ApplicationVersion";

    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var build = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == BuildMetadataKey)?.Value
            ?? assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        return Create(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version,
            build);
    }

    public static AppVersionInfo Create(string? informationalVersion, Version? assemblyVersion, string? build)
    {
        var display = StripBuildMetadata(informationalVersion);
        if (string.IsNullOrEmpty(display))
            display = assemblyVersion is null ? "0.0.0" : assemblyVersion.ToString(3);

        var version = ParseNumeric(display) ?? assemblyVersion ?? new Version(0, 0, 0);
        return new AppVersionInfo(display, version, string.IsNullOrWhiteSpace(build) ? "" : build.Trim());
    }

    // SemVer build metadata ("+<commit>") is appended by the SDK and is not part of the version.
    private static string StripBuildMetadata(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "";
        var plus = informationalVersion.IndexOf('+');
        return (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
    }

    private static Version? ParseNumeric(string display)
    {
        var dash = display.IndexOf('-');
        var core = dash >= 0 ? display[..dash] : display;
        return Version.TryParse(core, out var version) ? version : null;
    }
}
