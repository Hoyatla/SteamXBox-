using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// One virtual Xbox pad per physical controller.
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

    private const string PadA = "hid:pad-a";
    private const string PadB = "xinput-slot:1";

    private static (VirtualPadSet Set, List<FakePad> Made) Build()
    {
        var made = new List<FakePad>();
        var set = new VirtualPadSet(() =>
        {
            var pad = new FakePad();
            made.Add(pad);
            return pad;
        });

        return (set, made);
    }

    // The whole point: two controllers, two distinct virtual pads.
    [Fact]
    public async Task EachControllerGetsItsOwnPad()
    {
        var (set, made) = Build();

        var first = await set.ForAsync(PadA, default);
        var second = await set.ForAsync(PadB, default);

        Assert.NotSame(first, second);
        Assert.Equal(2, made.Count);
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public async Task TheSameControllerAlwaysGetsTheSamePad()
    {
        var (set, made) = Build();

        var first = await set.ForAsync(PadA, default);
        var again = await set.ForAsync(PadA, default);

        Assert.Same(first, again);
        Assert.Single(made);
    }

    [Fact]
    public async Task APadIsConnectedExactlyOnce()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadA, default);

        Assert.Equal(1, made[0].Connects);
    }

    // A controller attached but never touched would otherwise appear to every game as a connected
    // player — a phantom occupying a split-screen slot.
    [Fact]
    public async Task NoPadExistsUntilAControllerSendsSomething()
    {
        var (set, made) = Build();

        Assert.Equal(0, set.Count);
        Assert.Empty(made);

        await set.ForAsync(PadA, default);

        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task OneControllersInputGoesOnlyToItsOwnPad()
    {
        var (set, made) = Build();

        var a = await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);

        await a.SubmitAsync(Xbox360Report.Neutral with { LeftThumbX = 100 }, default);

        Assert.Single(made[0].Submitted);
        Assert.Empty(made[1].Submitted);
    }

    // Sending neutral to only the pad that sent the last frame leaves the other players holding
    // whatever they were last told — a stuck stick or a held trigger.
    [Fact]
    public async Task ATransitionReachesEveryPad()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);

        await set.SubmitAllAsync(Xbox360Report.Neutral, default);

        Assert.All(made, pad => Assert.Single(pad.Submitted));
    }

    [Fact]
    public async Task SubmittingToNoPadsIsHarmless()
    {
        var (set, _) = Build();

        await set.SubmitAllAsync(Xbox360Report.Neutral, default);

        Assert.Equal(0, set.Count);
    }

    // Left connected, a departed controller's pad stays visible to games as a player who never
    // presses anything.
    [Fact]
    public async Task AControllerThatLeavesReleasesItsPad()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA);

        Assert.Equal(0, set.Count);
        Assert.True(made[0].Disposed);
    }

    // A pad disposed while holding a deflected stick leaves the game reading that deflection.
    [Fact]
    public async Task AReleasedPadIsNeutralisedFirst()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA);

        Assert.Equal(Xbox360Report.Neutral, made[0].Submitted[^1]);
    }

    [Fact]
    public async Task ForgettingAnUnknownControllerIsHarmless()
    {
        var (set, _) = Build();

        await set.ForgetAsync("never-seen");

        Assert.Equal(0, set.Count);
    }

    [Fact]
    public async Task AControllerThatComesBackGetsAFreshPad()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForgetAsync(PadA);
        await set.ForAsync(PadA, default);

        Assert.Equal(2, made.Count);
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public async Task DisposingReleasesEveryPad()
    {
        var (set, made) = Build();

        await set.ForAsync(PadA, default);
        await set.ForAsync(PadB, default);
        await set.DisposeAsync();

        Assert.Equal(0, set.Count);
        Assert.All(made, pad => Assert.True(pad.Disposed));
    }
}
