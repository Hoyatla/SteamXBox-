using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Where the keyboard goes, fixed and floating.
/// </summary>
/// <remarks>
/// Two behaviours, and they answer different needs. Fixed is for a user who wants the keyboard in
/// the same place every time, so the eye and the thumb both learn it. Floating is for one who wants
/// it next to what they are typing, and never on top of it.
/// </remarks>
public class OverlayPlacementFixedTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1040);

    // Fixed means fixed: bottom centre, whatever the field is doing.
    [Fact]
    public void FixedSitsAtTheBottomCentre()
    {
        var placed = OverlayPlacement.PlaceFixed(800, 300, Screen).Bounds;

        Assert.Equal((1920 - 800) / 2, placed.X);
        Assert.Equal(1040 - OverlayPlacement.ScreenMargin - 300, placed.Y);
    }

    // The only thing that varies is which screen: a keyboard pinned to the primary monitor is
    // useless to somebody typing on the second one.
    [Fact]
    public void FixedFollowsTheScreenItIsGiven()
    {
        var second = new ScreenRect(1920, 0, 2560, 1400);
        var placed = OverlayPlacement.PlaceFixed(800, 300, second).Bounds;

        Assert.True(placed.X >= second.X);
        Assert.True(placed.Right <= second.Right);
        Assert.Equal(1400 - OverlayPlacement.ScreenMargin - 300, placed.Y);
    }

    [Fact]
    public void FixedNeverOverflowsASmallScreen()
    {
        var tiny = new ScreenRect(0, 0, 400, 200);
        var placed = OverlayPlacement.PlaceFixed(800, 300, tiny).Bounds;

        Assert.True(placed.Width <= tiny.Width);
        Assert.True(placed.Height <= tiny.Height);
    }

    // Floating: left first, and clear of the text rather than flush against it.
    [Fact]
    public void FloatingPrefersTheLeftAndLeavesAGap()
    {
        var field = new ScreenRect(1000, 400, 500, 40);
        var placed = OverlayPlacement.Place(600, 300, field, isCaret: true, Screen);

        Assert.Equal(PlacementKind.BesideField, placed.Kind);
        Assert.True(placed.Bounds.Right <= field.X - OverlayPlacement.Gap);
        Assert.False(placed.Bounds.IntersectsWith(field));
    }

    [Fact]
    public void FloatingGoesAboveWhenTheLeftIsTooNarrow()
    {
        var field = new ScreenRect(120, 700, 1600, 40);
        var placed = OverlayPlacement.Place(600, 300, field, isCaret: true, Screen);

        Assert.Equal(PlacementKind.AboveField, placed.Kind);
        Assert.True(placed.Bounds.Bottom <= field.Y);
    }

    [Fact]
    public void FloatingGoesRightWhenNeitherLeftNorAboveFits()
    {
        var field = new ScreenRect(60, 40, 700, 900);
        var placed = OverlayPlacement.Place(600, 300, field, isCaret: true, Screen);

        Assert.Equal(PlacementKind.BesideField, placed.Kind);
        Assert.True(placed.Bounds.X >= field.Right + OverlayPlacement.Gap);
    }

    // The frame must not sit on the typing area in any of the three.
    [Theory]
    [InlineData(1000, 400, 500, 40)]
    [InlineData(120, 700, 1600, 40)]
    [InlineData(60, 40, 700, 900)]
    public void TheBoardNeverTouchesTheField(int x, int y, int w, int h)
    {
        var field = new ScreenRect(x, y, w, h);
        var placed = OverlayPlacement.Place(600, 300, field, isCaret: true, Screen);

        Assert.False(placed.Bounds.IntersectsWith(field));
    }

    [Fact]
    public void AnUnknownFieldFallsBackToTheBottom()
        => Assert.Equal(
            PlacementKind.ScreenBottom,
            OverlayPlacement.Place(600, 300, new ScreenRect(0, 0, 0, 0), isCaret: false, Screen).Kind);

    // The case the author described: a caret at the top-left of a full-screen text area. No room to
    // its left, none above, none to its right — but everything under it is blank, because writing
    // runs rightwards and downwards and has not got there yet.
    [Fact]
    public void ACaretHighInAFullScreenFieldPutsTheBoardBelowIt()
    {
        var caretTopLeft = new ScreenRect(10, 20, 2, 24);
        var placed = OverlayPlacement.Place(600, 300, caretTopLeft, isCaret: true, Screen);

        Assert.Equal(PlacementKind.BelowField, placed.Kind);
        Assert.True(placed.Bounds.Y >= caretTopLeft.Bottom);
        Assert.False(placed.Bounds.IntersectsWith(caretTopLeft));
    }

    // Below is a fallback, not a preference: a caret with room to its left still goes left.
    [Fact]
    public void BelowIsOnlyUsedWhenTheOthersDoNotFit()
    {
        var caretWithRoomLeft = new ScreenRect(1200, 40, 2, 24);

        Assert.Equal(
            PlacementKind.BesideField,
            OverlayPlacement.Place(600, 300, caretWithRoomLeft, isCaret: true, Screen).Kind);
    }

    // A caret low on the screen has no room below either, and must not be pushed off it.
    [Fact]
    public void ACaretNearTheBottomDoesNotGoBelow()
    {
        var caretLow = new ScreenRect(10, 1000, 2, 24);
        var placed = OverlayPlacement.Place(600, 300, caretLow, isCaret: true, Screen);

        Assert.NotEqual(PlacementKind.BelowField, placed.Kind);
        Assert.True(placed.Bounds.Bottom <= Screen.Bottom);
    }
}
