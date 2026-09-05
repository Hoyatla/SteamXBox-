using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SenSÉ.Desktop;

/// <summary>
/// Brings the overlay back after it has hidden itself.
/// </summary>
/// <remarks>
/// The overlay hides for the two actions that send keystrokes, and a hidden window has no taskbar
/// button and no handle to click — so without this it would be gone until the process was killed.
///
/// A file signal rather than a pipe or a mutex, because that is already how the overlay keyboard is
/// told to show and close: one mechanism in the product instead of two. Launching
/// <c>SenSÉ.exe</c> again writes the signal; this watcher picks it up and restores the window.
/// </remarks>
internal static class ShowSignal
{
    /// <summary>Name of the file another process writes to ask the overlay to come back.</summary>
    public const string FileName = "desktop-show.signal";

    private static DispatcherTimer? _timer;

    private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Starts watching for the signal, and stops once the window is back.</summary>
    public static void Watch(Window window)
    {
        Clear();

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) =>
        {
            if (!File.Exists(Path))
            {
                return;
            }

            Clear();
            _timer!.Stop();
            _timer = null;

            OverlayShower.Show(window);
        };

        _timer.Start();
    }

    /// <summary>Writes the signal, from whichever process wants the overlay back.</summary>
    public static void Raise()
    {
        try
        {
            File.WriteAllText(Path, DateTime.UtcNow.Ticks.ToString());
        }
        catch
        {
        }
    }

    /// <summary>
    /// Removes a stale signal.
    /// </summary>
    /// <remarks>
    /// Called before watching as well as after acting: a signal left behind by a previous session
    /// would make the overlay reappear the instant it tried to hide.
    /// </remarks>
    private static void Clear()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
        }
    }
}
