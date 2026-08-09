using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.Osk;

/// <summary>
/// Finds the text field the user is typing into, so the keyboard can be placed next to it.
/// </summary>
/// <remarks>
/// Three attempts, in decreasing order of precision:
///
/// 1. The caret rectangle reported by <c>GetGUIThreadInfo</c> for the foreground thread. This is the
///    blinking cursor itself, so it is exactly right when it is available.
/// 2. The window rectangle of the focused control. Coarser, but a text box is usually small enough
///    that dodging the whole control is the same as dodging the caret.
/// 3. Nothing, in which case the keyboard falls back to the bottom of the screen.
///
/// Some applications — anything drawing its own text editing, which includes most games and several
/// browsers — report no caret at all. That is expected, not a failure, and is why step 3 exists.
/// </remarks>
public static class CaretLocator
{
    /// <summary>
    /// A caret is a sliver a few pixels wide. Padded out so the keyboard clears the line of text it
    /// sits on rather than butting up against the character being typed.
    /// </summary>
    private const int CaretPaddingY = 10;

    private const int MinimumUsefulSize = 4;

    /// <summary>What was found, and how reliable it is.</summary>
    /// <param name="Rect">The area to keep clear, in screen pixels.</param>
    /// <param name="IsCaret">
    /// True when this is the caret itself. False when it is only the focused control, which for a
    /// word processor is the whole document — text already written may be covered, the caret may not.
    /// </param>
    public readonly record struct ActiveField(ScreenRect Rect, bool IsCaret);

    /// <summary>Work area of the monitor holding the foreground window.</summary>
    /// <remarks>
    /// Not the primary monitor. Falling back to the primary is why nothing worked on a second
    /// screen: with no caret to locate, the keyboard went home to monitor one while the user was
    /// typing on monitor two.
    /// </remarks>
    /// <summary>Work area of the monitor the mouse pointer is on.</summary>
    /// <remarks>
    /// The fallback when no caret can be found. The pointer is the one thing that always says which
    /// screen the user is actually working on: the foreground window can be a dialog on one monitor
    /// while the typing happens on another, and the primary monitor is simply a guess.
    /// </remarks>
    public static ScreenRect PointerWorkArea()
    {
        try
        {
            var area = System.Windows.Forms.Screen
                .FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;

            return new ScreenRect(area.X, area.Y, area.Width, area.Height);
        }
        catch
        {
            return WorkAreaFor(default);
        }
    }

    public static ScreenRect ForegroundWorkArea()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window != IntPtr.Zero && GetWindowRect(window, out var rect))
            {
                var centre = new System.Drawing.Point(
                    rect.Left + ((rect.Right - rect.Left) / 2),
                    rect.Top + ((rect.Bottom - rect.Top) / 2));

                var area = System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
                return new ScreenRect(area.X, area.Y, area.Width, area.Height);
            }
        }
        catch
        {
        }

        return WorkAreaFor(default);
    }

    public static ActiveField FindActiveFieldDetailed()
    {
        // Cheapest first: a plain Win32 call, and correct for anything using the classic caret.
        var caret = FindCaretRect();
        if (!caret.IsEmpty)
        {
            return new ActiveField(caret, IsCaret: true);
        }

        // Then ask the application itself. Word, the WinUI Notepad and browsers have no Windows
        // caret at all, and this is the only way to learn where they are about to put text.
        var automation = UiAutomationCaret.Find();
        if (!automation.IsEmpty)
        {
            // Treated as a caret when it is the size of a line of text rather than a whole document:
            // a caret must never be covered, a document may be.
            var looksLikeALine = automation.Height <= MaxCaretHeight;
            return new ActiveField(automation, looksLikeALine);
        }

        return new ActiveField(FindFocusedControlRect(), IsCaret: false);
    }

    /// <summary>
    /// Above this height, a rectangle is a text area rather than a line being typed.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: a caret in a large font, or a selection spanning two lines, is still
    /// somewhere the keyboard must not sit.
    /// </remarks>
    private const int MaxCaretHeight = 120;

    public static ScreenRect FindActiveField() => FindActiveFieldDetailed().Rect;

    private static ScreenRect FindFocusedControlRect()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return default;
            }

            var threadId = GetWindowThreadProcessId(foreground, out _);
            if (threadId == 0)
            {
                return default;
            }

            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            {
                return default;
            }

            if (GetWindowRect(info.hwndFocus, out var control))
            {
                var width = control.Right - control.Left;
                var height = control.Bottom - control.Top;
                if (width >= MinimumUsefulSize && height >= MinimumUsefulSize)
                {
                    return new ScreenRect(control.Left, control.Top, width, height);
                }
            }
        }
        catch
        {
        }

        return default;
    }

    private static ScreenRect FindCaretRect()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return default;
            }

            var threadId = GetWindowThreadProcessId(foreground, out _);
            if (threadId == 0)
            {
                return default;
            }

            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(threadId, ref info))
            {
                return default;
            }

            var focused = info.hwndCaret != IntPtr.Zero ? info.hwndCaret : info.hwndFocus;

            // The caret rectangle is in client coordinates of the window that owns it.
            var caret = info.rcCaret;
            if (info.hwndCaret != IntPtr.Zero && caret.Right > caret.Left && caret.Bottom > caret.Top)
            {
                var topLeft = new Point { X = caret.Left, Y = caret.Top - CaretPaddingY };
                var bottomRight = new Point { X = caret.Right, Y = caret.Bottom + CaretPaddingY };

                if (ClientToScreen(info.hwndCaret, ref topLeft) && ClientToScreen(info.hwndCaret, ref bottomRight))
                {
                    return new ScreenRect(
                        topLeft.X,
                        topLeft.Y,
                        Math.Max(MinimumUsefulSize, bottomRight.X - topLeft.X),
                        Math.Max(MinimumUsefulSize, bottomRight.Y - topLeft.Y));
                }
            }

        }
        catch
        {
            // Any failure here just means the keyboard goes to its default position.
        }

        return default;
    }

    /// <summary>Work area of the monitor holding <paramref name="field"/>, excluding the taskbar.</summary>
    public static ScreenRect WorkAreaFor(ScreenRect field)
    {
        try
        {
            var screen = field.IsEmpty
                ? System.Windows.Forms.Screen.PrimaryScreen
                : System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point(field.X + (field.Width / 2), field.Y + (field.Height / 2)));

            var area = (screen ?? System.Windows.Forms.Screen.PrimaryScreen)!.WorkingArea;
            return new ScreenRect(area.X, area.Y, area.Width, area.Height);
        }
        catch
        {
            return new ScreenRect(0, 0, 1920, 1080);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public Rect rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(int threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
