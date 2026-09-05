using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Anchors that let two sticks aim at a wide keyboard with comparable reach in every direction.
/// </summary>
public class StickAnchorLayoutTests
{
    // The overlay keyboard: five rows, twelve columns at its widest.
    private const int Columns = 12;
    private const int Rows = 5;

    private static IReadOnlyList<StickAnchor> Board() => StickAnchorLayout.Build(Columns, Rows);

    [Fact]
    public void TwoAnchorsOnePerStick()
    {
        Assert.Equal(2, Board().Count);
        Assert.Equal(2, StickAnchorLayout.AnchorCount);
        Assert.Equal(1, StickAnchorLayout.AnchorsPerStick);
    }

    [Fact]
    public void TheBandsCoverEveryColumnWithoutOverlapping()
    {
        var anchors = Board();

        Assert.Equal(0, anchors[0].FirstColumn);
        Assert.Equal(Columns - 1, anchors[^1].LastColumn);

        for (var i = 1; i < anchors.Count; i++)
        {
            Assert.Equal(anchors[i - 1].LastColumn + 1, anchors[i].FirstColumn);
        }
    }

    // The reason four vertical bands were chosen over a two-by-two grid: three columns against five
    // rows is far closer to equal reach than six columns against two and a half.
    [Fact]
    public void EachBandIsRoughlyAsWideAsTheBoardIsTall()
    {
        foreach (var anchor in Board())
        {
            var width = anchor.LastColumn - anchor.FirstColumn + 1;
            var ratio = (double)Math.Max(width, Rows) / Math.Min(width, Rows);

            Assert.True(ratio < 2.0, $"band of {width} against {Rows} rows is too anisotropic");
        }
    }

    // An odd board gives the extra column to the left band rather than the right: 13 becomes 7 and
    // 6, never 6 and 7, so the split is at least predictable.
    [Fact]
    public void TheRemainderGoesToTheLeftmostBand()
    {
        var anchors = StickAnchorLayout.Build(13, Rows);
        var widths = anchors.Select(a => a.LastColumn - a.FirstColumn + 1).ToArray();

        Assert.Equal(new[] { 7, 6 }, widths);
    }

    // Both sticks are live at once, each on its own half: a word is typed by alternating hands, not
    // by walking one selection across the board.
    [Fact]
    public void TheLeftStickOwnsTheLeftHalf()
    {
        var anchors = Board();
        var left = anchors[StickAnchorLayout.AnchorFor(leftStick: true)];
        var right = anchors[StickAnchorLayout.AnchorFor(leftStick: false)];

        Assert.Equal(0, left.FirstColumn);
        Assert.Equal(Columns - 1, right.LastColumn);
        Assert.True(left.LastColumn < right.FirstColumn);
    }

    // The stick springs back on its own; the selection coming to rest somewhere predictable is what
    // makes typing without watching possible.
    [Fact]
    public void ACentredStickRestsOnItsAnchor()
    {
        foreach (var anchor in Board())
        {
            Assert.Equal(anchor.Home, StickAnchorLayout.Resolve(anchor, 0, 0, Rows));
        }
    }

    [Fact]
    public void FullDeflectionReachesTheEdgesOfTheBand()
    {
        var anchor = Board()[1];

        Assert.Equal(anchor.FirstColumn, StickAnchorLayout.Resolve(anchor, -1, 0, Rows).Column);
        Assert.Equal(anchor.LastColumn, StickAnchorLayout.Resolve(anchor, 1, 0, Rows).Column);
    }

    // Sticks report up as positive; rows count downwards.
    [Fact]
    public void PushingUpSelectsTheTopRow()
    {
        var anchor = Board()[0];

        Assert.Equal(0, StickAnchorLayout.Resolve(anchor, 0, 1, Rows).Row);
        Assert.Equal(Rows - 1, StickAnchorLayout.Resolve(anchor, 0, -1, Rows).Row);
    }

    // A stick can be pushed slightly past its calibrated range; that must not select a key outside
    // the band, which would belong to the other stick.
    [Fact]
    public void OverTravelStaysInsideTheBand()
    {
        var anchor = Board()[1];
        var far = StickAnchorLayout.Resolve(anchor, 3.5, -4.0, Rows);

        Assert.InRange(far.Column, anchor.FirstColumn, anchor.LastColumn);
        Assert.InRange(far.Row, 0, Rows - 1);
    }

    [Fact]
    public void EveryColumnOfABandIsReachable()
    {
        var anchor = Board()[0];
        var reached = new HashSet<int>();

        for (var push = -1.0; push <= 1.0; push += 0.05)
        {
            reached.Add(StickAnchorLayout.Resolve(anchor, push, 0, Rows).Column);
        }

        for (var column = anchor.FirstColumn; column <= anchor.LastColumn; column++)
        {
            Assert.Contains(column, reached);
        }
    }

    [Fact]
    public void EveryRowIsReachable()
    {
        var anchor = Board()[0];
        var reached = new HashSet<int>();

        for (var push = -1.0; push <= 1.0; push += 0.05)
        {
            reached.Add(StickAnchorLayout.Resolve(anchor, 0, push, Rows).Row);
        }

        Assert.Equal(Rows, reached.Count);
    }

    // A board too narrow to divide is still navigable, just coarsely — refusing to build would make
    // the keyboard unusable rather than merely imprecise.
    [Fact]
    public void ABoardTooNarrowToDivideStillGetsOneAnchor()
    {
        var anchors = StickAnchorLayout.Build(1, Rows);

        Assert.Single(anchors);
        Assert.Equal(0, anchors[0].FirstColumn);
        Assert.Equal(0, anchors[0].LastColumn);
    }

    // An odd band puts its anchor off centre — column 2 of a band spanning 0 to 5. Scaling both
    // sides by half the band would leave the far column reachable only at exactly full deflection.
    [Fact]
    public void BothEdgesAreReachedWithTheSamePush()
    {
        var anchor = Board()[0];

        Assert.Equal(anchor.FirstColumn, StickAnchorLayout.Resolve(anchor, -1, 0, Rows).Column);
        Assert.Equal(anchor.LastColumn, StickAnchorLayout.Resolve(anchor, 1, 0, Rows).Column);

        // And just short of full travel still reaches it, rather than falling a column short.
        Assert.Equal(anchor.LastColumn, StickAnchorLayout.Resolve(anchor, 0.97, 0, Rows).Column);
    }
}
