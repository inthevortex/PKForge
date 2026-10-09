using Microsoft.Maui.ApplicationModel;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopAppInfo : IAppInfo
{
    public string PackageName => "org.pkforge.app";
    public string Name => "PKForge";
    public string VersionString => "3.1.0";
    public Version Version => new(3, 1, 0);
    public string BuildString => "310";

    public void ShowSettingsUI() { }

    public AppTheme RequestedTheme => AppTheme.Dark;
    public AppPackagingModel PackagingModel => AppPackagingModel.Packaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
}
