using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopLauncher : ILauncher
{
    public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(uri != null);

    public Task<bool> OpenAsync(Uri uri)
    {
        if (uri is null) return Task.FromResult(false);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch
        {
            try
            {
                Process.Start("xdg-open", uri.AbsoluteUri);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }

    public Task<bool> OpenAsync(OpenFileRequest request)
    {
        var path = request?.File?.FullPath;
        if (string.IsNullOrEmpty(path)) return Task.FromResult(false);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch
        {
            try
            {
                Process.Start("xdg-open", path);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }

    public Task<bool> TryOpenAsync(Uri uri) => OpenAsync(uri);
}
