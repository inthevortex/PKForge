using System.Diagnostics;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// What the desktop launcher hands to <c>xdg-open</c>: web links and local folders only, so a
/// link can never name a program to run.
/// </summary>
internal static class LaunchTargets
{
    /// <summary>The argument to open <paramref name="uri"/> with, or null when it is not a web link or an existing folder.</summary>
    public static string? For(Uri? uri, Func<string, bool> directoryExists)
    {
        if (uri is null || !uri.IsAbsoluteUri) return null;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) return uri.AbsoluteUri;
        return uri.IsFile && directoryExists(uri.LocalPath) ? uri.LocalPath : null;
    }

    /// <summary>Runs <c>xdg-open</c> directly on <paramref name="target"/>, passed as one argument.</summary>
    public static ProcessStartInfo XdgOpen(string target)
    {
        var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(target);
        return info;
    }
}
