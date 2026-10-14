using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class SdlRuntimeTests
{
    private const uint Audio = 0x10;
    private const uint Gamepad = 0x2000;
    private const uint Events = 0x4000;

    private sealed class FakeSdl : ISdlNative
    {
        public List<string> Calls { get; } = [];
        public HashSet<uint> FailingFlags { get; } = [];
        public bool MixerFails { get; set; }

        public bool InitSubSystem(uint flag)
        {
            Calls.Add($"init {flag:X}");
            return !FailingFlags.Contains(flag);
        }

        public void QuitSubSystem(uint flag) => Calls.Add($"quit {flag:X}");

        public bool InitMixer()
        {
            Calls.Add("mix init");
            return !MixerFails;
        }

        public void QuitMixer() => Calls.Add("mix quit");

        public string LastError => "fake failure";
    }

    private static (SdlSubsystems Subsystems, FakeSdl Native, List<string> Warnings) Create()
    {
        var native = new FakeSdl();
        var warnings = new List<string>();
        return (new SdlSubsystems(native, warnings.Add), native, warnings);
    }

    [Fact]
    public void Subsystems_InitialisedOnceAndQuitOnlyByTheLastHolder()
    {
        var (subsystems, native, _) = Create();

        Assert.True(subsystems.Acquire(Gamepad));
        Assert.True(subsystems.Acquire(Gamepad));
        subsystems.Release(Gamepad);

        Assert.Equal(["init 2000"], native.Calls);
        Assert.Equal(1, subsystems.RefCount(Gamepad));

        subsystems.Release(Gamepad);

        Assert.Equal(["init 2000", "quit 2000"], native.Calls);
        Assert.False(subsystems.HoldsAny);
    }

    [Fact]
    public void Subsystems_CombinedFlagsAreCountedPerFlag()
    {
        var (subsystems, native, _) = Create();

        Assert.True(subsystems.Acquire(Gamepad | Events));
        Assert.True(subsystems.Acquire(Events));
        subsystems.Release(Gamepad | Events);

        Assert.Equal(["init 2000", "init 4000", "quit 2000"], native.Calls);
        Assert.Equal(1, subsystems.RefCount(Events));
    }

    [Fact]
    public void Subsystems_PartialFailureRollsBackOnlyWhatThisCallTook()
    {
        var (subsystems, native, warnings) = Create();
        Assert.True(subsystems.Acquire(Gamepad));
        native.FailingFlags.Add(Events);

        Assert.False(subsystems.Acquire(Audio | Gamepad | Events));

        Assert.Equal(["init 2000", "init 10", "init 4000", "quit 10"], native.Calls);
        Assert.Equal(1, subsystems.RefCount(Gamepad));
        Assert.Equal(0, subsystems.RefCount(Audio));
        Assert.Equal(0, subsystems.RefCount(Events));
        Assert.Contains(warnings, w => w.Contains("fake failure", StringComparison.Ordinal));
    }

    [Fact]
    public void Subsystems_OverReleaseIsReportedAndQuitsNothing()
    {
        var (subsystems, native, warnings) = Create();

        subsystems.Release(Audio);

        Assert.Empty(native.Calls);
        Assert.Single(warnings);
    }

    [Fact]
    public void Mixer_EachHolderSharesOneInitAndTheLastQuitsMixerBeforeAudio()
    {
        var (subsystems, native, _) = Create();

        Assert.True(subsystems.AcquireMixer(Audio));
        Assert.True(subsystems.AcquireMixer(Audio));
        subsystems.ReleaseMixer(Audio);
        Assert.Equal(["init 10", "mix init"], native.Calls);

        subsystems.ReleaseMixer(Audio);

        Assert.Equal(["init 10", "mix init", "mix quit", "quit 10"], native.Calls);
        Assert.Equal(0, subsystems.MixerHolds);
        Assert.False(subsystems.HoldsAny);
    }

    [Fact]
    public void Mixer_FailedInitHoldsNothingAndARetryInitialisesAgain()
    {
        var (subsystems, native, _) = Create();
        native.MixerFails = true;

        Assert.False(subsystems.AcquireMixer(Audio));
        Assert.Equal(["init 10", "mix init", "quit 10"], native.Calls);
        Assert.False(subsystems.HoldsAny);

        native.MixerFails = false;
        Assert.True(subsystems.AcquireMixer(Audio));
        subsystems.ReleaseMixer(Audio);

        Assert.Equal(1, native.Calls.Count(c => c == "mix quit"));
        Assert.Equal(2, native.Calls.Count(c => c == "quit 10"));
    }

    [Fact]
    public void Mixer_FailedAudioInitNeverTouchesTheMixer()
    {
        var (subsystems, native, _) = Create();
        native.FailingFlags.Add(Audio);

        Assert.False(subsystems.AcquireMixer(Audio));

        Assert.Equal(["init 10"], native.Calls);
    }

    [Fact]
    public void Mixer_AudioHeldByAnotherUserSurvivesTheMixerRelease()
    {
        var (subsystems, native, _) = Create();
        Assert.True(subsystems.Acquire(Audio));
        Assert.True(subsystems.AcquireMixer(Audio));

        subsystems.ReleaseMixer(Audio);

        Assert.Equal(["init 10", "mix init", "mix quit"], native.Calls);
        Assert.Equal(1, subsystems.RefCount(Audio));
    }

    [Fact]
    public void Mixer_OverReleaseIsReportedAndReleasesNothing()
    {
        var (subsystems, native, warnings) = Create();
        Assert.True(subsystems.Acquire(Audio));

        subsystems.ReleaseMixer(Audio);

        Assert.Equal(["init 10"], native.Calls);
        Assert.Equal(1, subsystems.RefCount(Audio));
        Assert.Single(warnings);
    }

    [Fact]
    public async Task OwnerThread_WorkFromManyThreadsRunsOnOneThreadNeverDuringAPump()
    {
        var running = true;
        var pumping = false;
        var overlaps = 0;
        var pumps = 0;
        var threads = new HashSet<int>();
        var owner = new SdlOwnerThread(
            "test-sdl",
            () => Volatile.Read(ref running),
            () =>
            {
                Volatile.Write(ref pumping, true);
                Interlocked.Increment(ref pumps);
                Thread.Sleep(1);
                Volatile.Write(ref pumping, false);
            },
            _ => { });

        var work = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 25; i++)
            {
                await owner.InvokeAsync(() =>
                {
                    if (Volatile.Read(ref pumping)) Interlocked.Increment(ref overlaps);
                    lock (threads) threads.Add(Environment.CurrentManagedThreadId);
                    return true;
                });
            }
        }));
        await Task.WhenAll(work);
        Volatile.Write(ref running, false);
        await owner.InvokeAsync(() => true);

        Assert.Equal(0, overlaps);
        Assert.Single(threads);
        Assert.True(Volatile.Read(ref pumps) > 0);
    }

    [Fact]
    public void OwnerThread_InvokeFromTheOwnerThreadRunsInline()
    {
        var owner = new SdlOwnerThread("test-sdl", () => false, () => { }, _ => { });

        var nested = owner.Invoke(() => owner.Invoke(() => owner.IsCurrentThread));

        Assert.True(nested);
        Assert.False(owner.IsCurrentThread);
    }

    [Fact]
    public async Task OwnerThread_AFailingWorkItemFaultsItsTaskAndTheThreadCarriesOn()
    {
        var owner = new SdlOwnerThread("test-sdl", () => false, () => { }, _ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.InvokeAsync<bool>(() => throw new InvalidOperationException()));

        Assert.Equal(42, await owner.InvokeAsync(() => 42));
    }

    [Fact]
    public async Task OwnerThread_APumpFailureIsReportedAndPumpingContinues()
    {
        var failures = 0;
        var pumps = 0;
        var owner = new SdlOwnerThread(
            "test-sdl",
            () => Volatile.Read(ref pumps) < 3,
            () =>
            {
                Interlocked.Increment(ref pumps);
                throw new InvalidOperationException();
            },
            _ => Interlocked.Increment(ref failures));

        await owner.InvokeAsync(() => true);
        await owner.InvokeAsync(() => true);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref failures) < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        Assert.Equal(3, Volatile.Read(ref failures));
    }
}
