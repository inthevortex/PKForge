using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class AppUpdateRulesTests
{
    [Theory]
    [InlineData("1.3.0", "v1.3.1", true)]
    [InlineData("1.3.0", "v1.10.0", true)]
    [InlineData("1.3.0", "v1.3.0", false)]
    [InlineData("1.3.0", "v1.2.9", false)]
    [InlineData("3.0.3", "v3.1.0", true)] // the 3.1 release, as a 3.0.3 install sees it
    [InlineData("3.1.0", "v3.0.3", false)]
    [InlineData("1.4.0", "v1.4.0-beta+build.1", false)]
    [InlineData("garbage", "v1.4.0", false)]
    public void ComparesStableReleaseTags(string installed, string candidate, bool expected) =>
        Assert.Equal(expected, AppUpdateRules.IsNewerVersion(installed, candidate));

    [Fact]
    public void SkippedReleaseDoesNotPromptButNewerReleaseDoes()
    {
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.1", "1.3.1"));
        Assert.True(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.4.0", "1.3.1"));
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.0", ""));
    }

    [Fact]
    public void ParseRelease_MatchesApkAndLinuxTarball()
    {
        var sampleJson = """
        {
          "tag_name": "v3.2.0",
          "html_url": "https://github.com/inthevortex/PKForge/releases/tag/v3.2.0",
          "assets": [
            {
              "name": "pkforge-3.2.0.apk",
              "browser_download_url": "https://github.com/inthevortex/PKForge/releases/download/v3.2.0/pkforge-3.2.0.apk",
              "size": 52428800
            },
            {
              "name": "pkforge-3.2.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/inthevortex/PKForge/releases/download/v3.2.0/pkforge-3.2.0-linux-x64.tar.gz",
              "size": 73400320
            }
          ]
        }
        """;

        var apkCheck = AppUpdateRules.ParseRelease(sampleJson, "3.1.0", name => name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
        Assert.True(apkCheck.IsAvailable);
        Assert.NotNull(apkCheck.Update);
        Assert.Equal("v3.2.0", apkCheck.Update.Version);
        Assert.Equal("https://github.com/inthevortex/PKForge/releases/download/v3.2.0/pkforge-3.2.0.apk", apkCheck.Update.DownloadUrl);
        Assert.Equal(52428800, apkCheck.Update.SizeBytes);

        var linuxCheck = AppUpdateRules.ParseRelease(sampleJson, "3.1.0", name => name.EndsWith("-linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase));
        Assert.True(linuxCheck.IsAvailable);
        Assert.NotNull(linuxCheck.Update);
        Assert.Equal("v3.2.0", linuxCheck.Update.Version);
        Assert.Equal("https://github.com/inthevortex/PKForge/releases/download/v3.2.0/pkforge-3.2.0-linux-x64.tar.gz", linuxCheck.Update.DownloadUrl);
        Assert.Equal(73400320, linuxCheck.Update.SizeBytes);
    }

    [Fact]
    public void ParseRelease_ThrowsWhenAssetNotFound()
    {
        var sampleJson = """
        {
          "tag_name": "v3.2.0",
          "html_url": "https://github.com/inthevortex/PKForge/releases/tag/v3.2.0",
          "assets": [
            {
              "name": "pkforge-3.2.0.apk",
              "browser_download_url": "https://github.com/inthevortex/PKForge/releases/download/v3.2.0/pkforge-3.2.0.apk",
              "size": 52428800
            }
          ]
        }
        """;

        var ex = Assert.Throws<System.IO.InvalidDataException>(() =>
            AppUpdateRules.ParseRelease(sampleJson, "3.1.0",
                name => name.EndsWith("-linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase),
                "Missing linux build"));
        Assert.Equal("Missing linux build", ex.Message);
    }

    [Fact]
    public void ParseRelease_NotAvailableWhenAlreadyLatest()
    {
        var sampleJson = """
        {
          "tag_name": "v3.1.0",
          "html_url": "https://github.com/inthevortex/PKForge/releases/tag/v3.1.0",
          "assets": [
            {
              "name": "pkforge-3.1.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/inthevortex/PKForge/releases/download/v3.1.0/pkforge-3.1.0-linux-x64.tar.gz",
              "size": 73400320
            }
          ]
        }
        """;

        var check = AppUpdateRules.ParseRelease(sampleJson, "3.1.0", name => name.EndsWith("-linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase));
        Assert.False(check.IsAvailable);
        Assert.NotNull(check.Update);
        Assert.Contains("up to date", check.Message);
    }
}
