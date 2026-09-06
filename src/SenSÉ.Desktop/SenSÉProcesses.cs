using System.Diagnostics;
using System.IO;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Desktop;

/// <summary>
/// Starts and stops the processes that make up SenSÉ.
/// </summary>
/// <remarks>
/// Desktop is the root of the environment: it brings the controller bridge up when it starts, and
/// takes everything down when it closes. Before this, closing a window left the core and the
/// overlay keyboard running invisibly — a background process holding a HID device with nothing on
/// screen to say so, which is how a user ends up with a controller that does not work in another
/// application and no idea why.
/// </remarks>
internal static class SenSÉProcesses
{
    private const string Core = "SenSÉ.Core";
    private const string Overlay = "SenSÉ.Osk";
    private const string Configuration = "SenSÉ";

    /// <summary>Starts the controller bridge in the background if it is not already running.</summary>
    public static void StartCore()
    {
        try
        {
            if (IsRunning(Core))
            {
                UiLog.Process(Core, "already running; not started again");
                return;
            }

            var executable = Path.Combine(AppContext.BaseDirectory, Core + ".exe");
            if (!File.Exists(executable))
            {
                // Silently doing nothing here is how the environment came up with no bridge at all
                // and nothing anywhere saying why.
                UiLog.Error($"process {Core}: executable not found at {executable}");
                return;
            }

            var arguments = BuildArguments();
            UiLog.Process(Core, $"starting: {executable} {arguments}");

            // Minimised, not hidden. CreateNoWindow suppressed the console entirely, which left the
            // bridge with no way to be looked at: its log scrolls in that window, and a background
            // process holding a HID device with no visible trace is exactly what makes a controller
            // problem impossible to diagnose. UseShellExecute is required for WindowStyle to apply.
            var started = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
            });

            // Le pont meurt avec l'environnement, y compris si celui-ci est tue : c'est la
            // promesse que fait le commentaire en tete de cette classe, et un finally ne la
            // tient pas. Voir JobEnfants.
            JobEnfants.Inscrire(started);

            UiLog.Process(Core, started is null ? "start returned no process" : $"started, PID {started.Id}");
        }
        catch (Exception ex)
        {
            // The Controller tile reports the state, so a failure here is visible where it matters
            // rather than as a dialog on top of whatever the user was doing — but the tile says only
            // that it is not running, never why.
            UiLog.Failure($"starting {Core}", ex);
        }
    }

    /// <summary>
    /// The command line the bridge needs, built from the profile the user last selected.
    /// </summary>
    /// <remarks>
    /// This was the bug. The environment used to start the core with no arguments at all: without
    /// <c>xbox-run</c> there is no virtual gamepad, so switching to Xbox did nothing, and without
    /// <c>--profile</c> there is no mapping, so the profile shortcuts did nothing. The controller
    /// looked broken in both modes because it had been started as neither.
    ///
    /// Falling back to the defaults rather than refusing to start: a first run has no profile
    /// recorded yet, and a bridge running on the default mapping is far better than none.
    /// </remarks>
    private static string BuildArguments()
    {
        var settings = App.SettingsSvc.Settings;
        var profile = LoadActiveProfile(settings.LastActiveProfile);

        return SenSÉ.Core.Runtime.CoreCommandLine.BuildRun(
            profile?.Name ?? SenSÉ.Core.Runtime.CoreCommandLine.DefaultProfile,
            profile?.Mode ?? "profile",
            profile?.SwitchButton ?? SenSÉ.Core.Runtime.CoreCommandLine.DefaultSwitchButton);
    }

    private static ProfileData? LoadActiveProfile(string? name)
    {
        try
        {
            var profiles = new ProfileService();
            profiles.LoadAll();

            var chosen = profiles.Profiles.FirstOrDefault(p =>
                             string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                         ?? profiles.Profiles.FirstOrDefault();

            if (chosen is null)
            {
                UiLog.Warn($"no profile found (wanted '{name}'); the bridge starts on the defaults");
            }
            else if (!string.Equals(chosen.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                UiLog.Warn($"profile '{name}' not found; falling back to '{chosen.Name}'");
            }

            return chosen;
        }
        catch (Exception ex)
        {
            UiLog.Failure("loading the profiles", ex);
            return null;
        }
    }

    public static bool IsCoreRunning() => IsRunning(Core);

    /// <summary>
    /// Shuts down everything that belongs to SenSÉ.
    /// </summary>
    /// <remarks>
    /// The overlay keyboard is asked first, through the exit signal it already watches for: it owns
    /// a topmost window and can leave a latched modifier key behind if it is killed outright. The
    /// core is closed next, then the configuration window. Killing is the fallback, not the plan —
    /// but it is a fallback, because a process that refuses to close must not survive the
    /// environment that owns it.
    /// </remarks>
    public static void StopAll()
    {
        UiLog.Info("stopping every SenSÉ process");
        SignalOverlayExit();

        foreach (var name in new[] { Overlay, Core, Configuration })
        {
            foreach (var process in SafeProcesses(name))
            {
                var pid = -1;
                try
                {
                    pid = process.Id;

                    if (!process.CloseMainWindow() || !process.WaitForExit(1500))
                    {
                        // Worth recording rather than assuming: a process that has to be killed is
                        // one that ignored a close request, and that is a defect of its own.
                        UiLog.Process(name, $"PID {pid} did not close on request; killing");
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        UiLog.Process(name, $"PID {pid} closed cleanly");
                    }
                }
                catch (Exception ex)
                {
                    // Already gone, or not ours to close.
                    UiLog.Failure($"stopping {name} (PID {pid})", ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    /// <summary>Writes the file signal the overlay watches, so it can close itself cleanly.</summary>
    private static void SignalOverlayExit()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "osk-exit.signal"),
                DateTime.UtcNow.Ticks.ToString());
        }
        catch (Exception ex)
        {
            // The kill below still covers it.
            UiLog.Failure("writing the overlay exit signal", ex);
        }
    }

    private static bool IsRunning(string name)
    {
        var found = SafeProcesses(name);
        try
        {
            return found.Length > 0;
        }
        finally
        {
            foreach (var process in found)
            {
                process.Dispose();
            }
        }
    }

    private static Process[] SafeProcesses(string name)
    {
        try
        {
            // Never this process: Desktop is called SenSÉ.Desktop, which does not match, but a
            // future rename must not make the environment kill itself.
            var self = Environment.ProcessId;
            return Process.GetProcessesByName(name).Where(p => p.Id != self).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
