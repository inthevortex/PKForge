using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class SpriteStyleTests
{
    [Theory]
    [InlineData(null, false, SpriteStyle.Pixel)]
    [InlineData(null, true, SpriteStyle.Pixel)]
    [InlineData(SpriteStyle.Home, true, SpriteStyle.Pixel)]
    [InlineData(SpriteStyle.Home, false, SpriteStyle.Pixel)]
    public void AndroidAlwaysDrawsThePixelSprites(SpriteStyle? picked, bool packInstalled, SpriteStyle expected) =>
        Assert.Equal(expected, SpriteStyles.Resolve(desktop: false, picked, packInstalled));

    [Theory]
    [InlineData(false, SpriteStyle.Pixel)]
    [InlineData(true, SpriteStyle.Home)]
    public void DesktopDefaultsToHomeRendersOnceThePackIsIn(bool packInstalled, SpriteStyle expected) =>
        Assert.Equal(expected, SpriteStyles.Resolve(desktop: true, picked: null, packInstalled));

    [Theory]
    [InlineData(SpriteStyle.Pixel, true)]
    [InlineData(SpriteStyle.Pixel, false)]
    [InlineData(SpriteStyle.Home, true)]
    [InlineData(SpriteStyle.Home, false)]
    public void TheDesktopPlayersPickWinsOverTheDefault(SpriteStyle picked, bool packInstalled) =>
        Assert.Equal(picked, SpriteStyles.Resolve(desktop: true, picked, packInstalled));

    [Theory]
    [InlineData(1, 512, 32)]
    [InlineData(0, 512, 32)]
    [InlineData(-5, 512, 32)]
    [InlineData(32, 512, 32)]
    [InlineData(33, 512, 48)]
    [InlineData(100, 512, 112)]
    [InlineData(112, 512, 112)]
    [InlineData(500, 512, 512)]
    [InlineData(693, 512, 512)]
    [InlineData(int.MaxValue, 512, 512)]
    [InlineData(100, 20, 20)]
    [InlineData(100, 0, 0)]
    public void PreparedSidesRoundUpToAStepWithinTheRendersOwnSize(int pixels, int native, int expected) =>
        Assert.Equal(expected, SpriteStyles.PreparedSide(pixels, native));

    [Fact]
    public void APreparedSideIsNeverSmallerThanTheDrawUnlessTheRenderIs()
    {
        for (var pixels = 1; pixels <= 600; pixels++)
        {
            var side = SpriteStyles.PreparedSide(pixels, 512);
            Assert.True(side >= Math.Min(pixels, 512), $"{pixels} px got {side}");
            Assert.True(side - pixels < SpriteStyles.PreparedStep || side == SpriteStyles.MinPreparedSide, $"{pixels} px got {side}");
        }
    }
}
