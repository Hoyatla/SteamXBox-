using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// One virtual pad per physical controller, shaped for the controller's family.
/// </summary>
/// <remarks>
/// Reading several controllers at once is only half of split-screen. If they all feed one virtual
/// pad the game still sees a single player, and two people pressing at once produce one incoherent
/// stream rather than two players.
///
/// ViGEm is not exercised here — it needs a driver and real hardware. What is worth testing is the
/// bookkeeping, and none of that is about ViGEm.
/// </remarks>
public class VirtualPadSetTests
{
    private sealed class FakePad : IVirtualXbox360Sink
    {
        public int Connects { get; private set; }

        public List<Xbox360Report> Submitted { get; } = [];

        public bool Disposed { get; private set; }

        public ControllerIdentity Identity { get; set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            Connects++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitAsync(Xbox360Report report, CancellationToken cancellationToken)
        {
            Submitted.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDS4Pad : IVirtualDS4Sink
    {
        public int Connects { get; private set; }

        public List<DS4Report> Submitted { get; } = [];

        public bool Disposed { get; private set; }

        public ControllerIdentity Identity { get; set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            Connects++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitAsync(DS4Report report, CancellationToken cancellationToken)
        {
            Submitted.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static readonly ControllerIdentity PadA = new(
        ControllerKind.XInput, "hid:pad-a", "Pad A", Slot: 0);
    private static readonly ControllerIdentity PadB = new(
        ControllerKind.XInput, "xinput-slot:1", "Pad B", Slot: 1);

    private static (VirtualPadSet Set, List<FakePad> XboxMade, List<FakeDS4Pad> DS4Made) Build()
    {
        var xboxMade = new List<FakePad>();
        var ds4Made = new List<FakeDS4Pad>();
        var set = new VirtualPadSet(
            identity =>
            {
                var pad = new FakePad { Identity = identity };
                xboxMade.Add(pad);
                return pad;
            },
            identity =>
            {
                var pad = new FakeDS4Pad { Identity = identity };
                ds4Made.Add(pad);
                return pad;
            });

        return (set, xboxMade, ds4Made);
    }

    // The whole point: two controllers, two distinct virtual pads.
    [Fact]
    public async Task EachControllerGetsItsOwnPad()
    {
        var (set, made, _) = Build();

        var first = await set.ForAsync(PadA, default);
        var second = await set.ForAsync(PadB, default);

        Assert.NotSame(first, second);
        Assert.Equal(2, made.Count);
        Assert.Equal(2, set.Count);
    }

    // The pad's rumble has to reach the physical controller that owns it, so the factory receives
    // the whole identity — the XInput slot and the kind — not just the dictionary key.
    [Fact]
    public async Task ThePadFactorySeesTheOwningControllersIdentity()
    {
        var (set, made, ds4Made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForDS4Async(PadB, default);

        Assert.Equal(PadA, made[0].Identity);
        Assert.Equal(PadB, ds4Made[0].Identity);
        Assert.Equal(0, made[0].Identity.Slot);
        Assert.Equal(ControllerKind.XInput, made[0].Identity.Kind);
    }

    [Fact]
    public async Task TheSameControllerAlwaysGetsTheSamePad()
    {
        var (set, made, _) = Build();

        var first = await set.ForAsync(PadA, default);
        var again = await set.ForAsync(PadA, default);

        Assert.Same(first, again);
        Assert.Single(made);
    }

    [Fact]
    public async Task APadIsConnectedExactlyOnce()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadA, default);

        Assert.Equal(1, made[0].Connects);
    }

    // A controller attached but never touched would otherwise appear to every game as a connected
    // player — a phantom occupying a split-screen slot.
    [Fact]
    public async Task NoPadExistsUntilAControllerSendsSomething()
    {
        var (set, made, ds4Made) = Build();

        Assert.Equal(0, set.Count);
        Assert.Empty(made);
        Assert.Empty(ds4Made);

        await set.ForAsync(PadA, default);

        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task OneControllersInputGoesOnlyToItsOwnPad()
    {
        var (set, made, _) = Build();

        var a = await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);

        await a.SubmitAsync(Xbox360Report.Neutral with { LeftThumbX = 100 }, default);

        Assert.Single(made[0].Submitted);
        Assert.Empty(made[1].Submitted);
    }

    [Fact]
    public async Task TargetedNeutralisationDoesNotReachAnotherPad()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);

        await set.NeutralizeForAsync(PadA.Id, default);

        Assert.Single(made[0].Submitted);
        Assert.Empty(made[1].Submitted);
    }

    // Sending neutral to only the pad that sent the last frame leaves the other players holding
    // whatever they were last told — a stuck stick or a held trigger.
    [Fact]
    public async Task ATransitionReachesEveryPad()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);

        await set.NeutralizeAllAsync(default);

        Assert.All(made, pad => Assert.Single(pad.Submitted));
    }

    [Fact]
    public async Task SubmittingToNoPadsIsHarmless()
    {
        var (set, _, _) = Build();

        await set.NeutralizeAllAsync(default);

        Assert.Equal(0, set.Count);
    }

    // Left connected, a departed controller's pad stays visible to games as a player who never
    // presses anything.
    [Fact]
    public async Task AControllerThatLeavesReleasesItsPad()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA.Id);

        Assert.Equal(0, set.Count);
        Assert.True(made[0].Disposed);
    }

    // A pad disposed while holding a deflected stick leaves the game reading that deflection.
    [Fact]
    public async Task AReleasedPadIsNeutralisedFirst()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA.Id);

        Assert.Equal(Xbox360Report.Neutral, made[0].Submitted[^1]);
    }

    [Fact]
    public async Task ForgettingAnUnknownControllerIsHarmless()
    {
        var (set, _, _) = Build();

        await set.ForgetAsync("never-seen");

        Assert.Equal(0, set.Count);
    }

    [Fact]
    public async Task AControllerThatComesBackGetsAFreshPad()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA.Id);
        await set.ForAsync(PadA, default);

        Assert.Equal(2, made.Count);
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task DisposingReleasesEveryPad()
    {
        var (set, made, _) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);
        await set.DisposeAsync();

        Assert.Equal(0, set.Count);
        Assert.All(made, pad => Assert.True(pad.Disposed));
    }

    // A DualSense gets a DualShock 4 pad, on a separate dictionary from the Xbox ones: the same
    // controller id must never own two pads, one per family.
    [Fact]
    public async Task ADualSenseControllerGetsADualShock4Pad()
    {
        var (set, xboxMade, ds4Made) = Build();

        var pad = await set.ForDS4Async(PadA, default);

        Assert.IsType<FakeDS4Pad>(pad);
        Assert.Empty(xboxMade);
        Assert.Single(ds4Made);
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task TheSameControllerCanOnlyHaveOnePadOfEachFamily()
    {
        var (set, xboxMade, ds4Made) = Build();

        var ds4 = await set.ForDS4Async(PadA, default);
        var again = await set.ForDS4Async(PadA, default);

        Assert.Same(ds4, again);
        Assert.Single(ds4Made);
        Assert.Empty(xboxMade);
    }

    [Fact]
    public async Task HasIsTrueForEitherFamily()
    {
        var (set, _, _) = Build();

        await set.ForDS4Async(PadA, default);

        Assert.True(set.Has(PadA.Id));
    }

    // The neutral report a family's pad receives is its own: an Xbox neutral to a DualShock 4 pad
    // would be a report it cannot mean.
    [Fact]
    public async Task TargetedNeutralisationSpeaksEachFamilysReport()
    {
        var (set, xboxMade, ds4Made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForDS4Async(PadB, default);

        await set.NeutralizeForAsync(PadA.Id, default);
        await set.NeutralizeForAsync(PadB.Id, default);

        Assert.Equal(Xbox360Report.Neutral, xboxMade[0].Submitted[^1]);
        Assert.Equal(DS4Report.Neutral, ds4Made[0].Submitted[^1]);
    }

    [Fact]
    public async Task ForgettingReleasesEitherFamily()
    {
        var (set, xboxMade, ds4Made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForDS4Async(PadB, default);
        await set.ForgetAsync(PadB.Id);

        Assert.Equal(1, set.Count);
        Assert.False(xboxMade[0].Disposed);
        Assert.True(ds4Made[0].Disposed);
    }
}

