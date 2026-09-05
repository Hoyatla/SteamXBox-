using System.Runtime.InteropServices;
using System.Windows;

namespace SenSÉ.Desktop.Search;

/// <summary>
/// The usable area of the screen the mouse is on.
/// </summary>
/// <remarks>
/// WPF only offers <see cref="SystemParameters.WorkArea"/>, which is the primary monitor and nothing
/// else — a launcher placed with it appears on screen one while the user is working on screen two.
///
/// <para>
/// Asked of Windows directly rather than by referencing WinForms for its <c>Screen</c> class. That
/// reference would pull the WinForms <c>Application</c>, <c>MessageBox</c> and <c>Point</c> types
/// into a WPF project that already has all three, and every ambiguity it created would have to be
/// resolved by hand in files that have nothing to do with this.
/// </para>
/// </remarks>
internal static class PointerScreen
{
    /// <summary>Work area of the monitor under the pointer, in device pixels.</summary>
    /// <remarks>
    /// Falls back to WPF's primary work area if Windows refuses, which is wrong on a second monitor
    /// but never nothing — a launcher somewhere is better than a launcher nowhere.
    /// </remarks>
    public static Rect WorkArea()
    {
        try
        {
            if (GetCursorPos(out var cursor))
            {
                var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);

                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

                if (monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info))
                {
                    return new Rect(
                        info.WorkLeft,
                        info.WorkTop,
                        Math.Max(1, info.WorkRight - info.WorkLeft),
                        Math.Max(1, info.WorkBottom - info.WorkTop));
                }
            }
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
        }

        return SystemParameters.WorkArea;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int Left, Top, Right, Bottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
