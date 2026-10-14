using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class GamepadRouterTests
{
    private sealed class Handler : IPadHandler
    {
        public bool OnPadButton(PadButton button) => true;
    }

    [Fact]
    public void IsTopFollowsTheMostRecentPush()
    {
        var router = new GamepadRouter();
        var home = new Handler();
        var menu = new Handler();

        Assert.False(router.IsTop(home));

        router.Push(home);
        Assert.True(router.IsTop(home));

        router.Push(menu);
        Assert.False(router.IsTop(home));
        Assert.True(router.IsTop(menu));

        router.Remove(menu);
        Assert.True(router.IsTop(home));
    }
}
