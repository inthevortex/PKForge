using Microsoft.Maui.ApplicationModel;
using PKForge.App.Services;
using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopUpdateInstaller : IUpdateInstaller
{
    public bool CanInstallUpdates => false;

    public void OpenInstallPermissionSettings() { }

    public async Task<AppUpdateInstallResult> DownloadAndInstallAsync(
        AvailableAppUpdate update,
        Action<long, long> onProgress,
        CancellationToken cancellationToken)
    {
        await Launcher.OpenAsync(update.ReleaseUrl).ConfigureAwait(false);
        return AppUpdateInstallResult.ReleasePageOpened;
    }
}
