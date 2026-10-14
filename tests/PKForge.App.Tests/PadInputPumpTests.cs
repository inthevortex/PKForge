namespace PKForge.App.Tests;

using PKForge.App.Services;
using Xunit;

public sealed class PadInputPumpTests
{
    private sealed class TestPadHandler : IPadHandler
    {
        public List<PadButton> HandledButtons { get; } = [];
        public bool ReturnValue { get; set; } = true;
        public Action<PadButton>? OnPress { get; set; }

        public bool OnPadButton(PadButton button)
        {
            HandledButtons.Add(button);
            OnPress?.Invoke(button);
            return ReturnValue;
        }
    }

    private sealed class TestPagingHandler : IPadPagingHandler, IPadReleaseHandler
    {
        public List<PadButton> HandledButtons { get; } = [];
        public List<PadButton> ReleasedButtons { get; } = [];
        public bool ShouldersRepeat { get; set; } = true;
        public bool ReturnValue { get; set; } = true;

        public bool OnPadButton(PadButton button)
        {
            HandledButtons.Add(button);
            return ReturnValue;
        }

        public void OnPadReleased(PadButton button) => ReleasedButtons.Add(button);
    }

    /// <summary>A clock that only moves when the test advances it; due timers fire in order.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _nowMs;

        public int ArmedTimers => _timers.Count(t => t.DueAtMs is not null);
        public int CreatedTimers { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            CreatedTimers++;
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Moves the clock forward, firing each due timer and then running <paramref name="afterEachFire"/>.</summary>
        public void Advance(int ms, Action? afterEachFire = null)
        {
            var target = _nowMs + ms;
            while (_timers
                .Where(t => t.DueAtMs <= target)
                .OrderBy(t => t.DueAtMs)
                .FirstOrDefault() is { } next)
            {
                _nowMs = next.DueAtMs!.Value;
                next.Fire();
                afterEachFire?.Invoke();
            }
            _nowMs = target;
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _periodMs;
            public long? DueAtMs { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAtMs = dueTime == Timeout.InfiniteTimeSpan ? null : owner._nowMs + (long)dueTime.TotalMilliseconds;
                _periodMs = period == Timeout.InfiniteTimeSpan ? null : (long)period.TotalMilliseconds;
                return true;
            }

            public void Fire()
            {
                DueAtMs = _periodMs is { } period ? DueAtMs + period : null;
                callback(state);
            }

            public void Dispose()
            {
                DueAtMs = null;
                owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A pump on a manual clock whose UI-thread posts queue until drained, so tests can model
    /// both a responsive UI thread and one that is stalled while timers keep firing.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public ManualTimeProvider Time { get; } = new();
        public Queue<Action> UiQueue { get; } = new();
        public GamepadRouter Router { get; } = new();
        public PadInputPump Pump { get; }
        public int Haptics { get; private set; }

        public Harness(IPadHandler handler)
        {
            Router.Push(handler);
            Pump = new PadInputPump(Router, () => Haptics++, UiQueue.Enqueue, Time);
        }

        public void Drain()
        {
            while (UiQueue.TryDequeue(out var action))
                action();
        }

        public void Run(int ms) => Time.Advance(ms, Drain);

        public void Stall(int ms) => Time.Advance(ms);

        public void Dispose() => Pump.Dispose();
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
        Assert.Equal(PadButton.Up, PadInputPump.EvaluateDirection(0f, -0.5f));
        Assert.Equal(PadButton.Right, PadInputPump.EvaluateDirection(0.5f, 0f));

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
    public void Press_UnhandledDirectionsAreConsumedWithoutRepeatOrHaptic()
    {
        var handler = new TestPadHandler { ReturnValue = false };
        using var h = new Harness(handler);

        Assert.True(h.Pump.Press(PadButton.Up));
        Assert.True(h.Pump.Press(PadButton.Down));
        Assert.True(h.Pump.Press(PadButton.Left));
        Assert.True(h.Pump.Press(PadButton.Right));
        Assert.False(h.Pump.Press(PadButton.A));
        Assert.False(h.Pump.Press(PadButton.B));

        Assert.Null(h.Pump.CurrentRepeatButton);
        Assert.Equal(0, h.Time.ArmedTimers);
        Assert.Equal(0, h.Haptics);
    }

    [Fact]
    public void Press_HandledButtonTriggersHapticWithoutRepeat()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        Assert.True(h.Pump.Press(PadButton.A));
        h.Run(2000);

        Assert.Equal(1, h.Haptics);
        Assert.Equal([PadButton.A], handler.HandledButtons);
        Assert.Null(h.Pump.CurrentRepeatButton);
    }

    [Fact]
    public void DirectionalHold_RepeatsAfter320MsThenEvery80Ms()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        Assert.Single(handler.HandledButtons);

        h.Run(319);
        Assert.Single(handler.HandledButtons);
        h.Run(1);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(79);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(1);
        Assert.Equal(3, handler.HandledButtons.Count);
        h.Run(160);
        Assert.Equal(5, handler.HandledButtons.Count);
        Assert.All(handler.HandledButtons, button => Assert.Equal(PadButton.Down, button));
        Assert.Equal(1, h.Haptics);

        h.Pump.Release(PadButton.Down);
        h.Run(2000);
        Assert.Equal(5, handler.HandledButtons.Count);
        Assert.Null(h.Pump.CurrentRepeatButton);
    }

    [Fact]
    public void DirectionalHold_ReusesOneTimerForEveryStep()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Run(320 + 80 * 10);
        Assert.Equal(12, handler.HandledButtons.Count);
        Assert.Equal(1, h.Time.CreatedTimers);

        h.Pump.Release(PadButton.Down);
        h.Pump.Press(PadButton.Up);
        h.Run(320 + 80 * 3);
        Assert.Equal(17, handler.HandledButtons.Count);
        Assert.Equal(2, h.Time.CreatedTimers);
        Assert.Equal(1, h.Time.ArmedTimers);
    }

    [Fact]
    public void HatHold_ReusesOneTimerForEveryStep()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Motion(0.9f, 0f);
        h.Run(320 + 80 * 10);
        Assert.Equal(12, handler.HandledButtons.Count);
        Assert.Equal(1, h.Time.CreatedTimers);
        Assert.Equal(1, h.Time.ArmedTimers);
    }

    [Fact]
    public void ShoulderHold_RepeatsAfter380MsThenEvery140MsOnPagingScreens()
    {
        var handler = new TestPagingHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.R);
        Assert.Equal(PadButton.R, h.Pump.CurrentRepeatButton);

        h.Run(379);
        Assert.Single(handler.HandledButtons);
        h.Run(1);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(139);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(1);
        Assert.Equal(3, handler.HandledButtons.Count);
        Assert.Equal(1, h.Haptics);
    }

    [Fact]
    public void ShoulderPress_DoesNotRepeatWhenTopScreenDoesNotPage()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        Assert.True(h.Pump.Press(PadButton.R));
        h.Run(2000);

        Assert.Null(h.Pump.CurrentRepeatButton);
        Assert.Single(handler.HandledButtons);
    }

    [Fact]
    public void ShoulderPress_RepeatDecisionIsTakenBeforeDispatch()
    {
        var router = new GamepadRouter();
        var pager = new TestPagingHandler();
        var opener = new TestPadHandler { OnPress = _ => router.Push(pager) };
        router.Push(opener);
        var time = new ManualTimeProvider();
        using var pump = new PadInputPump(router, timeProvider: time);

        pump.Press(PadButton.R);
        time.Advance(2000);

        Assert.Null(pump.CurrentRepeatButton);
        Assert.Empty(pager.HandledButtons);
    }

    [Fact]
    public void ShoulderRepeat_StopsWhenTopScreenStopsPaging()
    {
        var handler = new TestPagingHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.L);
        h.Run(380);
        Assert.Equal(2, handler.HandledButtons.Count);

        handler.ShouldersRepeat = false;
        h.Run(2000);

        Assert.Equal(2, handler.HandledButtons.Count);
        Assert.Null(h.Pump.CurrentRepeatButton);
        Assert.Equal(0, h.Time.ArmedTimers);
    }

    [Fact]
    public void KeyRepeat_StopsWhenDispatchIsNoLongerHandled()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Right);
        handler.ReturnValue = false;
        h.Run(320);
        Assert.Equal(2, handler.HandledButtons.Count);
        Assert.Null(h.Pump.CurrentRepeatButton);

        h.Run(2000);
        Assert.Equal(2, handler.HandledButtons.Count);
    }

    [Fact]
    public void KeyRepeat_NewRepeatingPressReplacesTheHeldOne()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Run(200);
        h.Pump.Press(PadButton.Left);
        Assert.Equal(PadButton.Left, h.Pump.CurrentRepeatButton);

        h.Pump.Release(PadButton.Down);
        Assert.Equal(PadButton.Left, h.Pump.CurrentRepeatButton);

        h.Run(319);
        Assert.Equal([PadButton.Down, PadButton.Left], handler.HandledButtons);
        h.Run(1);
        Assert.Equal([PadButton.Down, PadButton.Left, PadButton.Left], handler.HandledButtons);
    }

    [Fact]
    public void KeyRepeat_StalledUiThreadNeverQueuesMoreThanOneTick()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Stall(1000);
        Assert.Single(h.UiQueue);

        h.Drain();
        Assert.Equal(2, handler.HandledButtons.Count);

        h.Stall(1000);
        Assert.Single(h.UiQueue);
        h.Drain();
        Assert.Equal(3, handler.HandledButtons.Count);
    }

    [Fact]
    public void KeyRepeat_StaleTickFromPreviousButtonIsDropped()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Stall(320);
        Assert.Single(h.UiQueue);

        h.Pump.Press(PadButton.Left);
        h.Drain();
        Assert.Equal([PadButton.Down, PadButton.Left], handler.HandledButtons);

        h.Run(320);
        Assert.Equal([PadButton.Down, PadButton.Left, PadButton.Left], handler.HandledButtons);
    }

    [Fact]
    public void KeyRepeat_StaleTickIsDroppedWhenSameButtonIsPressedAgain()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Stall(320);
        h.Pump.Release(PadButton.Down);
        h.Pump.Press(PadButton.Down);
        h.Drain();
        Assert.Equal(2, handler.HandledButtons.Count);

        h.Run(319);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(1);
        Assert.Equal(3, handler.HandledButtons.Count);
    }

    [Fact]
    public void Release_AlwaysNotifiesRouterAndOnlyStopsTheRepeatingButton()
    {
        var handler = new TestPagingHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Pump.Release(PadButton.A);
        Assert.Equal(PadButton.Down, h.Pump.CurrentRepeatButton);

        h.Pump.Release(PadButton.Down);
        Assert.Null(h.Pump.CurrentRepeatButton);
        Assert.Equal([PadButton.A, PadButton.Down], handler.ReleasedButtons);
    }

    [Fact]
    public void Motion_StepsOnDeflectionThenRepeatsWithHapticOnlyOnce()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        Assert.True(h.Pump.Motion(0f, -0.9f));
        Assert.Equal(PadButton.Up, h.Pump.CurrentHatDirection);
        Assert.Single(handler.HandledButtons);

        h.Run(319);
        Assert.Single(handler.HandledButtons);
        h.Run(1);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(80);
        Assert.Equal(3, handler.HandledButtons.Count);
        Assert.All(handler.HandledButtons, button => Assert.Equal(PadButton.Up, button));
        Assert.Equal(1, h.Haptics);
    }

    [Fact]
    public void Motion_WithinTheSameDirectionDoesNotRestartTheStep()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Motion(0f, -0.9f);
        h.Run(200);
        Assert.True(h.Pump.Motion(0.2f, -0.7f));
        Assert.Single(handler.HandledButtons);

        h.Run(120);
        Assert.Equal(2, handler.HandledButtons.Count);
    }

    [Fact]
    public void Motion_UnhandledDirectionStillConsumedAndKeepsRepeating()
    {
        var handler = new TestPadHandler { ReturnValue = false };
        using var h = new Harness(handler);

        Assert.True(h.Pump.Motion(-0.9f, 0f));
        Assert.Equal(0, h.Haptics);

        h.Run(320 + 80 * 3);
        Assert.Equal(5, handler.HandledButtons.Count);
        Assert.All(handler.HandledButtons, button => Assert.Equal(PadButton.Left, button));
    }

    [Fact]
    public void Motion_ReturningToCentreStopsRepeatWithoutRelease()
    {
        var handler = new TestPagingHandler();
        using var h = new Harness(handler);

        h.Pump.Motion(0f, 0.9f);
        Assert.False(h.Pump.Motion(0.1f, 0.1f));
        Assert.Null(h.Pump.CurrentHatDirection);

        h.Run(2000);
        Assert.Single(handler.HandledButtons);
        Assert.Empty(handler.ReleasedButtons);
    }

    [Fact]
    public void Motion_RollingToAnotherDirectionDropsTheStaleTick()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Motion(0f, -0.9f);
        h.Stall(320);
        Assert.Single(h.UiQueue);

        h.Pump.Motion(0.9f, 0f);
        h.Drain();
        Assert.Equal([PadButton.Up, PadButton.Right], handler.HandledButtons);
        Assert.Equal(2, h.Haptics);

        h.Run(319);
        Assert.Equal(2, handler.HandledButtons.Count);
        h.Run(1);
        Assert.Equal([PadButton.Up, PadButton.Right, PadButton.Right], handler.HandledButtons);
    }

    [Fact]
    public void Motion_StalledUiThreadNeverQueuesMoreThanOneTick()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Motion(0f, 0.9f);
        h.Stall(1000);
        Assert.Single(h.UiQueue);

        h.Drain();
        Assert.Equal(2, handler.HandledButtons.Count);
    }

    [Fact]
    public void Reset_StopsEveryRepeatAndDropsQueuedTicks()
    {
        var handler = new TestPadHandler();
        using var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Pump.Motion(0f, -0.8f);
        Assert.Equal(PadButton.Down, h.Pump.CurrentRepeatButton);
        Assert.Equal(PadButton.Up, h.Pump.CurrentHatDirection);

        h.Stall(320);
        Assert.Equal(2, h.UiQueue.Count);

        h.Pump.Reset();
        h.Drain();
        h.Run(2000);

        Assert.Null(h.Pump.CurrentRepeatButton);
        Assert.Null(h.Pump.CurrentHatDirection);
        Assert.Equal(2, handler.HandledButtons.Count);
        Assert.Equal(0, h.Time.ArmedTimers);
    }

    [Fact]
    public void Dispose_StopsRepeatsAndIgnoresLaterTicks()
    {
        var handler = new TestPadHandler();
        var h = new Harness(handler);

        h.Pump.Press(PadButton.Down);
        h.Stall(320);
        h.Dispose();
        h.Drain();
        h.Run(2000);

        Assert.Single(handler.HandledButtons);
        Assert.Equal(0, h.Time.ArmedTimers);
    }

    [Fact]
    public void Repeat_RunsWithInlineDispatchAndDefaultTiming()
    {
        var router = new GamepadRouter();
        var handler = new TestPadHandler();
        router.Push(handler);
        var time = new ManualTimeProvider();
        using var pump = new PadInputPump(router, timeProvider: time);

        pump.Press(PadButton.Up);
        time.Advance(PadInputPump.DefaultHatHoldDelayMs + PadInputPump.DefaultHatRepeatMs);

        Assert.Equal(3, handler.HandledButtons.Count);
    }
}
