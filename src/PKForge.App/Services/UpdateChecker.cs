using System.Reflection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Checks GitHub Releases against installed version and platform-specific assets.
/// </summary>
public sealed class UpdateChecker
{
    private const string SkippedVersionKey = "update_skipped_version";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromMinutes(10) };

    public string RepositorySlug { get; }

    public string SkippedVersion => Preferences.Default.Get(SkippedVersionKey, "");

    public UpdateChecker(string? repositorySlug = null)
    {
        RepositorySlug = repositorySlug
            ?? typeof(UpdateChecker).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "PKForgeUpdateRepository")?.Value
            ?? "sofianeelhor/PKForge";
    }

    public void SetSkippedVersion(string version) =>
        Preferences.Default.Set(SkippedVersionKey, version);

    public async Task<AppUpdateCheck> CheckAsync(
        Func<string, bool> assetPredicate,
        string? missingAssetError = null,
        CancellationToken cancellationToken = default)
    {
        if (AppInfo.Current.PackageName?.EndsWith(".debug", StringComparison.OrdinalIgnoreCase) == true)
            return new AppUpdateCheck(false, null, "Diagnostics builds are updated manually.");

        using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        var url = $"https://api.github.com/repos/{RepositorySlug}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"PKForge/{AppInfo.Current.VersionString} (+https://github.com/{RepositorySlug})");

        using var response = await Http.SendAsync(request, checkTimeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(checkTimeout.Token).ConfigureAwait(false);
        return AppUpdateRules.ParseRelease(json, AppInfo.Current.VersionString, assetPredicate, missingAssetError);
    }
}
