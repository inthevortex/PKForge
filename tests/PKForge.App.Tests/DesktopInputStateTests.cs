using PKForge.App.Platforms.Desktop;
using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesktopInputStateTests
{
    private enum Key { Enter, Space, Tab, Escape }

    private sealed class AcceptAll : IPadHandler
    {
        public bool OnPadButton(PadButton button) => true;
    }

    [Fact]
    public void HeldButtons_ReleaseReturnsTheButtonThatKeyPressed()
    {
        var held = new HeldButtons<Key>();

        Assert.True(held.Press(Key.Tab, PadButton.Y));

        Assert.Equal(PadButton.Y, held.Release(Key.Tab));
        Assert.False(held.IsHeld(Key.Tab));
    }

    [Fact]
    public void HeldButtons_RepeatedPressOfAHeldKeyIsRejected()
    {
        var held = new HeldButtons<Key>();

        Assert.True(held.Press(Key.Enter, PadButton.A));
        Assert.False(held.Press(Key.Enter, PadButton.A));
    }

    [Fact]
    public void HeldButtons_ReleaseOfAnUnheldKeyReleasesNothing()
    {
        var held = new HeldButtons<Key>();

        Assert.Null(held.Release(Key.Escape));
    }

    [Fact]
    public void HeldButtons_ButtonStaysHeldWhileAnotherKeyPressesIt()
    {
        var held = new HeldButtons<Key>();
        held.Press(Key.Enter, PadButton.A);
        held.Press(Key.Space, PadButton.A);

        Assert.Null(held.Release(Key.Enter));
        Assert.Equal(PadButton.A, held.Release(Key.Space));
    }

    [Fact]
    public void HeldButtons_ReleaseAllReturnsEachButtonOnceAndForgetsEveryKey()
    {
        var held = new HeldButtons<Key>();
        held.Press(Key.Enter, PadButton.A);
        held.Press(Key.Space, PadButton.A);
        held.Press(Key.Tab, PadButton.Y);

        var released = held.ReleaseAll();

        Assert.Equal([PadButton.A, PadButton.Y], released.Order());
        Assert.Null(held.Release(Key.Enter));
        Assert.True(held.Press(Key.Tab, PadButton.Y));
    }

    [Fact]
    public void Gamepad_PressWhileInactiveIsNotPostedAndHasNoRelease()
    {
        var state = new GamepadInputState(windowActive: false);

        Assert.False(state.ButtonDown(1, PadButton.A));
        Assert.Null(state.ButtonUp(1, PadButton.A));
    }

    [Fact]
    public void Gamepad_PressPostedWhileActiveIsReleasedAfterTheWindowDeactivates()
    {
        var state = new GamepadInputState(windowActive: true);
        Assert.True(state.ButtonDown(1, PadButton.A));

        var released = state.Deactivate();

        Assert.Equal([PadButton.A], released);
        Assert.Null(state.ButtonUp(1, PadButton.A));
    }

    [Fact]
    public void Gamepad_ButtonUpIsNeverGatedByFocus()
    {
        var state = new GamepadInputState(windowActive: true);
        state.ButtonDown(1, PadButton.B);
        state.Deactivate();
        state.Activate();
        state.ButtonDown(1, PadButton.L);

        Assert.Equal(PadButton.L, state.ButtonUp(1, PadButton.L));
    }

    [Fact]
    public void Gamepad_StickIsTrackedWhileInactiveSoReturningHasNoPhantomDirection()
    {
        var state = new GamepadInputState(windowActive: true);
        state.StickMoved(1, StickAxis.X, short.MaxValue);
        state.Deactivate();

        Assert.Null(state.StickMoved(1, StickAxis.X, 0));
        state.Activate();
        var stick = state.StickMoved(1, StickAxis.Y, 0);

        Assert.Equal((0f, 0f), stick);
    }

    [Fact]
    public void Gamepad_AnIdlePadDoesNotCancelAnotherPadsStick()
    {
        var state = new GamepadInputState(windowActive: true);
        state.StickMoved(1, StickAxis.Y, short.MinValue);

        var stick = state.StickMoved(2, StickAxis.X, 300);

        Assert.NotNull(stick);
        Assert.Equal(PadButton.Up, PadInputPump.EvaluateDirection(stick.Value.X, stick.Value.Y));
    }

    [Fact]
    public void Gamepad_RemovingAPadReleasesItsButtonsAndDropsItsStick()
    {
        var state = new GamepadInputState(windowActive: true);
        state.ButtonDown(1, PadButton.Right);
        state.ButtonDown(2, PadButton.A);
        state.StickMoved(1, StickAxis.X, short.MaxValue);
        state.StickMoved(2, StickAxis.X, 0);

        var (released, stick) = state.RemovePad(1);

        Assert.Equal([PadButton.Right], released);
        Assert.Equal((0f, 0f), stick);
        Assert.Equal(PadButton.A, state.ButtonUp(2, PadButton.A));
    }

    [Fact]
    public void Gamepad_SameButtonOnTwoPadsIsReleasedOnlyWhenBothLetGo()
    {
        var state = new GamepadInputState(windowActive: true);
        state.ButtonDown(1, PadButton.A);
        state.ButtonDown(2, PadButton.A);

        Assert.Null(state.ButtonUp(1, PadButton.A));
        Assert.Equal(PadButton.A, state.ButtonUp(2, PadButton.A));
    }

    [Fact]
    public void Gamepad_DeactivationMakesSticksPostedBeforeItStale()
    {
        var state = new GamepadInputState(windowActive: true);
        Assert.NotNull(state.StickMoved(1, StickAxis.X, short.MaxValue));
        var posted = state.Epoch;
        Assert.True(state.IsCurrent(posted));

        state.Deactivate();

        Assert.False(state.IsCurrent(posted));
        Assert.False(state.IsCurrent(state.Epoch));
    }

    [Fact]
    public void Gamepad_ActivationReturnsTheStickHeldMeanwhileAndStartsAFreshEpoch()
    {
        var state = new GamepadInputState(windowActive: true);
        var before = state.Epoch;
        state.Deactivate();
        Assert.Null(state.StickMoved(1, StickAxis.Y, short.MinValue));

        var stick = state.Activate();

        Assert.Equal(PadButton.Up, PadInputPump.EvaluateDirection(stick.X, stick.Y));
        Assert.True(state.IsCurrent(state.Epoch));
        Assert.False(state.IsCurrent(before));
    }

    [Fact]
    public void Gamepad_ActivationWithACentredStickReturnsNeutral()
    {
        var state = new GamepadInputState(windowActive: false);

        Assert.Equal((0f, 0f), state.Activate());
    }

    [Fact]
    public void Gamepad_AStickQueuedBeforeDeactivationCannotRestartTheHatRepeat()
    {
        var router = new GamepadRouter();
        router.Push(new AcceptAll());
        using var pump = new PadInputPump(router);
        var state = new GamepadInputState(windowActive: true);
        var queue = new List<Action>();
        void PostStick((float X, float Y) stick, int epoch) => queue.Add(() =>
        {
            if (state.IsCurrent(epoch)) pump.Motion(stick.X, stick.Y);
        });

        PostStick(state.StickMoved(1, StickAxis.X, short.MaxValue)!.Value, state.Epoch);
        foreach (var button in state.Deactivate())
            queue.Add(() => pump.Release(button));
        queue.Add(() => pump.Motion(0f, 0f));
        pump.Reset();
        foreach (var action in queue) action();

        Assert.Null(pump.CurrentHatDirection);
    }

    [Fact]
    public void Gamepad_ActivationPostsTheHeldStickSoTheHatResumes()
    {
        var router = new GamepadRouter();
        router.Push(new AcceptAll());
        using var pump = new PadInputPump(router);
        var state = new GamepadInputState(windowActive: false);
        state.StickMoved(1, StickAxis.X, short.MinValue);

        var stick = state.Activate();
        if (state.IsCurrent(state.Epoch)) pump.Motion(stick.X, stick.Y);

        Assert.Equal(PadButton.Left, pump.CurrentHatDirection);
    }

    [Fact]
    public void Rumble_LongPressKindReachesTheRumble()
    {
        var kinds = new List<HapticKind>();
        var gate = new PadRumbleGate(() => true, kinds.Add);

        gate.OnHaptic(HapticKind.LongPress);
        gate.FromPad(() => gate.OnHaptic(HapticKind.LongPress));

        Assert.Equal([HapticKind.LongPress], kinds);
    }

    [Fact]
    public void Rumble_OnlyForHandledPadPressesWhenEnabled()
    {
        var router = new GamepadRouter();
        router.Push(new AcceptAll());
        var enabled = true;
        var rumbles = 0;
        var gate = new PadRumbleGate(() => enabled, _ => rumbles++);
        using var pump = new PadInputPump(router, () => gate.OnHaptic(HapticKind.Click));

        pump.Press(PadButton.A);
        Assert.Equal(0, rumbles);

        gate.FromPad(() => pump.Press(PadButton.A));
        Assert.Equal(1, rumbles);

        enabled = false;
        gate.FromPad(() => pump.Press(PadButton.A));
        Assert.Equal(1, rumbles);
    }
}
