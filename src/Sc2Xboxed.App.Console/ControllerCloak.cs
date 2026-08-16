using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Windows;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// Hides the controllers SteamXBox is holding from every other application.
/// </summary>
/// <remarks>
/// Without this a pad reaches two places at once. SteamXBox reads it and acts on it — closing the
/// overlay keyboard, opening the launcher — while the focused application reads the same physical
/// device and acts on it too, so one press of B both puts the keyboard away and cancels whatever the
/// user was doing. Nothing in user space can prevent that: a connected pad is readable by anyone.
/// HidHide is a filter driver, and it is the only mechanism that can.
///
/// <para>
/// <b>Cloaking outlives the process.</b> It survives a crash, a force-kill and a reboot, so a
/// session that ends badly would otherwise leave the user with a controller that works in no game at
/// all and nothing on screen to connect that to SteamXBox. The note file is what bounds that: what
/// was hidden is written down before it is hidden, so the next launch can undo exactly that much.
/// </para>
///
/// <para>
/// <b>Only ever what SteamXBox hid.</b> The first version cleared HidHide entirely at startup, and
/// the machine it was written on turned out to have a configuration of its own — a Steam Controller
/// hidden deliberately and a third-party DualSense tool on the allowed list. Clearing everything
/// would have quietly dismantled that on every launch. A user's own configuration is theirs.
/// </para>
/// </remarks>
public static class ControllerCloak
{
    /// <summary>
    /// Where the devices hidden by this product are written down.
    /// </summary>
    /// <remarks>
    /// <b>Beside the state, not beside the executable.</b> It used to live next to the binary, which
    /// meant a machine with two copies of SteamXBox had two notes, and neither could repair the
    /// other's mess. That is not hypothetical: the development machine carried an installed 3.2 and
    /// a portable 4.6, the installed one hid devices and was killed, and when it was uninstalled
    /// <b>its note went into the bin with it</b> — leaving a controller hidden from every game with
    /// nothing left on the machine that knew why.
    ///
    /// <para>
    /// Measured at that point: five devices hidden, two claimed by the surviving note. The three
    /// orphans would never have been given back.
    /// </para>
    /// </remarks>
    private static string NotePath => Path.Combine(StateFolder, "hidhide-hidden.state");

    private static string StateFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox");

    /// <summary>The place the note used to be kept, still read once so nothing is stranded.</summary>
    private static string LegacyNotePath => Path.Combine(AppContext.BaseDirectory, "hidhide-hidden.state");

    /// <summary>
    /// Brings a note left beside the executable over to the shared state.
    /// </summary>
    /// <remarks>
    /// Runs before anything reads the note. An installation that is upgraded rather than reinstalled
    /// still has its old note where the old code put it, and that note names devices somebody's
    /// games cannot see.
    /// </remarks>
    private static void AdoptLegacyNote()
    {
        try
        {
            if (!File.Exists(LegacyNotePath))
            {
                return;
            }

            Directory.CreateDirectory(StateFolder);

            if (File.Exists(NotePath))
            {
                // Both exist: the devices of each are owed to the user, so they are joined rather
                // than one chosen. An identifier named twice is unhidden twice, which is harmless.
                var merged = File.ReadAllLines(NotePath)
                    .Concat(File.ReadAllLines(LegacyNotePath).Skip(1))
                    .ToArray();

                File.WriteAllLines(NotePath, merged);
            }
            else
            {
                File.Move(LegacyNotePath, NotePath);
                return;
            }

            File.Delete(LegacyNotePath);
        }
        catch
        {
            // A note that cannot be moved is still readable where it is on the next attempt. Losing
            // the session over it would be the worse trade.
        }
    }

    /// <summary>First line of the note when this product is also what turned cloaking on.</summary>
    private const string TurnedCloakOn = "cloak-on-by-steamxbox";

    private static bool _warned;

    /// <summary>Whether the repair of an earlier session has already been done in this one.</summary>
    private static bool _repaired;

    /// <summary>Whether HidHide is on this machine at all.</summary>
    public static bool Available => HidHideConfigurator.IsInstalled;

    /// <summary>
    /// Undoes what an earlier session hid and did not release.
    /// </summary>
    /// <remarks>
    /// Called before the controllers are opened. A session killed before it could release its cloak
    /// is repaired by the next launch, without the user having to know any of this exists — which is
    /// the whole reason the feature is safe to switch on by default.
    /// </remarks>
    public static void Reset(DiagnosticLog log)
    {
        if (!Available || _repaired)
        {
            return;
        }

        // Once per process, and that is a fix rather than an optimisation. The call sits inside the
        // loop that waits for a controller to come back, so every time the last pad was switched off
        // this ran again: it gave back everything this very session had hidden, turned cloaking off,
        // and the line after it hid them all again. Observed twice in four minutes on 11 August
        // 2026. Repairing what an *earlier* session left behind is a thing to do once, at the start,
        // which is what it was written for.
        _repaired = true;

        AdoptLegacyNote();

        // Done whether or not there is a note: rubbish on the allowed list has nothing to do with
        // what this session hid, and nothing else will ever take it off.
        try
        {
            var hidhide = new HidHideConfigurator();
            var removed = hidhide.ForgetDeadApplications();

            if (removed.Count > 0)
            {
                log.WriteBlock(
                    LogLevel.Info,
                    LogCategory.Session,
                    $"HidHide: removed {removed.Count} allowed-list entry(ies) naming nothing:",
                    [.. removed.Select(entry => entry.Length == 0 ? "(entrée vide)" : entry)]);
            }

            // What could not be removed is said as plainly as what could. The command-line tool
            // refuses an empty path outright, so an entry like that survives every launch; a user
            // who wants it gone needs HidHide's own window, and needs to be told so.
            var stuck = hidhide.UnremovableRubbish();

            if (stuck.Count > 0)
            {
                log.Info(
                    LogCategory.Session,
                    $"HidHide: {stuck.Count} empty allowed-list entry(ies) remain and cannot be removed "
                    + "by the command-line tool. Remove them in the HidHide window if you want them gone.");
            }
        }
        catch (Exception exception)
        {
            log.Info(LogCategory.Session, $"HidHide allowed-list cleanup failed: {exception.Message}");
        }

        if (!File.Exists(NotePath))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(NotePath);
            var cloakWasOurs = lines.FirstOrDefault() == TurnedCloakOn;
            var devices = lines.Skip(1).Where(line => line.Length > 0).ToArray();

            new HidHideConfigurator().Unhide(devices, turnCloakOff: cloakWasOurs);
            File.Delete(NotePath);

            log.WriteBlock(
                LogLevel.Info,
                LogCategory.Session,
                $"HidHide: released {devices.Length} device(s) left hidden by an earlier session"
                + (cloakWasOurs ? ", and turned cloaking back off:" : ":"),
                devices);
        }
        catch (Exception exception)
        {
            log.Info(LogCategory.Session, $"HidHide reset failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Hides exactly the devices that were opened, and says so in the log.
    /// </summary>
    /// <remarks>
    /// Every hidden device is named. Hiding hardware has to be auditable afterwards, from the log
    /// alone, by somebody who was not here when it happened.
    /// </remarks>
    public static void Apply(DiagnosticLog log)
    {
        var devices = AttachedControllers.LastOpenedDeviceInstanceIds;

        if (devices.Count == 0)
        {
            return;
        }

        if (!Available)
        {
            // Said once, plainly, and not treated as an error. Everything works without HidHide
            // except this, and the user is entitled to know which half they have.
            if (!_warned)
            {
                _warned = true;

                log.Info(
                    LogCategory.Session,
                    "HidHide is not installed: the controller stays visible to other applications, so "
                    + "a button SteamXBox uses also reaches whatever is in the foreground. Install it "
                    + "from https://github.com/nefarius/HidHide/releases to stop that.");

                System.Console.WriteLine(
                    "HidHide is not installed; controller buttons will also reach other applications.");
            }

            return;
        }

        var application = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(application))
        {
            log.Info(LogCategory.Session, "HidHide: this executable's path is unknown; nothing hidden.");
            return;
        }

        try
        {
            var hidhide = new HidHideConfigurator();

            // Written before the driver is told anything. If the process dies between the two, the
            // note names a device that is not hidden — unhiding it next launch is harmless. The other
            // order would lose a hidden device on a crash, which is the failure that strands a
            // controller.
            // Asked once, and the two questions are kept apart on purpose. A device on HidHide's
            // list is hidden only while cloaking is on; switching cloaking off does not clear the
            // list. Confusing the two made this method give up exactly when it was needed most —
            // after a manual cloak-off, every controller was still listed, so nothing looked new,
            // and SteamXBox hid nothing at all while believing it had.
            var listed = hidhide.HiddenDevicePaths();
            var cloakOn = hidhide.IsCloakOn();

            // Never claimed: a device somebody else put on the list is their configuration, whatever
            // the switch happens to be doing.
            var candidates = devices
                .Where(id => !listed.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            // And never a keyboard or a mouse, nor anything carrying one. A pad-and-keyboard
            // combination is a single composite device: its gamepad interface is safe to hide, its
            // parent is not, and hiding the parent would leave the machine unable to type — which
            // includes being unable to reach the screen that would undo it.
            var ours = candidates
                .Where(id => !DeviceTree.IsOrCarriesKeyboardOrMouse(id))
                .ToArray();

            foreach (var refused in candidates.Except(ours, StringComparer.OrdinalIgnoreCase))
            {
                log.Info(
                    LogCategory.Session,
                    $"HidHide: refusing to hide {refused} — it is, or carries, a keyboard or a mouse.");
            }

            if (ours.Length == 0 && cloakOn)
            {
                // Everything we hold is listed and the switch is on, so it is genuinely hidden.
                return;
            }

            if (ours.Length == 0)
            {
                // Listed but not hidden. Nothing to add — the list is already right — but the switch
                // has to go on, and the fact that we are the ones who turned it on has to be written
                // down, or nobody will turn it off again.
                Directory.CreateDirectory(StateFolder);

                if (!File.Exists(NotePath))
                {
                    File.WriteAllLines(NotePath, [TurnedCloakOn]);
                }

                hidhide.HideOnly(application, [], turnCloakOn: true);

                log.Info(
                    LogCategory.Session,
                    "HidHide: the controllers were already on its list but cloaking was off, so none "
                    + "of them was actually hidden. Cloaking switched on.");

                return;
            }

            // Added to the note, never written over it. This runs again for every pad plugged in
            // mid-session, and overwriting cost both halves of the note at once:
            //
            //   * the device list became only the newly hidden pad, so everything hidden earlier
            //     was forgotten and nobody would ever give it back;
            //   * the header was recomputed from "is cloaking on?", which by then is yes — because
            //     we turned it on — so the record that it is ours to turn off was erased.
            //
            // Measured on 11 August 2026: a pad switched on mid-session left four devices hidden and
            // no note at all, with cloaking on and nothing on the machine claiming it.
            var existing = File.Exists(NotePath) ? File.ReadAllLines(NotePath) : [];
            var header = existing.FirstOrDefault() ?? "";
            var already = existing.Skip(1).Where(line => line.Length > 0);

            // Only decided when there is nothing to inherit. Once said, it stays said. The switch's
            // state was read once, above: asking again here would answer a question about a moment
            // that has already passed.
            if (header != TurnedCloakOn && existing.Length == 0 && !cloakOn)
            {
                header = TurnedCloakOn;
            }

            // The folder may not exist on a first run, and a note that cannot be written is a
            // controller nobody will give back.
            Directory.CreateDirectory(StateFolder);

            File.WriteAllLines(
                NotePath,
                new[] { header }.Concat(already.Concat(ours).Distinct(StringComparer.OrdinalIgnoreCase)));

            hidhide.HideOnly(application, ours);

            log.WriteBlock(
                LogLevel.Info,
                LogCategory.Session,
                $"HidHide: hiding {ours.Length} controller(s) from other applications:",
                ours);
        }
        catch (Exception exception)
        {
            log.Info(LogCategory.Session, $"HidHide could not hide the controllers: {exception.Message}");
        }
    }

    /// <summary>
    /// Gives one controller's device back, the moment that controller goes away.
    /// </summary>
    /// <remarks>
    /// Hiding a device is how SteamXBox stops a button reaching the foreground as well as itself.
    /// The moment a controller is switched off, unplugged or goes to sleep, that reason is gone —
    /// and until this existed the device stayed hidden from every other application for the rest of
    /// the session. A pad put to sleep in the evening was invisible to games until SteamXBox was
    /// closed, which is a long time to be punished for turning a controller off.
    ///
    /// <para>
    /// The note is rewritten as it goes, so that a session killed later gives back exactly what is
    /// still hidden and nothing else. Cloaking is switched off when the last of ours goes: a filter
    /// driver left on with nothing to filter is a cost with no purpose, and it is the state the
    /// user's own configuration would otherwise be judged against.
    /// </para>
    /// </remarks>
    public static void ReleaseOne(string identityId, DiagnosticLog log)
    {
        if (!Available || !File.Exists(NotePath))
        {
            return;
        }

        if (!AttachedControllers.DeviceByIdentity.TryGetValue(identityId, out var device))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(NotePath);
            var header = lines.FirstOrDefault() ?? "";
            var ours = lines.Skip(1).Where(line => line.Length > 0).ToList();

            if (!ours.Remove(device))
            {
                // Not one of ours, or already given back. Either way there is nothing owed.
                return;
            }

            var last = ours.Count == 0;

            new HidHideConfigurator().Unhide([device], turnCloakOff: last && header == TurnedCloakOn);

            if (last)
            {
                File.Delete(NotePath);
            }
            else
            {
                File.WriteAllLines(NotePath, new[] { header }.Concat(ours));
            }

            log.Info(
                LogCategory.Session,
                $"HidHide: gave back {device} when its controller left"
                + (last && header == TurnedCloakOn ? ", and turned cloaking off." : "."));
        }
        catch (Exception exception)
        {
            // The device stays hidden and the note still names it, so the next launch repairs it.
            log.Info(LogCategory.Session, $"HidHide: could not give back {device}: {exception.Message}");
        }
    }

    /// <summary>Gives the controllers back, on the way out.</summary>
    public static void Release(DiagnosticLog log)
    {
        if (!Available || !File.Exists(NotePath))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(NotePath);

            new HidHideConfigurator().Unhide(
                lines.Skip(1).Where(line => line.Length > 0).ToArray(),
                turnCloakOff: lines.FirstOrDefault() == TurnedCloakOn);

            File.Delete(NotePath);

            log.Info(LogCategory.Session, "HidHide: controllers released.");
        }
        catch (Exception exception)
        {
            // Worth being explicit about. The controller is still hidden, and the next launch is what
            // will repair it.
            log.Info(
                LogCategory.Session,
                $"HidHide release failed: {exception.Message}. The controllers stay hidden until "
                + "SteamXBox is started again, or until HidHide-Off.cmd is run.");
        }
    }
}
