using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class LaunchTargetsTests
{
    [Theory]
    [InlineData("https://github.com/kwsch/PKHeX", "https://github.com/kwsch/PKHeX")]
    [InlineData("http://example.com/a b", "http://example.com/a%20b")]
    [InlineData("HTTPS://Example.com/x", "https://example.com/x")]
    public void For_OpensWebLinks(string uri, string expected)
    {
        Assert.Equal(expected, LaunchTargets.For(new Uri(uri), _ => false));
    }

    [Theory]
    [InlineData("file:///usr/bin/xterm")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("smb://host/share")]
    public void For_RefusesOtherSchemes(string uri)
    {
        Assert.Null(LaunchTargets.For(new Uri(uri), _ => false));
    }

    [Fact]
    public void For_OpensAnExistingFolder()
    {
        const string folder = "/home/ash/PKForge exports";
        Assert.Equal(folder, LaunchTargets.For(new Uri(folder), path => path == folder));
    }

    [Fact]
    public void For_RefusesAFileUriThatIsNotAFolder()
    {
        Assert.Null(LaunchTargets.For(new Uri("/home/ash/run.sh"), _ => false));
    }

    [Fact]
    public void For_RefusesMissingAndRelativeUris()
    {
        Assert.Null(LaunchTargets.For(null, _ => true));
        Assert.Null(LaunchTargets.For(new Uri("docs/readme", UriKind.Relative), _ => true));
    }

    [Fact]
    public void XdgOpen_PassesTheTargetAsOneArgumentWithoutTheShell()
    {
        var info = LaunchTargets.XdgOpen("/home/ash/PKForge exports");

        Assert.Equal("xdg-open", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(new[] { "/home/ash/PKForge exports" }, info.ArgumentList);
        Assert.Equal("", info.Arguments);
    }
}
