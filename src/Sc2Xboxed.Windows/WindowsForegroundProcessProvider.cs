using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sc2Xboxed.Windows;

public sealed class WindowsForegroundProcessProvider
{
    public ForegroundProcessInfo? GetForegroundProcess()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new ForegroundProcessInfo(
                process.Id,
                process.ProcessName,
                TryGetMainModuleFileName(process));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? TryGetMainModuleFileName(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static partial class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
    }
}
