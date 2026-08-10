using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// Brings a window to the front and gives it the keyboard, for real.
/// </summary>
/// <remarks>
/// <see cref="Window.Activate"/> calls <c>SetForegroundWindow</c>, which Windows refuses when the
/// calling process is not already the foreground one. That refusal is silent and it returns as
/// though it worked. It is a deliberate protection — it stops a background program from stealing the
/// keyboard mid-sentence — and a launcher summoned by a global shortcut is exactly the case it gets
/// wrong: the user asked for the window, but the request arrives from a process that owns nothing on
/// screen.
///
/// <para>
/// The way through is the documented one: attach this thread's input queue to the foreground
/// window's for the moment of the call. While attached, Windows treats the two as one input context,
/// so the foreground change comes from the thread that already had it. Detached immediately after —
/// leaving them attached would tie our keyboard state to another application's for good.
/// </para>
///
/// <para>
/// No injection of any kind. <c>AttachThreadInput</c> is an ordinary Win32 call about input queues;
/// nothing of ours runs in the other process.
/// </para>
/// </remarks>
internal static class WindowForeground
{
    /// <summary>Puts <paramref name="window"/> in front, whatever had the foreground.</summary>
    public static void Take(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            var foreground = GetForegroundWindow();
            var ours = GetCurrentThreadId();
            var theirs = foreground == IntPtr.Zero ? ours : GetWindowThreadProcessId(foreground, out _);

            if (theirs == ours || theirs == 0)
            {
                SetForegroundWindow(handle);
                return;
            }

            AttachThreadInput(ours, theirs, true);

            try
            {
                SetForegroundWindow(handle);
                BringWindowToTop(handle);
            }
            finally
            {
                // Always, including if the call above failed. A permanent attachment would make this
                // process's focus follow another application's for the rest of the session.
                AttachThreadInput(ours, theirs, false);
            }
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            // Nothing to do about it, and not worth losing the window over: it is shown either way,
            // simply not focused.
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
