namespace PKForge.Engine.Tests;

using PKForge.App.Services;
using Xunit;

public sealed class PadInputPumpTests
{
    private sealed class TestPadHandler : IPadHandler
    {
        private readonly Lock _gate = new();
        private readonly List<PadButton> _buttons = [];
        public List<PadButton> HandledButtons
        {
            get { lock (_gate) return [.._buttons]; }
        }
        public bool ReturnValue { get; set; } = true;

        public bool OnPadButton(PadButton button)
        {
            lock (_gate) _buttons.Add(button);
            return ReturnValue;
        }
    }

    private sealed class TestPagingHandler : IPadPagingHandler, IPadReleaseHandler
    {
        private readonly Lock _gate = new();
        private readonly List<PadButton> _handled = [];
        private readonly List<PadButton> _released = [];
        public List<PadButton> HandledButtons
        {
            get { lock (_gate) return [.._handled]; }
        }
        public List<PadButton> ReleasedButtons
        {
            get { lock (_gate) return [.._released]; }
        }
        public bool ShouldersRepeat { get; set; } = true;

        public bool OnPadButton(PadButton button)
        {
            lock (_gate) _handled.Add(button);
            return true;
        }

        public void OnPadReleased(PadButton button)
        {
            lock (_gate) _released.Add(button);
        }
    }

    [Fact]
    public void EvaluateDirection_DeadzoneReturnsNull()
    {
        Assert.Null(PadInputPump.EvaluateDirection(0f, 0f));
        Assert.Null(PadInputPump.EvaluateDirection(0.49f, 0f));
        Assert.Null(PadInputPump.EvaluateDirection(-0.49f, 0f));
        Assert.Null(PadInputPump.EvaluateDirection(0f, 0.49f));
        Assert.Null(PadInputPump.EvaluateDirection(0f, -0.49f));
        Assert.Null(PadInputPump.EvaluateDirection(0.3f, 0.3f));
    }

    [Fact]
    public void EvaluateDirection_DominantAxisResolvesCorrectly()
    {
        Assert.Equal(PadButton.Up, PadInputPump.EvaluateDirection(0f, -0.6f));
        Assert.Equal(PadButton.Down, PadInputPump.EvaluateDirection(0f, 0.6f));

        Assert.Equal(PadButton.Left, PadInputPump.EvaluateDirection(-0.8f, 0.2f));
        Assert.Equal(PadButton.Right, PadInputPump.EvaluateDirection(0.8f, -0.3f));

        Assert.Equal(PadButton.Up, PadInputPump.EvaluateDirection(0.6f, -0.6f));
        Assert.Equal(PadButton.Down, PadInputPump.EvaluateDirection(-0.6f, 0.6f));
    }

    [Fact]
    public void IsDirectionalAndRepeatable_CategorizedAccurately()
    {
        Assert.True(PadInputPump.IsDirectional(PadButton.Up));
        Assert.True(PadInputPump.IsDirectional(PadButton.Down));
        Assert.True(PadInputPump.IsDirectional(PadButton.Left));
        Assert.True(PadInputPump.IsDirectional(PadButton.Right));
        Assert.False(PadInputPump.IsDirectional(PadButton.A));
        Assert.False(PadInputPump.IsDirectional(PadButton.L));
        Assert.False(PadInputPump.IsDirectional(PadButton.R));

        Assert.True(PadInputPump.IsRepeatable(PadButton.Up));
        Assert.True(PadInputPump.IsRepeatable(PadButton.L));
        Assert.True(PadInputPump.IsRepeatable(PadButton.R));
        Assert.False(PadInputPump.IsRepeatable(PadButton.A));
        Assert.False(PadInputPump.IsRepeatable(PadButton.B));
        Assert.False(PadInputPump.IsRepeatable(PadButton.Start));
    }

    [Fact]
    public void Press_DirectionalButtonsConsumedEvenWhenUnhandled()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler { ReturnValue = false };
        router.Push(handler);

        using var pump = new PadInputPump(router);

        Assert.True(pump.Press(PadButton.Up));
        Assert.True(pump.Press(PadButton.Down));
        Assert.True(pump.Press(PadButton.Left));
        Assert.True(pump.Press(PadButton.Right));

        Assert.False(pump.Press(PadButton.A));
        Assert.False(pump.Press(PadButton.B));
    }

    [Fact]
    public void Press_HandledButtonTriggersHaptic()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler { ReturnValue = true };
        router.Push(handler);

        int hapticCount = 0;
        using var pump = new PadInputPump(router, onHaptic: () => hapticCount++);

        Assert.True(pump.Press(PadButton.A));
        Assert.Equal(1, hapticCount);
        Assert.Equal(new[] { PadButton.A }, handler.HandledButtons);
    }

    [Fact]
    public void Release_NotifiesRouterAndStopsRepeat()
    {
        var router = new GamepadRouter();
        var handler = new TestPagingHandler();
        router.Push(handler);

        using var pump = new PadInputPump(router)
        {
            HatHoldDelayMs = 50,
            HatRepeatMs = 20,
        };

        pump.Press(PadButton.Down);
        Assert.Equal(PadButton.Down, pump.CurrentRepeatButton);

        pump.Release(PadButton.Down);
        Assert.Null(pump.CurrentRepeatButton);
        Assert.Equal(new[] { PadButton.Down }, handler.ReleasedButtons);
    }

    [Fact]
    public void Reset_ClearsAllHeldState()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler { ReturnValue = true };
        router.Push(handler);

        using var pump = new PadInputPump(router);

        pump.Press(PadButton.Down);
        pump.Motion(0f, -0.8f);

        Assert.Equal(PadButton.Down, pump.CurrentRepeatButton);
        Assert.Equal(PadButton.Up, pump.CurrentHatDirection);

        pump.Reset();

        Assert.Null(pump.CurrentRepeatButton);
        Assert.Null(pump.CurrentHatDirection);
    }

    [Fact]
    public async Task DirectionalRepeat_FiresAtExpectedCadence()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler { ReturnValue = true };
        router.Push(handler);

        using var pump = new PadInputPump(router)
        {
            HatHoldDelayMs = 120,
            HatRepeatMs = 40,
        };

        pump.Press(PadButton.Down);
        Assert.Single(handler.HandledButtons);

        await Task.Delay(30);
        Assert.Single(handler.HandledButtons);

        var deadline = DateTime.UtcNow.AddMilliseconds(800);
        while (handler.HandledButtons.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(handler.HandledButtons.Count >= 2, $"Expected at least 2 dispatches, got {handler.HandledButtons.Count}");

        pump.Release(PadButton.Down);
        var countAfterRelease = handler.HandledButtons.Count;

        await Task.Delay(60);
        Assert.Equal(countAfterRelease, handler.HandledButtons.Count);
    }

    [Fact]
    public async Task ShoulderRepeat_GatedByTopPagesWithShoulders()
    {
        var router = new GamepadRouter();
        var normalHandler = new TestPadHandler { ReturnValue = true };
        router.Push(normalHandler);

        using var pump = new PadInputPump(router)
        {
            ShoulderHoldDelayMs = 50,
            ShoulderRepeatMs = 25,
        };

        pump.Press(PadButton.R);
        Assert.Null(pump.CurrentRepeatButton);
        Assert.Single(normalHandler.HandledButtons);

        await Task.Delay(80);
        Assert.Single(normalHandler.HandledButtons);
        pump.Release(PadButton.R);

        var pagingHandler = new TestPagingHandler { ShouldersRepeat = true };
        router.Push(pagingHandler);

        pump.Press(PadButton.R);
        Assert.Equal(PadButton.R, pump.CurrentRepeatButton);
        Assert.Single(pagingHandler.HandledButtons);

        var deadline = DateTime.UtcNow.AddMilliseconds(600);
        while (pagingHandler.HandledButtons.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(pagingHandler.HandledButtons.Count >= 2, $"Expected at least 2 dispatches, got {pagingHandler.HandledButtons.Count}");
        pump.Release(PadButton.R);
    }

    [Fact]
    public async Task Motion_EvaluatesDeflectionAndRepeats()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler { ReturnValue = true };
        router.Push(handler);

        using var pump = new PadInputPump(router)
        {
            HatHoldDelayMs = 50,
            HatRepeatMs = 25,
        };

        Assert.True(pump.Motion(0f, -0.9f));
        Assert.Equal(PadButton.Up, pump.CurrentHatDirection);
        Assert.Single(handler.HandledButtons);

        var deadline = DateTime.UtcNow.AddMilliseconds(800);
        while (handler.HandledButtons.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(handler.HandledButtons.Count >= 2);
        Assert.All(handler.HandledButtons, btn => Assert.Equal(PadButton.Up, btn));

        Assert.True(pump.Motion(0.9f, 0f));
        Assert.Equal(PadButton.Right, pump.CurrentHatDirection);
        Assert.Equal(PadButton.Right, handler.HandledButtons.Last());

        Assert.False(pump.Motion(0f, 0f));
        Assert.Null(pump.CurrentHatDirection);
    }
}
