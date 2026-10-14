using Microsoft.Maui.ApplicationModel;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopAppInfo : IAppInfo
{
    private static readonly AppVersionInfo AppVersion = AppVersionInfo.FromAssembly(typeof(DesktopAppInfo).Assembly);

    public string PackageName => "org.pkforge.app";
    public string Name => "PKForge";
    public string VersionString => AppVersion.VersionString;
    public Version Version => AppVersion.Version;
    public string BuildString => AppVersion.BuildString;

    public void ShowSettingsUI() { }

    public AppTheme RequestedTheme => AppTheme.Dark;
    public AppPackagingModel PackagingModel => AppPackagingModel.Packaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
}
