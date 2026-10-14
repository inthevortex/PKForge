using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesktopLaunchOptionsTests
{
    [Fact]
    public void NoArgumentsLaunchesFullscreen()
    {
        var options = DesktopLaunchOptions.Parse([]);

        Assert.Equal(DesktopWindowMode.Fullscreen, options.Mode);
        Assert.Null(options.Size);
        Assert.False(options.Wayland);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void WindowedLaunchesWindowedAtTheDefaultSize()
    {
        var options = DesktopLaunchOptions.Parse(["--windowed"]);

        Assert.Equal(DesktopWindowMode.Windowed, options.Mode);
        Assert.Null(options.Size);
    }

    [Theory]
    [InlineData("--size", "1280x800")]
    [InlineData("--size=1280x800", null)]
    [InlineData("--size", "1280X800")]
    public void SizeImpliesWindowed(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];

        var options = DesktopLaunchOptions.Parse(args);

        Assert.Equal(DesktopWindowMode.Windowed, options.Mode);
        Assert.Equal(new LaunchWindowSize(1280, 800), options.Size);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void ExplicitFullscreenWinsOverSize()
    {
        var options = DesktopLaunchOptions.Parse(["--size", "1280x800", "--fullscreen"]);

        Assert.Equal(DesktopWindowMode.Fullscreen, options.Mode);
        Assert.Equal(new LaunchWindowSize(1280, 800), options.Size);
    }

    [Fact]
    public void TheLastModeFlagWins()
    {
        Assert.Equal(DesktopWindowMode.Windowed, DesktopLaunchOptions.Parse(["--fullscreen", "--windowed"]).Mode);
        Assert.Equal(DesktopWindowMode.Fullscreen, DesktopLaunchOptions.Parse(["--windowed", "--fullscreen"]).Mode);
    }

    [Theory]
    [InlineData("1280")]
    [InlineData("1280x")]
    [InlineData("x800")]
    [InlineData("1280x800x2")]
    [InlineData("-1280x800")]
    [InlineData("+1280x800")]
    [InlineData("1280.5x800")]
    [InlineData("319x800")]
    [InlineData("1280x16385")]
    [InlineData("big")]
    public void InvalidSizesAreIgnoredWithAWarning(string size)
    {
        var options = DesktopLaunchOptions.Parse(["--size", size]);

        Assert.Null(options.Size);
        Assert.Equal(DesktopWindowMode.Fullscreen, options.Mode);
        Assert.Single(options.Warnings);
    }

    [Fact]
    public void SizeWithoutAValueWarns()
    {
        var options = DesktopLaunchOptions.Parse(["--size"]);

        Assert.Null(options.Size);
        Assert.Single(options.Warnings);
    }

    [Fact]
    public void AnInvalidSizeKeepsAnEarlierValidOne()
    {
        var options = DesktopLaunchOptions.Parse(["--size", "1280x800", "--size", "nope"]);

        Assert.Equal(new LaunchWindowSize(1280, 800), options.Size);
        Assert.Single(options.Warnings);
    }

    [Theory]
    [InlineData(320, 320)]
    [InlineData(16384, 16384)]
    [InlineData(1920, 1080)]
    public void ParseSizeAcceptsTheBounds(int width, int height)
    {
        Assert.Equal(new LaunchWindowSize(width, height), DesktopLaunchOptions.ParseSize($"{width}x{height}"));
    }

    [Fact]
    public void WaylandIsRecorded()
    {
        Assert.True(DesktopLaunchOptions.Parse(["--wayland"]).Wayland);
    }

    [Fact]
    public void UnknownOptionsWarnButPlainArgumentsPassQuietly()
    {
        var options = DesktopLaunchOptions.Parse(["--frobnicate", "pkforge://open"]);

        Assert.Equal(DesktopWindowMode.Fullscreen, options.Mode);
        Assert.Single(options.Warnings);
        Assert.Contains("--frobnicate", options.Warnings[0], StringComparison.Ordinal);
    }
}
