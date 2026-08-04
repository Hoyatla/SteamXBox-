using System.Windows.Automation;
using System.Windows.Automation.Text;
using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.Osk;

/// <summary>
/// Finds the caret through UI Automation, for the applications that have no Windows caret.
/// </summary>
/// <remarks>
/// <c>GetGUIThreadInfo</c> only reports a caret for applications that use the classic
/// <c>CreateCaret</c> API. Word draws its own, the Windows 11 Notepad is WinUI, and browsers render
/// text themselves — for all of them the classic call returns nothing, which is why the keyboard
/// kept landing straight on the line being typed.
///
/// UI Automation asks the application where its text insertion point is, which is a question those
/// applications can answer. It is slower than a Win32 call — tens of milliseconds — so it is used
/// only when the cheap path has already failed, and only when the overlay is about to be shown.
/// </remarks>
public static class UiAutomationCaret
{
    /// <summary>
    /// Returns the caret or the current selection, in screen pixels, or an empty rectangle.
    /// </summary>
    public static ScreenRect Find()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return default;
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
                && pattern is TextPattern text)
            {
                var ranges = text.GetSelection();
                if (ranges is { Length: > 0 })
                {
                    var rectangles = ranges[0].GetBoundingRectangles();
                    if (rectangles is { Length: > 0 })
                    {
                        return FromRect(rectangles[0]);
                    }

                    // A collapsed caret reports no rectangle of its own. Expanding by one character
                    // gives it a body, which is what we actually want to keep clear anyway.
                    var probe = ranges[0].Clone();
                    probe.ExpandToEnclosingUnit(TextUnit.Character);
                    var expanded = probe.GetBoundingRectangles();
                    if (expanded is { Length: > 0 })
                    {
                        return FromRect(expanded[0]);
                    }
                }
            }

            // No text pattern: fall back to the focused element itself. For a single-line box that is
            // exactly right; for a document it is handled as a large field by the placement rules.
            var bounds = focused.Current.BoundingRectangle;
            return FromRect(bounds);
        }
        catch
        {
            // UI Automation throws freely: the element can vanish between two calls, and some
            // applications simply refuse to answer. Either way the keyboard falls back.
            return default;
        }
    }

    private static ScreenRect FromRect(System.Windows.Rect rect)
    {
        if (double.IsNaN(rect.X) || double.IsNaN(rect.Y)
            || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height)
            || rect.Width <= 0 || rect.Height <= 0)
        {
            return default;
        }

        return new ScreenRect(
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height));
    }
}
