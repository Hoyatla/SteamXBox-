using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// Asks another SteamXBox to stop, instead of stopping it.
/// </summary>
/// <remarks>
/// A console program is asked to stop with <c>CTRL_C_EVENT</c>, and that event goes to a console,
/// not to a process. So to ask one we do not share a console with, we have to leave ours, join
/// theirs, deafen ourselves to what we are about to shout, shout it, and go back.
///
/// <para>
/// <b>Deafening ourselves is not optional.</b> The event reaches every process attached to that
/// console — including this one, the moment it attaches. Without
/// <see cref="SetConsoleCtrlHandler"/> set to ignore, asking another SteamXBox to stop would stop
/// the asker first, and the request would never be sent.
/// </para>
///
/// <para>
/// If the answer does not come, the caller falls back to killing. Being polite is worth a few
/// seconds, not a session that will not close.
/// </para>
/// </remarks>
internal static class PoliteStop
{
    private const uint CtrlCEvent = 0;
    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>
    /// Asks a process to stop, and says whether it did.
    /// </summary>
    /// <returns><c>true</c> when the process ended within <paramref name="patience"/>.</returns>
    internal static bool Ask(Process process, TimeSpan patience)
    {
        try
        {
            // Ours is let go of first: a process may be attached to one console at a time.
            FreeConsole();

            if (!AttachConsole((uint)process.Id))
            {
                return false;
            }

            SetConsoleCtrlHandler(IntPtr.Zero, add: true);

            var asked = GenerateConsoleCtrlEvent(CtrlCEvent, 0);

            if (!asked)
            {
                return false;
            }

            return process.WaitForExit((int)patience.TotalMilliseconds);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            // Put back the way it was, in this order, whatever happened. Leaving ourselves deaf to
            // Ctrl+C would make this program the one nobody can stop.
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, add: false);
            AttachConsole(AttachParentProcess);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);
}
