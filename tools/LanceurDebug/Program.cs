using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamXBox.Debug;

/// <summary>
/// Starts the diagnostic instruments, then SteamXBox, in that order and never the other.
/// </summary>
/// <remarks>
/// <b>The order is the whole point.</b> The monitor takes a census of every window on the desktop
/// when it starts and reports what changes afterwards. Started second, that census already contains
/// whatever SteamXBox did, and everything the product touched on the way up reads as "was always
/// there". On 11 August a session was diagnosed with a monitor whose log had stopped an hour before
/// the fault; on the 12th the same fault was caught in four minutes because the monitor was
/// breathing first. Launching them by hand in the right order every time is a step nobody
/// remembers, so it lives here instead.
///
/// <para>
/// <b>Adding a tool later is one line</b> in <see cref="Tools"/>. That is what this exists for: the
/// list is the extension point, not the code around it.
/// </para>
///
/// <para>
/// It has no window. A launcher that flashes a console on every start is noise, and on success
/// there is nothing to say — the monitor's own window appearing is the acknowledgement. It speaks
/// only when something did not start, because a debug session begun without its instrument is worse
/// than one not begun at all: it produces evidence that looks complete.
/// </para>
///
/// <para>
/// Deliberately out of the solution and out of the installers, like the monitor. This is developer
/// tooling; it has no business on a client's machine.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// What to start, in order.
    /// </summary>
    /// <param name="Executable">File name, resolved beside this launcher.</param>
    /// <param name="Name">How it is called when something goes wrong.</param>
    /// <param name="SettleFor">
    /// How long to wait before starting the next one. Only the instruments need this — long enough
    /// to have taken their reading of a desktop that SteamXBox has not touched yet.
    /// </param>
    private sealed record Tool(string Executable, string Name, TimeSpan SettleFor);

    /// <summary>
    /// The instruments first, the product last. Add new debug tools above SteamXBox.
    /// </summary>
    private static readonly Tool[] Tools =
    [
        new("SteamXBox-Moniteur.exe", "le moniteur", TimeSpan.FromMilliseconds(900)),
        new("SteamXBox.exe", "SteamXBox", TimeSpan.Zero),
    ];

    [STAThread]
    private static int Main()
    {
        var here = AppContext.BaseDirectory;
        var trouble = new StringBuilder();

        foreach (var tool in Tools)
        {
            var path = Path.Combine(here, tool.Executable);

            if (!File.Exists(path))
            {
                trouble.AppendLine($"• {tool.Name} : introuvable ({tool.Executable}).");
                continue;
            }

            // One of each. Two SteamXBox on one machine fight over the same controllers, the same
            // HidHide whitelist and the same virtual pads; two monitors just double every line.
            if (AlreadyRunning(tool.Executable))
            {
                continue;
            }

            try
            {
                var started = Process.Start(new ProcessStartInfo(path)
                {
                    WorkingDirectory = here,

                    // Its own console, so closing this launcher — or the shell that spawned it —
                    // does not send a close event to what it started. That event is exactly what
                    // killed several test sessions before anyone understood why they ended early.
                    UseShellExecute = true,
                });

                if (started is null)
                {
                    trouble.AppendLine($"• {tool.Name} : n'a pas démarré.");
                    continue;
                }
            }
            catch (Exception failure)
            {
                trouble.AppendLine($"• {tool.Name} : {failure.Message}");
                continue;
            }

            if (tool.SettleFor > TimeSpan.Zero)
            {
                Thread.Sleep(tool.SettleFor);
            }
        }

        if (trouble.Length == 0)
        {
            return 0;
        }

        Say("Tout n'a pas démarré. Ce qui suit n'aura pas été mesuré :\n\n"
            + trouble
            + "\nCe qui a démarré tourne quand même.");

        return 1;
    }

    private static bool AlreadyRunning(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);

        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            // Not worth refusing to launch over: the worst case is a second copy, which the check
            // above is a courtesy against rather than a guarantee.
            return false;
        }
    }

    private static void Say(string message)
        => MessageBoxW(IntPtr.Zero, message, "SteamXBox Debug", 0x00000030);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint style);
}
