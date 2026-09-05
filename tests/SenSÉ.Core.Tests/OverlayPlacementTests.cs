using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Placement of the floating keyboard. The one rule that must never break is that it does not cover
/// the field being typed into — that was the whole problem with the old full-width bottom band.
/// </summary>
public class OverlayPlacementTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);
    private const int BoardW = 760;
    private const int BoardH = 260;

    private static ScreenRect Place(ScreenRect field, ScreenRect? screen = null)
        => OverlayPlacement.Place(BoardW, BoardH, field, screen ?? Screen).Bounds;

    /// <summary>
    /// Beside is preferred over above, and the left side over the right: the board's frame edge is
    /// the boundary with the field.
    /// </summary>
    [Fact]
    public void SitsBesideAFieldWhenThereIsRoom()
    {
        var field = new ScreenRect(700, 200, 400, 30);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        Assert.False(result.Bounds.IntersectsWith(field));
    }

    /// <summary>The floating board goes to the left of the field, flush against it.</summary>
    [Fact]
    public void PlacesLeftOfTheFieldWithAGapFromTheText()
    {
        var field = new ScreenRect(900, 400, 300, 28);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        // A gap, not flush. Edge to edge the frame still read as sitting on the text.
        Assert.Equal(field.X - OverlayPlacement.Gap, result.Bounds.Right);
        Assert.False(result.Bounds.IntersectsWith(field), $"overlap: {result.Bounds}");
    }

    /// <summary>No room on the left: the board goes above, centred on the field.</summary>
    [Fact]
    public void GoesAboveWhenThereIsNoRoomLeft()
    {
        var field = new ScreenRect(100, 400, 300, 28);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.Equal(PlacementKind.AboveField, result.Kind);
        Assert.True(result.Bounds.Bottom <= field.Y, $"still over the field: {result.Bounds}");
    }

    /// <summary>No room left and none above: the board goes to the right, flush against the field.</summary>
    [Fact]
    public void GoesRightWhenThereIsNoRoomLeftOrAbove()
    {
        var field = new ScreenRect(100, 0, 300, 28);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        Assert.Equal(field.Right + OverlayPlacement.Gap, result.Bounds.X);
        Assert.False(result.Bounds.IntersectsWith(field), $"overlap: {result.Bounds}");
    }

    /// <summary>The case the old layout got wrong: a field low on the screen.</summary>
    [Fact]
    public void KeepsClearOfAFieldNearTheBottom()
    {
        var field = new ScreenRect(700, 1000, 400, 30);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.False(result.Bounds.IntersectsWith(field), $"keyboard overlaps the field: {result.Bounds}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(400)]
    [InlineData(700)]
    [InlineData(900)]
    [InlineData(1040)]
    public void NeverCoversTheField(int fieldTop)
    {
        var field = new ScreenRect(800, fieldTop, 300, 28);

        var bounds = Place(field);

        Assert.False(bounds.IntersectsWith(field), $"overlap with a field at y={fieldTop}: {bounds}");
    }

    [Fact]
    public void StaysInsideTheWorkArea()
    {
        foreach (var x in new[] { 0, 400, 1600, 1900 })
        {
            var bounds = Place(new ScreenRect(x, 500, 200, 28));

            Assert.True(bounds.X >= Screen.X, $"escaped left: {bounds}");
            Assert.True(bounds.Right <= Screen.Right, $"escaped right: {bounds}");
            Assert.True(bounds.Y >= Screen.Y, $"escaped top: {bounds}");
            Assert.True(bounds.Bottom <= Screen.Bottom, $"escaped bottom: {bounds}");
        }
    }

    /// <summary>Placed beside, the board lines up with the field vertically rather than horizontally.</summary>
    [Fact]
    public void LinesUpVerticallyWithTheFieldWhenBeside()
    {
        var field = new ScreenRect(800, 300, 320, 28);

        var result = OverlayPlacement.Place(BoardW, BoardH, field, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);

        var fieldCentre = field.Y + (field.Height / 2);
        var boardCentre = result.Bounds.Y + (result.Bounds.Height / 2);
        Assert.True(Math.Abs(fieldCentre - boardCentre) <= 2, $"not aligned: {boardCentre} vs {fieldCentre}");
    }

    /// <summary>
    /// No caret information: behave exactly as the overlay always did — centred at the bottom of the
    /// active screen's work area, which is the fixed (non-floating) mode's placement.
    /// </summary>
    [Fact]
    public void FallsBackToTheScreenBottomWithoutAField()
    {
        var result = OverlayPlacement.Place(BoardW, BoardH, default, Screen);

        Assert.Equal(PlacementKind.ScreenBottom, result.Kind);
        Assert.True(result.Bounds.Bottom <= Screen.Bottom);

        var screenCentre = Screen.X + (Screen.Width / 2);
        var boardCentre = result.Bounds.X + (result.Bounds.Width / 2);
        Assert.True(Math.Abs(screenCentre - boardCentre) <= 1, $"not centred: {result.Bounds}");
    }

    /// <summary>
    /// A maximised editor: the text fills the screen and there is provably no free band. Overlap is
    /// then unavoidable, and must be reported as such rather than dressed up as a clean placement.
    /// </summary>
    [Fact]
    public void OverlapsOnlyWhenNothingFits()
    {
        var wholeWindow = new ScreenRect(0, 0, 1920, 900);

        var result = OverlayPlacement.Place(BoardW, BoardH, wholeWindow, isCaret: false, Screen);

        Assert.Equal(PlacementKind.Overlapping, result.Kind);

        // Furthest from the top of the text, where a document starts.
        Assert.True(result.Bounds.Y > wholeWindow.Y + (wholeWindow.Height / 2), $"landed high: {result.Bounds}");
    }

    /// <summary>
    /// The real measurement: Notepad maximised on a 2560x1392 work area leaves 5 px a side and a
    /// 126 px band on top. Nothing fits, and the board must still stay on that monitor.
    /// </summary>
    [Fact]
    public void MaximisedEditorStaysOnItsOwnMonitor()
    {
        var work = new ScreenRect(3440, 0, 2560, 1392);
        var editArea = new ScreenRect(3445, 126, 2550, 1261);

        var result = OverlayPlacement.Place(1012, 345, editArea, isCaret: false, work);

        Assert.Equal(PlacementKind.Overlapping, result.Kind);
        Assert.True(result.Bounds.X >= work.X, $"escaped left: {result.Bounds}");
        Assert.True(result.Bounds.Right <= work.Right, $"escaped right: {result.Bounds}");
    }

    /// <summary>
    /// The same editor windowed rather than maximised: there is now room beside it, so the board
    /// must leave the text alone entirely.
    /// </summary>
    [Fact]
    public void WindowedEditorGetsASidePlacement()
    {
        var work = new ScreenRect(0, 0, 3440, 1392);
        var editArea = new ScreenRect(200, 150, 1400, 900);

        var result = OverlayPlacement.Place(1012, 345, editArea, isCaret: false, work);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        Assert.False(result.Bounds.IntersectsWith(editArea), $"still on the text: {result.Bounds}");
    }

    /// <summary>
    /// The caret is never covered, however large the control around it. This is the Word and Notepad
    /// case: the document fills the screen, but the line being typed must stay visible.
    /// </summary>
    [Fact]
    public void NeverCoversTheCaretEvenInsideAFullScreenDocument()
    {
        var caretInAWordDocument = new ScreenRect(600, 320, 2, 40);

        var result = OverlayPlacement.Place(BoardW, BoardH, caretInAWordDocument, isCaret: true, Screen);

        Assert.NotEqual(PlacementKind.ScreenBottom, result.Kind);
        Assert.False(result.Bounds.IntersectsWith(caretInAWordDocument), $"covered the caret: {result.Bounds}");
    }

    /// <summary>A caret low in a document is dodged sideways, never covered.</summary>
    [Fact]
    public void KeepsClearOfACaretNearTheBottom()
    {
        var caret = new ScreenRect(600, 1010, 2, 30);

        var result = OverlayPlacement.Place(BoardW, BoardH, caret, isCaret: true, Screen);

        Assert.False(result.Bounds.IntersectsWith(caret));
    }

    /// <summary>A board wider than the screen is clamped rather than pushed off the edge.</summary>
    [Fact]
    public void ShrinksToFitANarrowScreen()
    {
        var narrow = new ScreenRect(0, 0, 600, 400);

        var bounds = OverlayPlacement.Place(BoardW, BoardH, new ScreenRect(100, 100, 200, 28), narrow).Bounds;

        Assert.True(bounds.Width <= narrow.Width, $"too wide: {bounds}");
        Assert.True(bounds.X >= narrow.X && bounds.Right <= narrow.Right, $"escaped: {bounds}");
    }

    /// <summary>Work areas do not start at zero on a multi-monitor desktop.</summary>
    [Fact]
    public void RespectsAnOffsetWorkArea()
    {
        var secondary = new ScreenRect(1920, 0, 1280, 1024);

        var bounds = Place(new ScreenRect(2200, 400, 300, 28), secondary);

        Assert.True(bounds.X >= secondary.X, $"escaped onto the other monitor: {bounds}");
        Assert.True(bounds.Right <= secondary.Right, $"escaped right: {bounds}");
    }

    /// <summary>
    /// A real two-monitor desktop: 3440x1440 primary with a 2560x1440 to its right.
    /// </summary>
    /// <remarks>
    /// The overlay window used to be created as a rectangle from (0,0) to the primary monitor's size.
    /// A board correctly placed at x=4000 was then drawn outside that window and simply never
    /// appeared. The placement was never the problem, which is why this asserts the coordinates land
    /// on the second monitor rather than being clamped back onto the first.
    /// </remarks>
    [Fact]
    public void PlacesOnASecondMonitorToTheRight()
    {
        var secondary = new ScreenRect(3440, 0, 2560, 1440);
        var field = new ScreenRect(4200, 600, 300, 28);

        var bounds = OverlayPlacement.Place(1012, 345, field, secondary).Bounds;

        Assert.True(bounds.X >= secondary.X, $"clamped back onto the primary monitor: {bounds}");
        Assert.True(bounds.Right <= secondary.Right, $"ran off the right edge: {bounds}");
        Assert.False(bounds.IntersectsWith(field));
    }

    /// <summary>
    /// A monitor to the left of the primary one gives the virtual desktop a negative origin, which is
    /// what the screen-to-client conversion in the overlay exists to handle.
    /// </summary>
    [Fact]
    public void PlacesOnAMonitorWithNegativeCoordinates()
    {
        var leftOfPrimary = new ScreenRect(-1920, -200, 1920, 1080);
        var field = new ScreenRect(-1200, 300, 300, 28);

        var bounds = OverlayPlacement.Place(1012, 345, field, leftOfPrimary).Bounds;

        Assert.True(bounds.X >= leftOfPrimary.X, $"escaped left: {bounds}");
        Assert.True(bounds.Right <= leftOfPrimary.Right, $"escaped right: {bounds}");
        Assert.True(bounds.Y >= leftOfPrimary.Y, $"escaped top: {bounds}");
        Assert.False(bounds.IntersectsWith(field));
    }

    /// <summary>
    /// A text area filling the height of the screen — a maximised Notepad or Word document. There is
    /// no room above or below the caret's own line, so the board must go to the side rather than sit
    /// on the text being typed.
    /// </summary>
    [Fact]
    public void MovesBesideAFieldFillingTheScreenHeight()
    {
        var documentColumn = new ScreenRect(200, 0, 700, 1080);

        var result = OverlayPlacement.Place(BoardW, BoardH, documentColumn, isCaret: true, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        Assert.False(result.Bounds.IntersectsWith(documentColumn), $"still on the text: {result.Bounds}");
    }

    /// <summary>Beside means the left side first, whatever the room on the right.</summary>
    [Fact]
    public void PrefersTheLeftSideWhenItFits()
    {
        var againstTheRightEdge = new ScreenRect(1300, 0, 600, 1080);

        var result = OverlayPlacement.Place(BoardW, BoardH, againstTheRightEdge, isCaret: true, Screen);

        Assert.Equal(PlacementKind.BesideField, result.Kind);
        Assert.Equal(againstTheRightEdge.X - OverlayPlacement.Gap, result.Bounds.Right);
    }

    /// <summary>A field wedged against the bottom leaves no room either side; it still must not be covered.</summary>
    [Fact]
    public void HandlesAFieldWithNoRoomOnEitherSide()
    {
        var tall = new ScreenRect(0, 0, 1920, 400);
        var field = new ScreenRect(800, 180, 300, 28);

        var bounds = OverlayPlacement.Place(BoardW, BoardH, field, tall).Bounds;

        Assert.True(bounds.Y >= tall.Y && bounds.Bottom <= tall.Bottom, $"escaped: {bounds}");
    }
}
