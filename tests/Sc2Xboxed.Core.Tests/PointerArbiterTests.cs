using Sc2Xboxed.Core.Mapping;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The one pointer, shared between however many controllers are pushing it.
/// </summary>
/// <remarks>
/// No controller owns the pointer and none is locked out. Two pushing at once cancel, because the
/// alternatives fail invisibly: electing an owner makes the second player's pad look dead, and
/// summing produces drift neither player asked for.
/// </remarks>
public class PointerArbiterTests
{
    private const string PadA = "hid:pad-a";
    private const string PadB = "xinput-slot:1";
    private const string PadC = "xinput-slot:2";

    // The ordinary case, and the one that must not regress while making room for the others.
    [Fact]
    public void OneControllerMovesThePointer()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 12, -7, 0);

        Assert.Equal((12, -7, 0, 0), arbiter.Resolve());
    }

    [Fact]
    public void ItDoesNotMatterWhichControllerItIs()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadC, 3, 4, 0);

        Assert.Equal((3, 4, 0, 0), arbiter.Resolve());
    }

    // The decision this class exists for.
    [Fact]
    public void TwoControllersPushingAtOnceStallThePointer()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 10, 0, 0);
        arbiter.Offer(PadB, 10, 0, 0);

        Assert.Equal((0, 0, 0, 0), arbiter.Resolve());
    }

    // Specifically not summing: same direction is still contention, not agreement.
    [Fact]
    public void AgreeingControllersStallJustTheSame()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 5, 5, 0);
        arbiter.Offer(PadB, 5, 5, 0);

        Assert.Equal((0, 0, 0, 0), arbiter.Resolve());
    }

    [Fact]
    public void ThePointerFreesUpAsSoonAsOneLetsGo()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 8, 0, 0);
        arbiter.Offer(PadB, -8, 0, 0);
        arbiter.Resolve();

        arbiter.Offer(PadA, 8, 0, 0);

        Assert.Equal((8, 0, 0, 0), arbiter.Resolve());
    }

    // Several frames arrive from one pad between two resolutions; they are one intent, not several.
    [Fact]
    public void RepeatedOffersFromOneControllerAccumulate()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 3, 1, 0);
        arbiter.Offer(PadA, 4, 1, 0);

        Assert.Equal((7, 2, 0, 0), arbiter.Resolve());
    }

    // A thumb resting on a stick produces scroll long before it produces travel, so a stray notch
    // must not be able to freeze another player's pointer.
    [Fact]
    public void ScrollDoesNotBlockAnotherControllersMotion()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 0, 0, 2);
        arbiter.Offer(PadB, 9, 0, 0);

        Assert.Equal((9, 0, 2, 0), arbiter.Resolve());
    }

    [Fact]
    public void TwoControllersScrollingAtOnceStallTheWheel()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 0, 0, 2);
        arbiter.Offer(PadB, 0, 0, -2);

        Assert.Equal((0, 0, 0, 0), arbiter.Resolve());
    }

    [Fact]
    public void HorizontalScrollPassesThroughBesideMotion()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 5, 0, 0, 3);

        Assert.Equal((5, 0, 0, 3), arbiter.Resolve());
    }

    [Fact]
    public void TwoControllersHorizontalScrollingAtOnceStallIt()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 0, 0, 0, 2);
        arbiter.Offer(PadB, 0, 0, 0, -2);

        Assert.Equal((0, 0, 0, 0), arbiter.Resolve());
    }

    [Fact]
    public void AnIdleOfferIsNotContention()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 0, 0, 0);
        arbiter.Offer(PadB, 6, 0, 0);

        Assert.Equal((6, 0, 0, 0), arbiter.Resolve());
        Assert.Equal(0, arbiter.Contenders);
    }

    [Fact]
    public void NothingOfferedMovesNothing()
        => Assert.Equal((0, 0, 0, 0), new PointerArbiter().Resolve());

    [Fact]
    public void ResolvingClearsTheSlate()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 5, 5, 1);
        arbiter.Resolve();

        Assert.Equal((0, 0, 0, 0), arbiter.Resolve());
    }

    // A pad that disconnects mid-push would otherwise contend forever: a pointer frozen by a
    // controller that is no longer in the room.
    [Fact]
    public void AControllerThatLeavesStopsContending()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 10, 0, 0);
        arbiter.Offer(PadB, 10, 0, 0);
        arbiter.Forget(PadB);

        Assert.Equal((10, 0, 0, 0), arbiter.Resolve());
    }

    [Fact]
    public void ContendersCountsOnlyThoseAskingToMove()
    {
        var arbiter = new PointerArbiter();
        arbiter.Offer(PadA, 1, 0, 0);
        arbiter.Offer(PadB, 0, 0, 3);

        Assert.Equal(1, arbiter.Contenders);
    }
}
