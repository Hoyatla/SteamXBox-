using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The keyboard dodging the mouse pointer. Two properties matter: it must get out from under the
/// pointer, and it must move away from it rather than towards it — a board that follows the cursor
/// would be worse than one that stays put.
/// </summary>
public class OverlayAvoidanceTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1080);
    private static readonly ScreenRect Board = new(450, 400, 1012, 345);

    private static bool Contains(ScreenRect rect, int x, int y)
        => x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;

    [Fact]
    public void StaysPutWhenThePointerIsFarAway()
    {
        Assert.Null(OverlayAvoidance.Dodge(Board, 100, 100, Work));
        Assert.Null(OverlayAvoidance.Dodge(Board, 1800, 950, Work));
    }

    /// <summary>Ten pixels away is already too close: that is the whole point of the margin.</summary>
    [Fact]
    public void MovesWhenThePointerEntersTheMargin()
    {
        var justAbove = OverlayAvoidance.Dodge(Board, 900, Board.Y - 5, Work);

        Assert.NotNull(justAbove);
    }

    [Fact]
    public void DoesNotMoveForAPointerJustOutsideTheMargin()
    {
        Assert.Null(OverlayAvoidance.Dodge(Board, 900, Board.Y - OverlayAvoidance.Margin - 1, Work));
    }

    /// <summary>The pointer must end up outside the board, whichever way it came in.</summary>
    [Theory]
    [InlineData(900, 420)]   // entering from the top
    [InlineData(900, 720)]   // from the bottom
    [InlineData(470, 550)]   // from the left
    [InlineData(1440, 550)]  // from the right
    [InlineData(960, 570)]   // dead centre
    public void PointerEndsUpOutsideTheBoard(int cursorX, int cursorY)
    {
        var moved = OverlayAvoidance.Dodge(Board, cursorX, cursorY, Work);

        Assert.NotNull(moved);
        Assert.False(Contains(moved!.Value, cursorX, cursorY), $"pointer still inside: {moved}");
    }

    /// <summary>
    /// It moves away from the pointer, never towards it. A board that closed the distance would read
    /// as chasing the cursor.
    /// </summary>
    [Theory]
    [InlineData(900, 420)]
    [InlineData(900, 720)]
    [InlineData(470, 550)]
    [InlineData(1440, 550)]
    public void MovesAwayFromThePointerNotTowardsIt(int cursorX, int cursorY)
    {
        var before = Distance(Board, cursorX, cursorY);
        var moved = OverlayAvoidance.Dodge(Board, cursorX, cursorY, Work)!.Value;
        var after = Distance(moved, cursorX, cursorY);

        Assert.True(after > before, $"got closer: {before} then {after}");
    }

    [Fact]
    public void StaysInsideTheWorkArea()
    {
        foreach (var (x, y) in new[] { (460, 410), (1450, 410), (460, 730), (1450, 730), (960, 570) })
        {
            var moved = OverlayAvoidance.Dodge(Board, x, y, Work);
            if (moved is null)
            {
                continue;
            }

            Assert.True(moved.Value.X >= Work.X, $"escaped left: {moved}");
            Assert.True(moved.Value.Right <= Work.Right, $"escaped right: {moved}");
            Assert.True(moved.Value.Y >= Work.Y, $"escaped top: {moved}");
            Assert.True(moved.Value.Bottom <= Work.Bottom, $"escaped bottom: {moved}");
        }
    }

    /// <summary>Preference for the smallest movement: the board gets out of the way, it does not tour the screen.</summary>
    [Fact]
    public void PrefersTheShortestEscape()
    {
        // Pointer just inside the top edge: dropping the board below it is the shortest way out.
        var moved = OverlayAvoidance.Dodge(Board, 960, Board.Y + 5, Work)!.Value;

        Assert.True(moved.Y > Board.Y, $"expected a downward move, got {moved}");
    }

    /// <summary>
    /// A work area barely taller than the board leaves no clean escape. It must still not leave the
    /// board sitting under the pointer, and it must not fall outside the screen.
    /// </summary>
    [Fact]
    public void HandlesAScreenWithNoRoomToEscape()
    {
        var cramped = new ScreenRect(0, 0, 1100, 400);
        var board = new ScreenRect(40, 30, 1012, 345);

        var moved = OverlayAvoidance.Dodge(board, 500, 200, cramped);

        if (moved is not null)
        {
            Assert.True(moved.Value.X >= cramped.X && moved.Value.Right <= cramped.Right, $"escaped: {moved}");
            Assert.True(moved.Value.Y >= cramped.Y && moved.Value.Bottom <= cramped.Bottom, $"escaped: {moved}");
        }
    }

    [Fact]
    public void IgnoresAnEmptyBoard()
    {
        Assert.Null(OverlayAvoidance.Dodge(default, 100, 100, Work));
    }

    /// <summary>Distance from the pointer to the nearest point of the rectangle.</summary>
    private static double Distance(ScreenRect rect, int x, int y)
    {
        var dx = Math.Max(Math.Max(rect.X - x, 0), x - rect.Right);
        var dy = Math.Max(Math.Max(rect.Y - y, 0), y - rect.Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
