using System.Diagnostics;
using SenSÉ.Core.Osk;

namespace SenSÉ.App.Console;

/// <summary>
/// Keeps one overlay keyboard resident per controller, so opening it costs a file rather than a
/// process.
/// </summary>
/// <remarks>
/// <b>The four seconds were the whole problem.</b> The overlay itself draws in ninety-four
/// milliseconds; what the user waits for is the .NET host unpacking a hundred and sixty-five
/// megabytes of single-file executable before the first line of that program runs. Paying that once,
/// when a controller is opened, is the difference between a keyboard that appears and one that is
/// summoned.
///
/// <para>
/// <b>One per controller, never one shared.</b> A single resident instance existed once, on the
/// default channels, and it answered for every controller — swallowing the frames meant for the
/// window that had just been opened for another. That is why <c>OskPrewarm</c>, which held a single
/// process in a static field, was abandoned rather than wired up; it is deleted along with this
/// change. Each keyboard here is addressed by its own suffix, and so are its pipes and its signals.
/// </para>
///
/// <para>
/// <b>Nothing here is required for the overlay to work.</b> Every method answers "no" rather than
/// throwing, and the caller falls back to starting a process the old way. A prewarm that fails costs
/// four seconds; a prewarm that throws would cost the keyboard entirely.
/// </para>
/// </remarks>
internal static class OskPrewarmSet
{
    private static readonly Dictionary<string, Process> Resident = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    /// <summary>
    /// Starts a hidden overlay for this controller, if one is not already waiting.
    /// </summary>
    /// <remarks>
    /// Called when a controller is opened, not when the keyboard is first asked for: the point is to
    /// have already paid by the time it is asked for.
    /// </remarks>
    internal static bool Start(string overlayPath, OskInstanceNaming naming, bool floating, Action<string> log)
    {
        try
        {
            lock (Gate)
            {
                if (IsAlive(naming.Suffix))
                {
                    return true;
                }

                if (!File.Exists(overlayPath))
                {
                    log($"OSK prewarm: {overlayPath} not found; the keyboard will cold-start.");
                    return false;
                }

                var start = new ProcessStartInfo { FileName = overlayPath, UseShellExecute = true };

                start.ArgumentList.Add("--instance");
                start.ArgumentList.Add(naming.Suffix);
                start.ArgumentList.Add("--floating");
                start.ArgumentList.Add(floating ? "1" : "0");
                start.ArgumentList.Add("--prewarm");

                var process = Process.Start(start);

                if (process is null)
                {
                    return false;
                }

                // Tied to this session by the kernel. A hidden overlay that outlived the Core would
                // be the worst orphan this product can make: no window to close, and it holds the
                // pipes the next session needs.
                SenSÉ.Windows.ChildProcesses.Adopt(process, log);

                Resident[naming.Suffix] = process;
                log($"OSK prewarm: overlay resident for instance '{naming.Suffix}' (pid {process.Id}).");

                return true;
            }
        }
        catch (Exception exception)
        {
            log($"OSK prewarm failed, the keyboard will cold-start: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Takes note of an overlay that was cold-started with <c>--prewarm</c>, so the next press finds
    /// it.
    /// </summary>
    /// <remarks>
    /// The first press of a session still pays the four seconds: the overlay needs its pipe to be
    /// listening before it starts, and that pipe is opened by the same press. What this buys is
    /// every press after — the keyboard is hidden rather than ended, and showing it again costs a
    /// file.
    /// </remarks>
    internal static void Remember(OskInstanceNaming naming, Process process, Action<string> log)
    {
        lock (Gate)
        {
            Resident[naming.Suffix] = process;
        }

        log($"OSK prewarm: '{naming.Suffix}' stays resident; the next opening will be immediate.");
    }

    /// <summary>
    /// Shows the resident overlay for this controller, and says whether there was one.
    /// </summary>
    /// <remarks>
    /// The show signal is a file the overlay watches for. Deliberately not deleted here — the
    /// overlay removes it once it has acted, and a signal written while it is still starting has to
    /// survive until it is looked at. Deleting it too early is what once swallowed the very first
    /// press of a session.
    /// </remarks>
    internal static bool Wake(OskInstanceNaming naming, Action<string> log)
    {
        try
        {
            lock (Gate)
            {
                if (!IsAlive(naming.Suffix))
                {
                    return false;
                }
            }

            WriteShowSignal(naming, log);

            return true;
        }
        catch (Exception exception)
        {
            log($"OSK prewarm: could not wake '{naming.Suffix}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Shows an overlay that was just cold-started.
    /// </summary>
    /// <remarks>
    /// A cold start launches with <c>--prewarm</c>, which keeps the window hidden until a show
    /// signal arrives. Without this the first press of a session started a keyboard that then
    /// waited forever: the pad was handed to it, the overlay was resident, and nothing was on
    /// screen. Written here rather than in the caller so the signal name and the resident path
    /// agree — they both go through this one file.
    /// </remarks>
    internal static void SignalShow(OskInstanceNaming naming, Action<string> log)
    {
        try
        {
            WriteShowSignal(naming, log);
        }
        catch (Exception exception)
        {
            log($"OSK prewarm: could not show cold-started '{naming.Suffix}': {exception.Message}");
        }
    }

    private static void WriteShowSignal(OskInstanceNaming naming, Action<string> log)
    {
        var signal = Path.Combine(AppContext.BaseDirectory, naming.ShowSignalFile);

        File.WriteAllText(signal, "show");
        log($"OSK prewarm: show signal written for '{naming.Suffix}'.");
    }

    /// <summary>
    /// Asks every resident overlay to end, on the way out of the session.
    /// </summary>
    /// <remarks>
    /// The exit signal rather than a kill, so the overlay closes its pipes and its window itself.
    /// The kernel would end them anyway — they are in this session's job — but a process asked to
    /// leave puts its own things away first.
    /// </remarks>
    internal static void StopAll(Action<string> log)
    {
        lock (Gate)
        {
            foreach (var (suffix, process) in Resident)
            {
                try
                {
                    var naming = OskInstanceNaming.FromSuffix(suffix);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, naming.ExitSignalFile), "exit");
                }
                catch (Exception exception)
                {
                    log($"OSK prewarm: could not ask '{suffix}' to exit: {exception.Message}");
                }

                try { process.Dispose(); } catch { }
            }

            Resident.Clear();
        }
    }

    private static bool IsAlive(string suffix)
    {
        if (!Resident.TryGetValue(suffix, out var process))
        {
            return false;
        }

        try
        {
            if (!process.HasExited)
            {
                return true;
            }
        }
        catch
        {
            // Gone in a way we cannot ask about; treated the same as gone.
        }

        Resident.Remove(suffix);

        return false;
    }
}
