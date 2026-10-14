using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace PKForge.App.Platforms.Desktop;

/// <summary>Opens web links, folders and files with <c>xdg-open</c>; see <see cref="LaunchTargets"/> for what a link may name.</summary>
public sealed class DesktopLauncher : ILauncher
{
    public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(LaunchTargets.For(uri, Directory.Exists) is not null);

    public Task<bool> OpenAsync(Uri uri) =>
        Task.FromResult(LaunchTargets.For(uri, Directory.Exists) is { } target && XdgOpen(target));

    public Task<bool> OpenAsync(OpenFileRequest request)
    {
        var path = request?.File?.FullPath;
        if (string.IsNullOrEmpty(path)) return Task.FromResult(false);
        return Task.FromResult(XdgOpen(Path.GetFullPath(path)));
    }

    public Task<bool> TryOpenAsync(Uri uri) => OpenAsync(uri);

    private static bool XdgOpen(string target)
    {
        try
        {
            using var process = Process.Start(LaunchTargets.XdgOpen(target));
            return process is not null;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
