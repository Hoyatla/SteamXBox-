using System.Windows;
using System.Windows.Threading;

namespace SteamXBox.Desktop.Capture;

/// <summary>
/// Runs a region capture from start to finish.
/// </summary>
/// <remarks>
/// The whole sequence belongs to SteamXBox, which is the point of not using Win+Shift+S: hide the
/// environment, let the user drag a region, grab the pixels, come back. Nothing else takes the
/// foreground in between, so there is no window to fight and no signal to wait for.
/// </remarks>
internal static class CaptureAction
{
    /// <summary>
    /// Hides <paramref name="environment"/>, captures a region, then shows it again.
    /// </summary>
    /// <returns>A line describing what happened, for the status area.</returns>
    public static string Run(Window environment)
    {
        var wasVisible = environment.IsVisible;
        environment.Hide();

        try
        {
            // One frame at Background priority before the selector opens. Hide() only queues the
            // window's removal; without letting the message loop run, the capture would include the
            // environment that is supposedly no longer on screen.
            Settle();

            var selector = new CaptureOverlayWindow();
            var chosen = selector.ShowDialog() == true && selector.Region is { } region;
            if (!chosen)
            {
                return "";
            }

            // Same reason again: the selector's own dimming must be gone before the pixels are read.
            Settle();

            var result = ScreenCapture.Capture(selector.Region!.Value, DateTimeOffset.Now);
            if (result.Width == 0)
            {
                return Shell.Localization.Strings.Current["Capture annulée."];
            }

            return result.Path.Length > 0
                ? Shell.Localization.Strings.Current.Format(
                    "Capture {0}×{1} copiée et enregistrée dans {2}", result.Width, result.Height, ScreenCapture.Folder)
                : Shell.Localization.Strings.Current.Format(
                    "Capture {0}×{1} copiée dans le presse-papiers.", result.Width, result.Height);
        }
        finally
        {
            if (wasVisible)
            {
                OverlayShower.Show(environment);
            }
        }
    }

    /// <summary>
    /// Lets the message loop drain so a pending hide is actually painted.
    /// </summary>
    /// <remarks>
    /// A bounded nested frame, not <c>Invoke(…, ApplicationIdle)</c>. Waiting for idle from the UI
    /// thread pushes a frame that only returns once the queue goes quiet — and this application
    /// keeps recurring timers running, so it could wait for ever. That is a hang, and it takes the
    /// whole environment with it.
    ///
    /// This version queues one callback at Background priority and exits the moment it runs: still
    /// a pump, but with a guaranteed way out.
    /// </remarks>
    private static void Settle()
    {
        var frame = new DispatcherFrame();
        Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));

        Dispatcher.PushFrame(frame);
    }
}
