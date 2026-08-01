using System.Diagnostics;

namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Keeps the overlay keyboard resident and hidden so that toggling it is instant.
/// </summary>
/// <remarks>
/// The overlay is a self-contained single-file executable of about 116 MB. Measured on this build,
/// roughly four seconds elapse between the process being created and the first line of its own code
/// running — host startup and the on-access scan of the bundle, not the overlay, which needs 94 ms
/// to build and show its window. Spawning it on each toggle put that four seconds between the button
/// press and the keyboard appearing.
///
/// Starting it once with <c>--prewarm</c> moves that cost to controller startup, where nobody is
/// waiting on it. From then on, showing and hiding are signal files.
///
/// Failure here is never fatal: if the process cannot be started, <see cref="IsResident"/> stays
/// false and the toggle falls back to launching the overlay cold, exactly as before.
/// </remarks>
public static class OskPrewarm
{
    private static Process? _process;

    public static bool IsResident
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>Starts the resident overlay. Safe to call more than once.</summary>
    public static void Start(string baseDirectory, Action<string> log)
    {
        if (IsResident)
        {
            return;
        }

        var overlayPath = Path.Combine(baseDirectory, "Sc2Xboxed.Osk.exe");
        if (!File.Exists(overlayPath))
        {
            log($"OSK prewarm skipped: {overlayPath} not found.");
            return;
        }

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = overlayPath,
                Arguments = "--prewarm",
                UseShellExecute = true,
            });
            log($"OSK prewarm started: PID={_process?.Id}");
        }
        catch (Exception exception)
        {
            _process = null;
            log($"OSK prewarm failed ({exception.GetType().Name}: {exception.Message}); the overlay will start cold.");
        }
    }

    /// <summary>
    /// Asks the resident overlay to exit, then makes sure it did.
    /// </summary>
    /// <remarks>
    /// A hidden process that outlives the controller runtime would hold the pad pipe open and leave
    /// an invisible window behind, so the signal is backed by a kill.
    /// </remarks>
    public static void Stop(string baseDirectory, Action<string> log)
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                Path.Combine(baseDirectory, "osk-exit.signal"),
                DateTimeOffset.UtcNow.Ticks.ToString());

            if (!_process.WaitForExit(2000))
            {
                log("OSK prewarm did not exit on signal; killing it.");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch (Exception exception)
        {
            log($"OSK prewarm stop failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try { _process.Dispose(); } catch { }
            _process = null;
        }
    }
}
