using System.Runtime.InteropServices;
using System.Text;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Picks the output mode from whatever is in the foreground, mirroring how Steam swaps between a
/// desktop configuration and a per-game one: a fullscreen game gets the virtual gamepad, the
/// desktop gets mouse and keyboard emulation.
/// </summary>
public sealed class ForegroundModeArbiter
{
    /// <summary>Shell surfaces are fullscreen by nature and must never read as a game.</summary>
    private static readonly string[] ShellClassNames =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Windows.UI.Core.CoreWindow",
    ];

    /// <summary>Our own windows must not drive the decision either.</summary>
    private static readonly string[] OwnProcessNames =
    [
        "Sc2Xboxed.Osk",
        "SteamXBox",
        "SteamXBox.Core",
    ];

    private readonly WindowsForegroundProcessProvider _provider;
    private readonly TimeSpan _pollInterval;

    private int _lastPollTick;
    private bool _hasPolled;

    /// <summary>
    /// Process the user manually chose a mode for. Automatic switching stays out of the way until
    /// the foreground moves to a different application.
    /// </summary>
    private int _manualOverrideProcessId = -1;

    public ForegroundModeArbiter(
        WindowsForegroundProcessProvider? provider = null,
        TimeSpan? pollInterval = null)
    {
        _provider = provider ?? new WindowsForegroundProcessProvider();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(750);
    }

    /// <summary>Name of the foreground process at the last poll, for logging.</summary>
    public string? LastForegroundProcess { get; private set; }

    /// <summary>
    /// Returns the mode the foreground suggests, or null when there is no opinion: throttled,
    /// unknown foreground, one of our own windows, or a manual override still in effect.
    /// </summary>
    public ControllerOutputMode? Poll()
    {
        int tick = Environment.TickCount;
        if (_hasPolled && tick - _lastPollTick < _pollInterval.TotalMilliseconds)
        {
            return null;
        }

        _lastPollTick = tick;
        _hasPolled = true;

        var foreground = _provider.GetForegroundProcess();
        if (foreground is null)
        {
            return null;
        }

        LastForegroundProcess = foreground.ProcessName;

        if (OwnProcessNames.Contains(foreground.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        // Moving to another application retires the manual choice.
        if (foreground.ProcessId != _manualOverrideProcessId)
        {
            _manualOverrideProcessId = -1;
        }
        else
        {
            return null;
        }

        return IsForegroundFullscreen()
            ? ControllerOutputMode.Xbox360
            : ControllerOutputMode.Profile;
    }

    /// <summary>
    /// Suspends automatic switching for the current foreground application, so a manual mode
    /// toggle is not immediately undone by the next poll.
    /// </summary>
    public void SuspendForForegroundApp()
    {
        var foreground = _provider.GetForegroundProcess();
        _manualOverrideProcessId = foreground?.ProcessId ?? -1;
    }

    /// <summary>True when the foreground window covers its entire monitor.</summary>
    private static bool IsForegroundFullscreen()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        if (IsShellWindow(window))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(window, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var screen = monitorInfo.Monitor;

        return windowRect.Left <= screen.Left
            && windowRect.Top <= screen.Top
            && windowRect.Right >= screen.Right
            && windowRect.Bottom >= screen.Bottom;
    }

    private static bool IsShellWindow(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        if (NativeMethods.GetClassName(window, buffer, buffer.Capacity) == 0)
        {
            return false;
        }

        var className = buffer.ToString();
        return ShellClassNames.Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    private static class NativeMethods
    {
        public const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);
    }
}
