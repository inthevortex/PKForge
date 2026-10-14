using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using PKForge.App.Services;

namespace PKForge.App.Platforms.Android;

public sealed class AndroidAppPlatform : IAppPlatform
{
    public bool CanSelfInstallUpdates => true;

    public async Task ExportFilesAsync(string title, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        if (paths.Count == 1)
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = title,
                File = new ShareFile(paths[0])
            });
        }
        else
        {
            await Share.Default.RequestAsync(new ShareMultipleFilesRequest
            {
                Title = title,
                Files = paths.Select(p => new ShareFile(p)).ToList()
            });
        }
    }

    public void Haptic(HapticKind kind)
    {
        try
        {
            var feedbackType = kind switch
            {
                HapticKind.LongPress => HapticFeedbackType.LongPress,
                _ => HapticFeedbackType.Click
            };
            HapticFeedback.Default.Perform(feedbackType);
        }
        catch { }
    }
}
