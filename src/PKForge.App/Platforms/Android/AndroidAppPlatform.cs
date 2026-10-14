using Microsoft.Maui.ApplicationModel.DataTransfer;
using PKForge.App.Services;

namespace PKForge.App.Platforms.Android;

public sealed class AndroidAppPlatform : IAppPlatform
{
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
}
