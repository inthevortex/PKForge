namespace PKForge.App.Services;

/// <summary>User-facing wording that differs between the Android and desktop builds, kept in one place.</summary>
internal static class PlatformText
{
#if ANDROID
    /// <summary>Appended to an emulator's folder-linking guidance.</summary>
    public const string FolderAccessHint = " If Android does not offer access to the folder, use Single save file with an exported save.";

    public const string LogsAction = "Share logs";

    public const string LogsDetail = "Crash reports and recent activity, to send us when something goes wrong.";

    public const string CrashReportAction = "Share report";

    public const string ParkWidgetHelp = "Add PKForge Poképark in Cocoon's widget picker. Resize it to give your Pokémon more room. The full meadow shows a quiet, living moment; tap it to return here. Your residents are saved between visits.";
#else
    /// <summary>Appended to an emulator's folder-linking guidance.</summary>
    public const string FolderAccessHint = "";

    public const string LogsAction = "Export logs";

    public const string LogsDetail = "Crash reports and recent activity, to save or send when something goes wrong.";

    public const string CrashReportAction = "Export report";

    public const string ParkWidgetHelp = "Home screen widgets are supported on mobile devices. On desktop, enjoy the meadow right here in PKForge! Your residents are saved between visits.";
#endif
}
