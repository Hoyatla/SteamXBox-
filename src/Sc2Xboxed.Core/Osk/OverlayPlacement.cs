namespace Sc2Xboxed.Core.Osk;

/// <summary>A rectangle in screen pixels. Kept free of any UI framework so this stays testable.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IntersectsWith(ScreenRect other)
        => !IsEmpty && !other.IsEmpty
           && X < other.Right && Right > other.X
           && Y < other.Bottom && Bottom > other.Y;
}

/// <summary>Where the keyboard ended up, and why.</summary>
public enum PlacementKind
{
    /// <summary>Under the field being typed into: the natural reading position.</summary>
    BelowField,

    /// <summary>Above it, because there was not enough room below.</summary>
    AboveField,

    /// <summary>
    /// To one side of the field. The left is preferred, the right only when there is no room
    /// above either.
    /// </summary>
    BesideField,

    /// <summary>
    /// On the text, because nothing else fits. A maximised editor leaves no free band wide or tall
    /// enough for the board, so the least-bad edge is chosen rather than pretending otherwise.
    /// </summary>
    Overlapping,

    /// <summary>Nothing was known about the field, or it filled the screen.</summary>
    ScreenBottom,
}

public readonly record struct OverlayPlacementResult(ScreenRect Bounds, PlacementKind Kind);

/// <summary>
/// Places the floating keyboard near what the user is typing into, without covering it.
/// </summary>
/// <remarks>
/// The overlay used to be a full-width band pinned to the bottom of the screen. That is the worst
/// case for a small text field: the eye has to travel the whole height of the display between the
/// field and the keys, and a field near the bottom — a chat box, a search bar in a taskbar app —
/// disappears behind the keyboard entirely.
///
/// So the board is sized to its own content and moved to the field. To its left by preference, with
/// the board's own frame edge acting as the boundary so the field is never covered; above it when
/// there is no room on the left; to its right when there is no room above either. When nothing is
/// known about the field, it falls back to the bottom of the screen, which is where it always used
/// to be.
/// </remarks>
public static class OverlayPlacement
{
    /// <summary>
    /// Gap left between the field and the keyboard, in pixels.
    /// </summary>
    /// <remarks>
    /// Twelve pixels was technically clear of the field and still felt like it was sitting on it:
    /// a text field's visual weight extends past its own rectangle, through its border, its label
    /// and whatever the application draws under it.
    /// </remarks>
    public const int Gap = 32;

    /// <summary>Minimum distance kept from the edges of the work area.</summary>
    public const int ScreenMargin = 8;

    /// <summary>
    /// A rectangle that is not a caret and is taller than this fraction of the work area is a
    /// document view, not a text box. Text already written in it may be covered.
    /// </summary>
    private const double MaxFieldHeightFraction = 0.5;

    public static OverlayPlacementResult Place(int boardWidth, int boardHeight, ScreenRect field, ScreenRect workArea)
        => Place(boardWidth, boardHeight, field, isCaret: false, workArea);

    /// <summary>
    /// Where the keyboard goes when it is not floating: the bottom centre of the screen the user is
    /// typing on.
    /// </summary>
    /// <remarks>
    /// Fixed means fixed — the same place every time, so the eye and the thumb both learn it. The
    /// only thing that varies is <i>which</i> screen: the caret decides that, because a keyboard
    /// pinned to the primary monitor is useless to someone typing on the second one. The caller
    /// resolves the work area from the caret and hands it in.
    /// </remarks>
    public static OverlayPlacementResult PlaceFixed(int boardWidth, int boardHeight, ScreenRect workArea)
    {
        var width = Math.Min(boardWidth, Math.Max(1, workArea.Width - (2 * ScreenMargin)));
        var height = Math.Min(boardHeight, Math.Max(1, workArea.Height - (2 * ScreenMargin)));

        return new OverlayPlacementResult(AtScreenBottom(width, height, workArea), PlacementKind.ScreenBottom);
    }

    /// <summary>
    /// Places the floating board, keeping <paramref name="field"/> clear.
    /// </summary>
    /// <param name="isCaret">
    /// True when the rectangle is the caret itself. A caret is always avoided, however large the
    /// control around it: covering already-written text is acceptable, covering the place where the
    /// next character appears is not.
    /// </param>
    public static OverlayPlacementResult Place(
        int boardWidth, int boardHeight, ScreenRect field, bool isCaret, ScreenRect workArea)
    {
        var width = Math.Min(boardWidth, Math.Max(1, workArea.Width - (2 * ScreenMargin)));
        var height = Math.Min(boardHeight, Math.Max(1, workArea.Height - (2 * ScreenMargin)));

        // Size no longer disqualifies a field. Giving up on anything taller than half the screen is
        // exactly what put the board on a maximised editor: the rule is to look for a free band
        // around the text, and only overlap once there is provably nowhere to go.
        var usable = !field.IsEmpty && field.IntersectsWith(workArea);

        if (!usable)
        {
            return new OverlayPlacementResult(AtScreenBottom(width, height, workArea), PlacementKind.ScreenBottom);
        }

        // Centred on the field horizontally, used by every placement that sits above or below it.
        var x = Clamp(
            field.X + (field.Width / 2) - (width / 2),
            workArea.X + ScreenMargin,
            workArea.Right - ScreenMargin - width);

        // Beside first, and the left side first of all. The board's frame edge is the boundary with
        // the field: the board sits flush against the field's left and never covers a line of text.
        // Measured against a maximised Notepad, neither the Win32 caret nor UI Automation returns the
        // caret: both hand back the whole edit area. Placing to the side is the only rule that keeps
        // the board off the typing area without knowing where the caret is.
        // A gap on the left as well as the right. Flush against the field, the board's own frame sat
        // on the text's edge: the user asked for the frame to stay clear of the typing area, not
        // merely out of it.
        if (field.X - workArea.X >= width + Gap + ScreenMargin)
        {
            var y = Clamp(
                field.Y + (field.Height / 2) - (height / 2),
                workArea.Y + ScreenMargin,
                workArea.Bottom - ScreenMargin - height);

            return new OverlayPlacementResult(
                new ScreenRect(field.X - Gap - width, y, width, height),
                PlacementKind.BesideField);
        }

        // No room on the left: above, centred on the field.
        var above = field.Y - Gap - height;
        if (above >= workArea.Y + ScreenMargin)
        {
            return new OverlayPlacementResult(new ScreenRect(x, above, width, height), PlacementKind.AboveField);
        }

        // No room above either. Two candidates are left, the right side and below, and which comes
        // first depends on what the rectangle is.
        //
        // For a caret, below comes first, because the right is where the text is about to go. A
        // caret is two pixels wide: measured as a rectangle it has almost the whole screen free to
        // its right, and the placement looks excellent right up until the user types, at which point
        // the line runs straight under the board. Writing runs left to right, so the space ahead of
        // the caret is spoken for and the space below it is not — not yet, and by the time the text
        // reaches it the caret will have moved and the board with it.
        //
        // For a field rectangle the right side keeps its priority: the rectangle is the whole
        // control, its right edge is a real boundary, and nothing is going to be written past it.
        var rightFits = workArea.Right - field.Right >= width + Gap + ScreenMargin;
        var below = field.Bottom + Gap;
        var belowFits = below + height <= workArea.Bottom - ScreenMargin;

        OverlayPlacementResult Right()
        {
            var y = Clamp(
                field.Y + (field.Height / 2) - (height / 2),
                workArea.Y + ScreenMargin,
                workArea.Bottom - ScreenMargin - height);

            return new OverlayPlacementResult(
                new ScreenRect(field.Right + Gap, y, width, height),
                PlacementKind.BesideField);
        }

        if (rightFits && !isCaret)
        {
            return Right();
        }

        if (belowFits)
        {
            return new OverlayPlacementResult(new ScreenRect(x, below, width, height), PlacementKind.BelowField);
        }

        if (rightFits)
        {
            return Right();
        }

        // Nowhere free. Overlap is now unavoidable, so pick the edge furthest from the top of the
        // text: that is where a document starts and where the caret spends most of its life. A
        // maximised editor on a single monitor always lands here, and no rule can do better —
        // 2550 px of text in a 2560 px work area leaves 5 px a side.
        var typingStartsNearTheTop = field.Y <= workArea.Y + (workArea.Height / 2);

        var overlapY = typingStartsNearTheTop
            ? workArea.Bottom - ScreenMargin - height
            : workArea.Y + ScreenMargin;

        return new OverlayPlacementResult(
            new ScreenRect(x, overlapY, width, height),
            PlacementKind.Overlapping);
    }

    /// <summary>
    private static ScreenRect AtScreenBottom(int width, int height, ScreenRect workArea)
        => new(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Bottom - ScreenMargin - height,
            width,
            height);

    private static int Clamp(int value, int min, int max)
        => max < min ? min : Math.Clamp(value, min, max);
}
