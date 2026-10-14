using Microsoft.Maui.ApplicationModel;
using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Coordinates checking for application updates and triggering installation across platforms.
/// </summary>
public sealed class AppUpdateService
{
    private readonly UpdateChecker _checker;
    private readonly IUpdateInstaller _installer;

    public AppUpdateService(UpdateChecker checker, IUpdateInstaller installer)
    {
        _checker = checker;
        _installer = installer;
    }

    public string SkippedVersion => _checker.SkippedVersion;

    public static bool CanInstallUpdates() =>
        IPlatformApplication.Current?.Services.GetService<IUpdateInstaller>()?.CanInstallUpdates ?? false;

    public static void OpenInstallPermissionSettings() =>
        IPlatformApplication.Current?.Services.GetService<IUpdateInstaller>()?.OpenInstallPermissionSettings();

    public Task<AppUpdateCheck> CheckAsync(CancellationToken cancellationToken = default) =>
        _checker.CheckAsync(
            name => name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase),
            "The latest release did not include an APK.",
            cancellationToken);

    public bool ShouldPromptAutomatically(AvailableAppUpdate update) =>
        AppUpdateRules.ShouldPromptAutomatically(AppInfo.Current.VersionString, update.Version, SkippedVersion);

    public void DontRemindMe(AvailableAppUpdate update) =>
        _checker.SetSkippedVersion(update.Version);

    public Task<AppUpdateInstallResult> DownloadAndInstallAsync(
        AvailableAppUpdate update,
        Action<long, long> onProgress,
        CancellationToken cancellationToken) =>
        _installer.DownloadAndInstallAsync(update, onProgress, cancellationToken);

#if ANDROID
    internal static void StartAndroidInstall(string apkPath, bool askUser = false) =>
        Platforms.Android.AndroidUpdateInstaller.StartAndroidInstall(apkPath, askUser);
#endif
}
