namespace Sc2Xboxed.Core.Osk;

/// <summary>
/// Moves the floating keyboard out of the way when the mouse pointer comes near it.
/// </summary>
/// <remarks>
/// The pointer and the keyboard are driven by the same hands: the right pad moves the cursor, and
/// the keyboard sits wherever the text field is. They collide constantly, and a keyboard sitting
/// under the pointer is a keyboard you cannot click through.
///
/// So the board dodges. As soon as the pointer enters a margin around it, the board jumps to the
/// far side of the pointer along whichever axis needs the smallest movement, staying inside the work
/// area. It moves away from the pointer, never towards it — that is what makes the behaviour
/// predictable instead of feeling like the keyboard is chasing you.
/// </remarks>
public static class OverlayAvoidance
{
    /// <summary>How close the pointer may get before the board moves, in pixels.</summary>
    public const int Margin = 10;

    /// <summary>Extra clearance left between the pointer and the board after a dodge.</summary>
    public const int Clearance = 24;

    /// <summary>
    /// Returns where the board should move to, or null when the pointer is far enough away.
    /// </summary>
    public static ScreenRect? Dodge(ScreenRect board, int cursorX, int cursorY, ScreenRect workArea)
    {
        if (board.IsEmpty)
        {
            return null;
        }

        var danger = new ScreenRect(
            board.X - Margin,
            board.Y - Margin,
            board.Width + (2 * Margin),
            board.Height + (2 * Margin));

        var inside = cursorX >= danger.X && cursorX < danger.Right
                     && cursorY >= danger.Y && cursorY < danger.Bottom;

        if (!inside)
        {
            return null;
        }

        // Four ways out. Each is the position the board would need for the pointer to sit outside
        // it by Clearance, on that side.
        var candidates = new[]
        {
            new ScreenRect(board.X, cursorY + Clearance, board.Width, board.Height),                 // move down
            new ScreenRect(board.X, cursorY - Clearance - board.Height, board.Width, board.Height),  // move up
            new ScreenRect(cursorX + Clearance, board.Y, board.Width, board.Height),                 // move right
            new ScreenRect(cursorX - Clearance - board.Width, board.Y, board.Width, board.Height),   // move left
        };

        ScreenRect? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!FitsIn(candidate, workArea))
            {
                continue;
            }

            // Smallest movement wins: the board should get out of the way, not tour the screen.
            var distance = Math.Abs(candidate.X - board.X) + Math.Abs(candidate.Y - board.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best is not null)
        {
            return best;
        }

        // Nowhere clean to go: slide along the work area edge furthest from the pointer rather than
        // staying under it. A cramped screen should still not leave the board glued to the cursor.
        var roomBelow = workArea.Bottom - cursorY;
        var roomAbove = cursorY - workArea.Y;

        var y = roomBelow >= roomAbove
            ? workArea.Bottom - board.Height
            : workArea.Y;

        var clampedY = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - board.Height));
        var clampedX = Math.Clamp(board.X, workArea.X, Math.Max(workArea.X, workArea.Right - board.Width));

        var fallback = new ScreenRect(clampedX, clampedY, board.Width, board.Height);
        return fallback == board ? null : fallback;
    }

    private static bool FitsIn(ScreenRect rect, ScreenRect area)
        => rect.X >= area.X && rect.Right <= area.Right
           && rect.Y >= area.Y && rect.Bottom <= area.Bottom;
}
