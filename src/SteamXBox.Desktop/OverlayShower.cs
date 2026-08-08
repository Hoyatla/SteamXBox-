using System.Windows;

namespace SteamXBox.Desktop;

/// <summary>
/// Shows and activates a window that hides itself, whatever its state.
/// </summary>
/// <remarks>
/// The overlay is maximized (see <c>MainWindow.Reposition</c>) and declares
/// <c>ShowActivated="False"</c> so it never steals the foreground at startup. WPF refuses to call
/// <c>Show()</c> on a window that has both set — it throws and the window never comes back. The two
/// actions that hide the overlay (a region capture, a keystroke action) re-show it later, and that
/// re-show hit the throw, taking the environment down with it. Every re-show goes through here:
/// <c>ShowActivated</c> is lifted only for the <c>Show()</c> and restored straight afterwards, so
/// the "never activates itself at startup" rule keeps holding.
/// </remarks>
internal static class OverlayShower
{
    public static void Show(Window window)
    {
        var wasActivated = window.ShowActivated;
        window.ShowActivated = true;
        window.Show();
        window.Activate();
        window.ShowActivated = wasActivated;
    }
}
