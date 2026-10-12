using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Platform-specific update installer: manages system permissions and triggering the installation.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>True if this platform can install updates locally (e.g. Android PackageInstaller).</summary>
    bool CanInstallUpdates { get; }

    /// <summary>Opens the platform's settings screen to grant package installation permissions.</summary>
    void OpenInstallPermissionSettings();

    /// <summary>Downloads and initiates installation or opens the release page.</summary>
    Task<AppUpdateInstallResult> DownloadAndInstallAsync(
        AvailableAppUpdate update,
        Action<long, long> onProgress,
        CancellationToken cancellationToken);
}
