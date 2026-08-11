using System.Runtime.InteropServices;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// Makes every way of closing this program run its cleanup.
/// </summary>
/// <remarks>
/// <b>Only one way out used to be tidy, and nothing offered it.</b> The session gives controllers
/// back, releases the virtual pads and turns HidHide's cloaking off on the way past the end of
/// <c>Main</c> — and the close button, <c>Stop-SteamXBox.cmd</c> and the <c>stop</c> subcommand all
/// called <c>Process.Kill</c>, which reaches none of it. Measured consequence: a controller left
/// invisible to every game on the machine, with the note explaining why deleted along with the copy
/// of SteamXBox that wrote it.
///
/// <para>
/// <c>Console.CancelKeyPress</c> covers Ctrl+C alone. The close button of a console window is
/// <c>CTRL_CLOSE_EVENT</c>, and .NET does not surface it, so closing the window was a kill in
/// everything but name. This registers for the whole family — close, log off, shutdown — and holds
/// the operating system off while the session finishes.
/// </para>
///
/// <para>
/// <b>The hold is bounded and the bound is not ours.</b> Windows allows about five seconds after
/// <c>CTRL_CLOSE_EVENT</c> before it terminates regardless; four is asked for, so the answer arrives
/// before the axe rather than at the same moment.
/// </para>
/// </remarks>
internal static class GracefulShutdown
{
    private const uint CtrlCEvent = 0;
    private const uint CtrlBreakEvent = 1;
    private const uint CtrlCloseEvent = 2;
    private const uint CtrlLogoffEvent = 5;
    private const uint CtrlShutdownEvent = 6;

    private static readonly ManualResetEventSlim Finished = new(false);

    private static CancellationTokenSource? _cancellation;

    /// <summary>Kept in a field on purpose: a delegate handed to Windows and then collected is a crash.</summary>
    private static ConsoleCtrlHandler? _handler;

    /// <summary>Starts listening, and says what to cancel when something asks this program to stop.</summary>
    internal static void Arm(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
        _handler = OnConsoleEvent;

        // A window with no console — the environment starts this one without allocating one — has
        // nothing to register against, and that is not a failure worth reporting.
        SetConsoleCtrlHandler(_handler, add: true);
    }

    /// <summary>
    /// Says the cleanup has finished.
    /// </summary>
    /// <remarks>
    /// Called on the last line of the session, after the controllers are given back. Until it is
    /// called, a close request is still being waited on — which is the whole point: the window stays
    /// up the extra half-second it takes to unhide a controller.
    /// </remarks>
    internal static void Done() => Finished.Set();

    private static bool OnConsoleEvent(uint type)
    {
        if (type is not (CtrlCEvent or CtrlBreakEvent or CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent))
        {
            return false;
        }

        _cancellation?.Cancel();

        // Ctrl+C leaves the program running, so there is nothing to hold up: the session ends by
        // itself and this handler must return at once or the next Ctrl+C is ignored.
        if (type is CtrlCEvent or CtrlBreakEvent)
        {
            return true;
        }

        Finished.Wait(TimeSpan.FromSeconds(4));

        return true;
    }

    private delegate bool ConsoleCtrlHandler(uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler, bool add);
}
