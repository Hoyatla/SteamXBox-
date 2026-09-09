using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SenSÉ.Core.Diagnostics;
using SenSÉ.Core.Input;
using SenSÉ.Core.Haptics;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Osk;
using SenSÉ.Core.Output;
using SenSÉ.Core.Runtime;
using HidSharp;
using SenSÉ.Hid;
using SenSÉ.VirtualGamepad;
using SenSÉ.Windows;

Action<string>? DebugLog = null;

if (args.Length > 0)
{
    if (args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
    {
        var logPath = CheminDebug.DebugLog("console");
        var logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };
        DebugLog = (string msg) =>
        {
            // Local time: these lines land in SenSÉ-debug.log beside the structured ones, which
            // are local. In UTC the same file carried two clocks two hours apart — see the note in
            // the overlay's Log, where that difference sent a diagnosis down the wrong road.
            var line = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            logFile.WriteLine(line);
        };
        DebugLog($"=== SenSÉ debug log ===");
        DebugLog($"Log file: {logPath}");
        DebugLog($"Exe path: {Environment.ProcessPath ?? AppContext.BaseDirectory}");
    }
    await RunCommandAsync(args, DebugLog);
    return;
}

MinimizeConsoleWindow();
await RunXbox360LiveAsync(new[]
{
    "xbox-run",
    "--restart",
    "--switch-button",
    "quick-access"
}, null);

static void LogHidEnumeration(Action<string> dlog)
{
    dlog("--- HID Enumeration Start ---");
    try
    {
        var allValve = HidSharp.DeviceList.Local
            .GetHidDevices(SteamHidConstants.ValveVendorId)
            .ToArray();
        dlog($"Total Valve HID devices: {allValve.Length}");

        foreach (var device in allValve)
        {
            int inputLen = 0, outputLen = 0, featureLen = 0;
            string inputErr = "", outputErr = "", featureErr = "";
            try { inputLen = device.GetMaxInputReportLength(); } catch (Exception ex) { inputErr = $"{ex.GetType().Name}:{ex.Message}"; }
            try { outputLen = device.GetMaxOutputReportLength(); } catch (Exception ex) { outputErr = $"{ex.GetType().Name}:{ex.Message}"; }
            try { featureLen = device.GetMaxFeatureReportLength(); } catch (Exception ex) { featureErr = $"{ex.GetType().Name}:{ex.Message}"; }

            bool canOpen = false;
            string openErr = "";
            try
            {
                if (device.TryOpen(out var s)) { canOpen = true; s.Dispose(); }
                else { openErr = "TryOpen=false"; }
            }
            catch (Exception ex) { openErr = $"{ex.GetType().Name}:{ex.Message}"; }

            bool knownProduct = SteamHidConstants.IsKnownSteamControllerProduct(device.ProductID);
            bool isControllerState = inputLen >= 54 && outputLen > 0 && featureLen > 0;

            dlog($"  PID=0x{device.ProductID:X4} known={knownProduct} input={inputLen}{(inputErr != "" ? $" ERR({inputErr})" : "")} output={outputLen}{(outputErr != "" ? $" ERR({outputErr})" : "")} feature={featureLen}{(featureErr != "" ? $" ERR({featureErr})" : "")} canOpen={canOpen}{(openErr != "" ? $" ERR({openErr})" : "")} isControllerState={isControllerState} path={device.DevicePath}");
        }

        var discovery = new SteamHidDiscovery(dlog);
        var preferred = discovery.FindPreferredControllerDevice();
        dlog($"FindPreferredControllerDevice result: {(preferred is null ? "NULL" : $"PID=0x{preferred.ProductID:X4} path={preferred.DevicePath}")}");
    }
    catch (Exception ex)
    {
        dlog($"HID enumeration EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        dlog(ex.StackTrace ?? "");
    }
    dlog("--- HID Enumeration End ---");
}

static void RunMappingSanityCheck()
{
    var mapper = new ControllerOutputMapper();

    var frame = ControllerState.Empty(TimeSpan.Zero) with
    {
        Buttons =
            SteamControllerButtons.L4 |
            SteamControllerButtons.R4 |
            SteamControllerButtons.L5 |
            SteamControllerButtons.R5
    };

    var output = mapper.Map(frame);

    Console.WriteLine("SenSÉ profile sanity check");
    Console.WriteLine($"Buttons: {output.Gamepad.Buttons}");
    Console.WriteLine("Expected rear mapping: L4=X, R4=Y, L5=A, R5=B");
    Console.WriteLine();
    PrintUsage();
}

static void MinimizeConsoleWindow()
{
    var window = NativeMethods.GetConsoleWindow();
    if (window != IntPtr.Zero)
    {
        _ = NativeMethods.ShowWindow(window, NativeMethods.SwHide);
    }
}

static async Task RunCommandAsync(string[] args, Action<string>? debugLog = null)
{
    switch (args[0])
    {
        case "hid-list":
            ListHidDevices();
            return;
        case "hid-probe":
            ProbeHidReports();
            return;
        case "haptic-test":
            await RunHapticTestAsync(args);
            return;
        case "xbox-run":
            await RunXbox360LiveAsync(args, debugLog);
            return;
        case "stop":
            StopOtherInstances(waitForExit: true);
            return;
        case "hidhide-setup":
            ConfigureHidHide();
            return;
        case "hidhide-status":
            PrintHidHideStatus();
            return;
        case "hidhide-off":
            DisableHidHide();
            return;
        case "pads-cleanup":
            CleanUpVirtualPadRecords();
            return;
        case "help":
            PrintUsage();
            return;
        case "sanity":
            RunMappingSanityCheck();
            return;
        case "hid-diag":
            RunHidDiagnostic();
            return;
        case "diag":
            RunDiagnosticReport(args);
            return;
        case "haptic-sides":
            RunHapticSideSweep();
            return;
        case "haptic-probe":
            RunHapticActuatorProbe(args);
            return;
        case "haptic-format-test":
            RunHapticFormatTest();
            return;
        case "power-off":
            RunPowerOffProbe(args);
            return;
        case "ds-power-off":
            RunDualSensePowerOffProbe();
            return;
        case "ds-bt-cut":
            RunDualSenseBtCut();
            return;
        default:
            PrintUsage();
            return;
    }
}

static void ListHidDevices()
{
    var discovery = new SteamHidDiscovery();
    var devices = discovery.ListValveDevices();

    if (devices.Count == 0)
    {
        Console.WriteLine("No Valve HID device found.");
        return;
    }

    foreach (var device in devices)
    {
        Console.WriteLine($"{device.ProductName} ({device.ProductIdHex})");
        Console.WriteLine($"  Manufacturer: {device.Manufacturer}");
        Console.WriteLine($"  Serial: {device.SerialNumber}");
        Console.WriteLine($"  Reports: input={device.MaxInputReportLength}, output={device.MaxOutputReportLength}, feature={device.MaxFeatureReportLength}");
        Console.WriteLine($"  CanOpen: {device.CanOpen}");
        if (!string.IsNullOrWhiteSpace(device.OpenError))
        {
            Console.WriteLine($"  OpenError: {device.OpenError}");
        }

        Console.WriteLine($"  Path: {device.DevicePath}");
    }
}

static void ProbeHidReports()
{
    var probe = new SteamHidProbe();
    var parser = new TritonInputReportParser();
    var reports = probe.CaptureInputReports(TimeSpan.FromSeconds(3), maxReports: 32);

    if (reports.Count == 0)
    {
        Console.WriteLine("No input report captured. Try moving sticks/touchpads while running the probe, and close Steam if it has exclusive access.");
        return;
    }

    foreach (var report in reports)
    {
        Console.WriteLine($"{report.Timestamp:O} {report.Data.Length} bytes {report.Hex}");
        if (parser.TryParse(report.Data, report.Timestamp.TimeOfDay, out var state))
        {
            Console.WriteLine(
                $"  parsed buttons={state.Buttons} " +
                $"ls=({state.LeftStick.X:F3},{state.LeftStick.Y:F3}) " +
                $"rs=({state.RightStick.X:F3},{state.RightStick.Y:F3}) " +
                $"lt={state.LeftTrigger:F3} rt={state.RightTrigger:F3} " +
                $"lp=t:{state.LeftPad.IsTouched} p:{state.LeftPad.Pressure:F3} click:{state.LeftPad.IsPressed} xy=({state.LeftPad.X:F3},{state.LeftPad.Y:F3}) " +
                $"rp=t:{state.RightPad.IsTouched} p:{state.RightPad.Pressure:F3} click:{state.RightPad.IsPressed} xy=({state.RightPad.X:F3},{state.RightPad.Y:F3})");
        }
    }
}

static async Task RunHapticTestAsync(string[] args)
{
    if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("Refusing to send haptic reports without --yes.");
        Console.WriteLine("Run: dotnet run --project src\\SenSÉ.App.Console -- haptic-test --yes");
        return;
    }

    await using var sink = new TritonHapticSink();
    var frame = new HapticOutputFrame(new[]
    {
        HapticCommand.TouchClick(HapticActuator.LeftTrackpad),
        HapticCommand.TouchClick(HapticActuator.RightTrackpad)
    });

    await sink.SubmitAsync(frame, CancellationToken.None);
    await Task.Delay(100);
    await sink.SubmitAsync(new HapticOutputFrame(new[]
    {
        HapticCommand.Stop(HapticActuator.LeftTrackpad),
        HapticCommand.Stop(HapticActuator.RightTrackpad)
    }), CancellationToken.None);

    Console.WriteLine("Sent trackpad haptic click test reports.");
}

/// <summary>
/// Stands SenSÉ down for as long as Steam owns the controller, then restores everything.
/// Returns false when cancelled, which means the caller should exit rather than reclaim.
/// </summary>
static async Task<bool> WaitWhileSteamOwnsAsync(
    SteamPresenceWatcher watcher,
    VirtualPadSet virtualPads,
    TritonHapticSink haptics,
    SenSÉ.App.Console.OskInstanceSet oskInstances,
    Action<string> log,
    DiagnosticLog diagnostics,
    CancellationToken cancellationToken)
{
    log("Releasing the controller to Steam.");

    // The one that actually matters, and the one that was missing. Everything else released here
    // is held in user space: haptics, the overlay, the virtual pads. HidHide is a filter driver —
    // while it hides the controller, Steam cannot see it no matter how politely SenSÉ has
    // stood down. So SenSÉ let go of everything except the thing that was in the way, and the
    // controller lost its native Steam support without anything on screen to say why.
    SenSÉ.App.Console.ControllerCloak.Release(diagnostics);

    // Mute before dropping the stream so a queued overlay tick cannot reopen the device.
    haptics.Muted = true;
    haptics.Reset();

    StopOskOverlay(oskInstances, log);

    // Unplug every virtual pad. Leaving one plugged in makes Steam enumerate a phantom controller
    // (a virtual Xbox 360 pad) alongside the real one, and games launched from Steam may bind to
    // the phantom.
    await virtualPads.DisposeAsync();
    log($"Virtual pad(s) unplugged ({virtualPads.Count} still connected).");

    Console.WriteLine("Steam owns the controller. Standing by...");

    while (true)
    {
        try
        {
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (watcher.Poll(DateTimeOffset.UtcNow) && watcher.Owner == ControllerOwner.SenSÉ)
        {
            break;
        }
    }

    haptics.Muted = false;

    // Steam does not put the controller back the way it found it. Steam Input takes over the
    // firmware layer for as long as it runs, and on the way out it leaves whatever it last wrote —
    // so a controller that has been through a Steam session is in a state neither Steam nor
    // SenSÉ chose. Reclaiming has to start from a known one rather than assume.
    //
    // The reset is a request, not a step that may fail the reclaim: the device may still be busy,
    // or gone with Steam. Whatever it answers, the source below reopens it and applies the
    // firmware layer this session wants.
    SenSÉ.App.Console.ControllerReset.ToNative(log);

    // No replug here: the pads come back the moment the controller is re-opened, in the loop's
    // next turn, when the attached controllers are given their pads again. The cloak comes back
    // the same way, on the next ControllerCloak.Apply — hiding it now would take the controller
    // from Steam's own shutdown, which is still finishing.
    log("Reclaiming; virtual pads and cloaking return on the next controller input.");

    return true;
}

/// <summary>
/// Stands by after the user switched the controller off, until it comes back on.
/// </summary>
/// <remarks>
/// Deliberately unbounded, unlike the disconnection path, which gives up after ten retries and exits.
/// A controller switched off on purpose may be switched back on in ten seconds or tomorrow morning,
/// and either way SenSÉ should still be there. Returns false only when cancelled.
/// </remarks>
static async Task<bool> WaitForControllerReturnAsync(
    VirtualPadSet virtualPads,
    TritonHapticSink haptics,
    SenSÉ.App.Console.OskInstanceSet oskInstances,
    IReadOnlyCollection<int> physicalSlots,
    string forced,
    Action<string> log,
    CancellationToken cancellationToken)
{
    log("Controller powered off; standing by until it comes back.");
    Console.WriteLine("Controller off. Waiting for it to come back on...");

    haptics.Muted = true;
    haptics.Reset();
    StopOskOverlay(oskInstances, log);

    // Neutralise and release every pad so no game holds a phantom player while the controller
    // is off. They come back on the next Xbox input.
    await virtualPads.DisposeAsync();
    log($"Virtual pad(s) released ({virtualPads.Count} still connected).");

    while (true)
    {
        try
        {
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        // Any supported family — Steam, PlayStation or Xbox — ends the wait. The one that was
        // switched off need not be the one that comes back: the user may pick up another pad, and
        // standing by forever because a DualSense is not a Valve device is the bug this replaced.
        if (SenSÉ.App.Console.AttachedControllers.AnyAttached(physicalSlots, forced, log))
        {
            break;
        }
    }

    log("Controller detected again; resuming.");
    Console.WriteLine("Controller back on, resuming.");

    haptics.Muted = false;

    return true;
}

/// <summary>
/// Brings the SenSÉ environment up, or back, from the controller.
/// </summary>
/// <remarks>
/// A separate process, started or signalled — never hosted here. The core runs without a desktop,
/// as a minimised console or a service, and must not acquire a UI thread to show a window.
///
/// When Desktop is already running it may simply be hidden: the overlay hides itself for the
/// actions that need the foreground. The signal file is what brings it back, and it is the same
/// mechanism the overlay keyboard already uses — one convention in the product rather than two.
/// </remarks>
/// <summary>
/// Reads the controller-to-profile assignments the configuration window wrote.
/// </summary>
/// <remarks>
/// The same file the GUI writes, read here rather than passed on the command line: assignments
/// change while the bridge is running, and a command line is fixed at launch.
///
/// A missing file is the normal first run, not a fault: every controller then falls back to the
/// profile the bridge was started with.
/// </remarks>
/// <summary>Where the controller-to-profile assignments live.</summary>
static string ControllerProfilesPath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SenSÉ", "controller-profiles.json");

/// <summary>
/// When the assignments were last written, or default when there are none.
/// </summary>
/// <remarks>
/// A timestamp rather than a file watcher, matching what the Xbox profile already does. The
/// configuration window writes the file in one go, so there is no partial state to catch, and a
/// check once a second is invisible next to a change made by hand.
/// </remarks>
/// <summary>
/// The moment anything the profiles depend on last changed: the assignments, or any profile itself.
/// </summary>
/// <remarks>
/// This used to watch the assignment file alone. Assigning a family to another profile therefore
/// reloaded, and <b>editing a profile did not</b> — its own file was rewritten while
/// controller-profiles.json sat untouched, so the comparison below found nothing new and the bridge
/// kept the mappers it had built at startup. The setting was correct on disk and had no effect until
/// the next launch.
///
/// <para>
/// Reported 14 August: a wheel chosen for a stick, saved, and never applied. The editor wrote it and
/// the loader would have read it — nothing was listening for the file to change.
/// </para>
///
/// <para>
/// The most recent write across the folder, not a per-file watch: the reload rebuilds every session
/// anyway, so knowing <i>which</i> profile changed buys nothing, and one timestamp keeps the
/// comparison at the call site exactly as it was.
/// </para>
/// </remarks>
static DateTime ControllerProfilesTimestamp()
{
    try
    {
        var newest = default(DateTime);

        var assignments = ControllerProfilesPath();
        if (File.Exists(assignments))
        {
            newest = File.GetLastWriteTimeUtc(assignments);
        }

        var profiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SenSÉ", "profiles");

        if (Directory.Exists(profiles))
        {
            foreach (var file in Directory.EnumerateFiles(profiles, "*.json"))
            {
                var written = File.GetLastWriteTimeUtc(file);

                if (written > newest)
                {
                    newest = written;
                }
            }
        }

        return newest;
    }
    catch
    {
        // An unreadable timestamp must not stop the bridge; it only means no reload this second.
        return default;
    }
}

/// <summary>
/// Builds one controller's gamepad mappers from its family's layout: the Xbox report mapper every
/// family uses, and the DualShock 4 report mapper kept for the DS4 sink family. Both read the same
/// profile section, so a family with a profile of its own maps from that profile's layout; families
/// without one keep the layout the bridge was launched with (the statics). One file carries the
/// whole pad, and one reload covers both mappers.
/// </summary>
static (ControllerOutputMapper Xbox, DualSenseGamepadMapper DualSense) BuildGamepadMappersFor(
    string controllerId,
    string familyId,
    ControllerKind kind,
    ControllerProfileBook book,
    DiagnosticLog log)
{
    var xboxMapper = new ControllerOutputMapper();
    var dualSenseMapper = new DualSenseGamepadMapper();

    if (!book.HasOwnProfile(familyId))
    {
        // No profile of its own: the controller still gets its family's button layout. A DualSense
        // is not a Steam Controller, and the Menu/View quirk was measured on a Steam Controller.
        //
        // Une instance de correspondance PAR mapper. Les deux recevaient la meme, et XboxButtonMap
        // est modifiable : deux mappers tenant le meme objet sont deux mappers dont l'un rebranche
        // les boutons de l'autre. Les valeurs viennent du fichier de la famille — PS5 et Xbox ont
        // chacune le sien — et jamais des statiques du processus, qui portent le profil de lancement
        // et sont la facon dont le reglage d'une manette atteignait toutes les autres.
        xboxMapper.ButtonMap = XboxButtonMap.DefaultFor(kind);
        dualSenseMapper.ButtonMap = XboxButtonMap.DefaultFor(kind);

        var familyTuning = SenSÉProfileSettings.DefaultFor(kind).XboxTuning;
        xboxMapper.Tuning = familyTuning;
        dualSenseMapper.Tuning = familyTuning;

        return (xboxMapper, dualSenseMapper);
    }

    var name = book.ProfileFor(familyId);

    try
    {
        var settings = ProfileMapper.LoadDetailed(name).Settings;

        // Une instance par mapper, lue deux fois depuis le meme profil. Partager l'objet laissait
        // les deux mappers d'une meme manette s'ecrire dessus.
        xboxMapper.ButtonMap = XboxButtonMap.FromDictionary(settings.XboxButtons, kind);
        xboxMapper.Tuning = settings.XboxTuning;
        dualSenseMapper.ButtonMap = XboxButtonMap.FromDictionary(settings.XboxButtons, kind);
        dualSenseMapper.Tuning = settings.XboxTuning;

        log.Info(LogCategory.Session, $"Controller {controllerId} uses Xbox layout from profile '{name}'.");
    }
    catch (Exception ex)
    {
        // Falling back to the family's own layout and tuning rather than refusing: a controller
        // whose profile was deleted must still work in a game. To the family's, never to the
        // process-wide statics — those hold the launch profile's values, and inheriting them here
        // is how one controller's tuning reached every other one.
        var familyTuning = SenSÉProfileSettings.DefaultFor(kind).XboxTuning;

        xboxMapper.ButtonMap = XboxButtonMap.DefaultFor(kind);
        xboxMapper.Tuning = familyTuning;
        dualSenseMapper.ButtonMap = XboxButtonMap.DefaultFor(kind);
        dualSenseMapper.Tuning = familyTuning;

        log.Warn(LogCategory.Session,
            $"Controller {controllerId}: profile '{name}' unusable ({ex.GetType().Name}); using the {kind} defaults.");
    }

    return (xboxMapper, dualSenseMapper);
}

static ControllerProfileBook LoadControllerProfiles(string fallbackProfile, DiagnosticLog log)
{
    var book = new ControllerProfileBook(fallbackProfile);

    try
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SenSÉ", "controller-profiles.json");

        if (File.Exists(path))
        {
            book.Load(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path)));

            log.Info(LogCategory.Session, $"Profiles by family: {book.Count} assignment(s) from {path}.");
        }
        else
        {
            log.Info(LogCategory.Session, $"No family assignments; every controller uses '{fallbackProfile}'.");
        }
    }
    catch (Exception ex)
    {
        log.Warn(LogCategory.Session, $"Reading the per-controller profiles: {ex.GetType().Name}: {ex.Message}");
    }

    return book;
}

/// <summary>
/// Builds one controller's desktop mapper from its own profile.
/// </summary>
/// <remarks>
/// This is what makes a per-controller profile mean anything at runtime. Until now every session
/// was built from the profile the bridge was launched with, so the assignments were recorded,
/// displayed, and never applied — a setting that looks like it works and does nothing.
/// </remarks>
/// <summary>
/// How one controller's sticks drive the pointer, taken from that controller's own profile.
/// </summary>
/// <remarks>
/// All three come from the profile now, none from a constant. They used to disagree: the mapper's
/// default dead zone is 0.15 while the profile on disk said 0.06, so a quarter of the stick's usable
/// travel was thrown away and the setting in the GUI changed nothing.
/// </remarks>
static StickPointerSettings StickPointerFor(ProfileMapper mapper)
    => new(
        DeadZone: mapper.Settings.RightStickDeadZone,
        PixelsPerSecond: mapper.Settings.StickPointerSpeed,
        Curve: mapper.Settings.StickPointerCurve);

/// <summary>
/// Whether this controller's keyboard should float, for a keyboard being started at launch.
/// </summary>
/// <remarks>
/// Read the same way a cold start reads it — the controller's own profile, the fallback otherwise —
/// so the resident overlay is pinned or floating exactly as the one a toggle would start would be.
/// A failure to read the profile defaults to floating, which is also the profile default.
/// </remarks>
static bool OskFloatingFor(string familyId, ControllerProfileBook book, DiagnosticLog log)
{
    try
    {
        return ProfileMapper.LoadDetailed(book.ProfileFor(familyId)).Settings.OskFloating;
    }
    catch (Exception ex)
    {
        log.Warn(LogCategory.Session,
            $"Family {familyId}: could not read OskFloating ({ex.GetType().Name}); defaulting to floating.");
        return true;
    }
}

static ProfileMapper BuildMapperFor(
    string controllerId,
    string familyId,
    ControllerProfileBook book,
    SenSÉProfileSettings? fallback,
    ControllerKind kind,
    DiagnosticLog log)
{
    var name = book.ProfileFor(familyId);

    if (book.HasOwnProfile(familyId))
    {
        try
        {
            var loaded = ProfileMapper.LoadDetailed(name);
            log.Info(LogCategory.Session, $"Controller {controllerId} uses profile '{name}'.");
            return new ProfileMapper(loaded.Settings);
        }
        catch (Exception ex)
        {
            // Falling back rather than refusing: a family whose profile was deleted must still
            // work, and the line below says which one and why.
            log.Warn(LogCategory.Session,
                $"Controller {controllerId}: profile '{name}' unusable ({ex.GetType().Name}); using the default.");
        }
    }

    // Said out loud. A family with no profile of its own runs on the settings of whichever
    // profile the bridge was launched with — which is another family's: an Xbox pad silently
    // inheriting a Steam Controller's trackpad tuning. It happened without a single line in the
    // log, so from the outside it looked like the pad had its own settings and they were wrong.
    // The family's own defaults, not the launch profile's. The launch profile belongs to whichever
    // controller happened to be first, and it is almost always a Steam Controller one: pads driving
    // the pointer, sticks free for the arrow keys. Handed to a DualSense or an Xbox pad, which have
    // no pads at all, that wires the left stick to the arrows — and a stick resting a few percent
    // off centre then holds a direction down for the whole session.
    //
    // Reported 14 August as "clavier et souris physique cassé", after the PS5 and Steam profiles
    // were deleted and both pads fell back on this line.
    var familyDefaults = SenSÉProfileSettings.DefaultFor(kind);

    log.Info(LogCategory.Session,
        $"Controller {controllerId} has no profile of its own; using the {kind} defaults "
        + $"(left stick: {familyDefaults.LeftStickMode}, right stick: {familyDefaults.RightStickMode}).");

    return new ProfileMapper(familyDefaults);
}


/// <summary>
/// The keyboard executable of a controller family.
/// </summary>
/// <remarks>
/// One per family, each produced by its own project. There used to be two names for a single binary
/// copied by hand — PS5 and Xbox shared the same copy, and the program read its own file name back
/// to work out which family it was serving. Nothing in the build made those names, so a build
/// without the copy left stale executables running beside fresh ones.
///
/// <para>
/// The old names are still tried, so a folder that has not been rebuilt keeps working rather than
/// opening no keyboard at all.
/// </para>
/// </remarks>
static string OverlayExecutableFor(ControllerKind kind) => kind switch
{
    ControllerKind.SteamController => "SenSÉSteam.Osk.exe",
    ControllerKind.DualSense => "SenSÉPS5.Osk.exe",
    _ => "SenSÉXbox.Osk.exe",
};

/// <summary>Short readable form of a controller id, for the one-line counters.</summary>
/// <remarks>
/// Both ends, because either one alone collapses different controllers into the same text — and a
/// counter line that shows one pad three times is worse than no counter line at all.
///
/// <para>
/// The tail alone was kept, on the reasoning that it holds the serial that separates two pads of the
/// same model. True for <c>usb:</c> keys. False for the container ids added since: Windows derives
/// them partly from the machine, so every one of them here ended in <c>d4e98a60d0e6</c> and three
/// distinct Xbox pads printed as the same controller. The head alone fails the opposite way, two
/// same-model pads sharing <c>usb:vid_…&amp;pid_…</c>. Keeping both is the only form that separates
/// every case this project actually produces.
/// </para>
/// </remarks>
/// <summary>
/// Removes the device records SenSÉ's own virtual pads left behind.
/// </summary>
/// <remarks>
/// Run from the uninstaller, which is already elevated. Leaving them is the same fault the 3.2
/// uninstaller committed with its startup entry: state that outlives the product and that nobody
/// will ever come back for.
///
/// <para>
/// Only what the ledger recorded. The rule "every absent VID_045E&amp;PID_028E" also matches a real
/// wired Xbox 360 controller, because being indistinguishable from one is what the emulation is for.
/// </para>
/// </remarks>
static void CleanUpVirtualPadRecords()
{
    var recorded = SenSÉ.Windows.VirtualPadLedger.Read();

    Console.WriteLine($"Ledger: {SenSÉ.Windows.VirtualPadLedger.Path}");
    Console.WriteLine($"Recorded device nodes: {recorded.Count}");

    if (recorded.Count == 0)
    {
        return;
    }

    var (removed, refused) = SenSÉ.Windows.VirtualPadLedger.Cleanup(Console.WriteLine);

    Console.WriteLine($"Removed {removed}, refused {refused}.");

    if (refused > 0)
    {
        // Named rather than swallowed. Refusals here almost always mean the command ran without
        // administrator, and a cleanup that reports success while removing nothing is worse than one
        // that does not run.
        Console.WriteLine("Refusals are usually a missing administrator. What resisted stays in the ledger.");
    }
}

static string Shorten(string id)
    => id.Length <= 20 ? id : id[..8] + "…" + id[^8..];
/// <summary>Reads --source steam|xinput, or an empty string when the choice is automatic.</summary>
static string ReadForcedSource(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--source", StringComparison.OrdinalIgnoreCase))
        {
            var value = args[i + 1].Trim().ToLowerInvariant();
            return value is "steam" or "xinput" ? value : "";
        }
    }

    return "";
}

/// <summary>Asks every open overlay keyboard to close, if any are running.</summary>
static void StopOskOverlay(SenSÉ.App.Console.OskInstanceSet oskInstances, Action<string> log)
{
    bool signaled = false;
    try
    {
        // One signal per keyboard. The generic file is written too, so an overlay without an
        // instance still hears it.
        var signalNames = oskInstances.All
            .Select(i => i.Naming.CloseSignalFile)
            .Append("osk-close.signal")
            .Distinct(StringComparer.Ordinal);

        foreach (var signalName in signalNames)
        {
            var closeSignalPath = CheminDebug.Signal(Path.GetFileNameWithoutExtension(signalName));
            File.WriteAllText(closeSignalPath, DateTime.UtcNow.Ticks.ToString());
        }

        signaled = true;
        log("OSK close signal(s) written.");
    }
    catch (Exception exception)
    {
        log($"OSK close signal failed: {exception.GetType().Name}: {exception.Message}");
    }

    if (!signaled)
    {
        KillOskProcesses();
    }

    // Same insurance as the toggle path: never leave a latched SHIFT behind.
    InputHelper.KeyUp(0xA0);
}

/// <summary>
/// Every executable an overlay keyboard can run under, regardless of the instance naming.
/// </summary>
static void KillOskProcesses()
{
    foreach (var name in new[] { "SenSÉPads.Osk", "SenSÉSticks.Osk", "SenSÉ.Osk" })
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }
}

static async Task RunXbox360LiveAsync(string[] args, Action<string>? debugLog = null)
{
    var logPath = CheminDebug.DebugLog("console");

    using var log = new DiagnosticLog(
        logPath,
        level: ReadLogLevel(args),
        categories: ReadLogCategories(args),
        alsoConsole: args.Contains("--debug", StringComparer.OrdinalIgnoreCase));

    // Existing call sites log free-form strings; route them to Info/Mapping.
    var DLog = debugLog ?? ((string msg) => log.Info(LogCategory.Mapping, msg));

    var counters = new RuntimeCounters();
    var frameBuffer = new FrameRingBuffer();

    // Dumps the frames leading up to an event, which is why per-frame logging can stay off.
    void DumpFrameContext(string reason)
    {
        var frames = frameBuffer.Drain();
        if (frames.Count == 0)
        {
            return;
        }

        log.WriteBlock(
            LogLevel.Info,
            LogCategory.Frame,
            $"last {frames.Count} frames before: {reason}",
            frames);
    }

    log.Info(LogCategory.Session, "=== SenSÉ session start ===");
    log.WriteBlock(LogLevel.Info, LogCategory.Session, "identity", SessionReport.Identity(args));
    log.Info(LogCategory.Session, $"log file       : {logPath} (level={log.Level}, categories={log.Categories})");

    if (args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
    {
        log.Info(LogCategory.Session, "Stopping other instances...");
        StopOtherInstances(waitForExit: true);
    }

    // Only one bridge may hold the controller, and the guarantee belongs here rather than in the
    // launchers. Two of them start the core — the environment at startup, the configuration window
    // when a controller appears — and each passes --restart, so each killed the other and the
    // survivor was restarted by whoever noticed the death first. The result was several instances
    // fighting over one HID device, and a diagnostics screen polling a process that kept dying.
    //
    // Taken after StopOtherInstances so a deliberate --restart still wins: the instance being
    // replaced has already exited and released the mutex by this point.
    using var single = new Mutex(initiallyOwned: false, @"Global\SenSÉ.Core.Single", out _);
    var owned = false;
    try
    {
        owned = single.WaitOne(TimeSpan.FromSeconds(2));
    }
    catch (AbandonedMutexException)
    {
        // The previous holder was killed rather than closed. The mutex is ours now, and that is
        // exactly the situation --restart creates.
        owned = true;
    }

    if (!owned)
    {
        log.Info(LogCategory.Session, "Another SenSÉ.Core instance is already running. Exiting.");
        Console.WriteLine("SenSÉ.Core is already running.");
        return;
    }

    using var cancellation = new CancellationTokenSource();
    var enableModeSwitch = !args.Contains("--no-mode-switch", StringComparer.OrdinalIgnoreCase);
    var initialMode = ReadInitialOutputMode(args);
    var switchButtons = ReadModeSwitchButtons(args);
    // Kept as the bootstrap handler and reassigned per frame from the session of the controller in
    // hand. The chord is a sequence of presses by one pair of thumbs; detecting it across two
    // players' frames latches on inputs nobody made, and the mode flips on its own.
    var modeSwitcher = new InputModeHandler(
        initialMode,
        switchButtons,
        TimeSpan.FromMilliseconds(350));
    // Xbox360-mode layout now lives inside the desktop profile, so a single save in the
    // configuration window moves desktop mapping and gamepad layout together. These statics stay as
    // the answer for any controller without a profile of its own.
    var profileName = ReadOptionValue(args, "--profile");
    SenSÉProfileSettings? loadedSettings = null;

    if (!string.IsNullOrEmpty(profileName))
    {
        var profileResult = ProfileMapper.LoadDetailed(profileName);
        loadedSettings = profileResult.Settings;
        log.WriteBlock(LogLevel.Info, LogCategory.Session, "profile", SessionReport.Profile(profileName, profileResult));
    }

    var effectiveSettings = loadedSettings ?? SenSÉProfileSettings.Default;
    ControllerOutputMapper.DefaultButtonMap = XboxButtonMap.FromDictionary(effectiveSettings.XboxButtons);
    ControllerOutputMapper.DefaultTuning = effectiveSettings.XboxTuning;
    TritonHapticReportBuilder.TriggerActuatorIndex = effectiveSettings.XboxTuning.TriggerActuatorIndex;
    log.Info(LogCategory.Session, $"Xbox layout: from '{profileName ?? CoreCommandLine.DefaultProfile}'");

    log.WriteBlock(LogLevel.Info, LogCategory.Session, "effective settings", SessionReport.EffectiveSettings(effectiveSettings));
    log.WriteBlock(LogLevel.Info, LogCategory.Session, "overlay keyboard", SessionReport.OverlayKeyboard(OskSettings.Load()));

    // One session per controller: mapper, mode chord, sub-pixel carry and frame gap. Everything
    // that compares a frame to the previous one belongs here, because sharing any of it between two
    // pads compares them against each other rather than each against itself.
    // Which profile belongs to which family, as the configuration window recorded it. Read once
    // here rather than per session: the file is the same for everybody, only the lookup differs.
    var profileBook = LoadControllerProfiles(profileName ?? CoreCommandLine.DefaultProfile, log);

    // The family each attached controller belongs to, by its session key. Filled at attach time and
    // again on every frame, so a session built later still knows which family it is. Settings are
    // filed by family, not by controller — the same pad connects under a different Bluetooth address
    // from one day to the next, and the family is the one thing that never changes.
    var kindByIdentity = new Dictionary<string, ControllerKind>(StringComparer.Ordinal);

    /// <summary>The profile key a controller's settings are filed under, or "" while its kind is unknown.</summary>
    string FamilyIdFor(string controllerId)
        => kindByIdentity.TryGetValue(controllerId, out var kind)
            ? ControllerIdentityFactory.FamilyKey(kind)
            : "";

    /// <summary>The controller kind behind a session key, or Steam as the unknown's default.</summary>
    ControllerKind KindFor(string controllerId)
        => kindByIdentity.TryGetValue(controllerId, out var kind) ? kind : ControllerKind.SteamController;

    // Watched by timestamp. Assigning a family to a profile in
    // the configuration window otherwise took effect only after restarting the bridge — which, from
    // the user's side, is a setting that silently does nothing until they think to relaunch.
    var profileBookStamp = ControllerProfilesTimestamp();

    var sessions = new ControllerSessionSet(id =>
    {
        // Mappers come from the controller's family profile: the same pad under a new Bluetooth
        // address still finds its family's settings, which is the whole point of filing by family.
        var familyId = FamilyIdFor(id);
        var mapper = BuildMapperFor(id, familyId, profileBook, loadedSettings, KindFor(id), log);
        var gamepadMappers = BuildGamepadMappersFor(id, familyId, KindFor(id), profileBook, log);

        return new ControllerSession(
            mapper,
            new InputModeHandler(initialMode, switchButtons, TimeSpan.FromMilliseconds(350)),
            gamepadMappers.Xbox,
            gamepadMappers.DualSense)
        {
            Id = id,
            StickPointer = StickPointerFor(mapper),
        };
    });

    // The session of whichever controller sent the frame in hand. Reassigned once per frame, so
    // everything downstream keeps reading one set of names and now reads the right controller's.
    var session = sessions.For("bootstrap");
    var profileMapper = session.ProfileMapper;

    // Applying settings must not require a restart, so the profiles are watched and reloaded live.
    // Every profile, not only the launch one: each controller has its own file and each must be
    // noticed when it is saved.
    using var profileWatcher = new ProfileFileWatcher(message => log.Warn(LogCategory.Session, message));
    var padSender = new PadDataSender();

    // The single HID writer for haptics, declared before the keyboard instances because every
    // overlay's requests are routed into it.
    await using var haptics = new TritonHapticSink(
        new SteamHidDiscovery(),
        new TritonHapticReportBuilder(),
        message => log.Info(LogCategory.Haptics, message));

    // One keyboard per controller, opened on demand. The sender above still serves the default
    // instance, which is what the pre-warmed overlay connects to.
    //
    // Haptic requests come back on each keyboard's own pipe and land in the core's single HID
    // stream, so this sink stays the only writer for the device no matter how many keyboards ask.
    await using var oskInstances = new SenSÉ.App.Console.OskInstanceSet(
        m => log.Info(LogCategory.Osk, m),
        async (command, token) =>
        {
            counters.OverlayHapticRequest();

            if (log.IsEnabled(LogLevel.Debug, LogCategory.Haptics))
            {
                log.Debug(LogCategory.Haptics,
                    $"overlay request: {command.Actuator} {command.Type} pulse={command.PulseWidthUs}us gain={command.GainDb}");
            }

            try
            {
                await haptics.SubmitAsync(new HapticOutputFrame(new[] { command }), token)
                    .ConfigureAwait(false);
                counters.HapticSubmitted();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
            {
                counters.HapticDropped();
                log.Warn(LogCategory.Haptics, $"Overlay haptic dropped: {exception.Message}");
            }
        });

    /// <summary>Pre-warms the overlay keyboard for one controller, so its first toggle pays a file
    /// rather than the four seconds a .NET single-file host needs before its first line runs.</summary>
    /// <remarks>
    /// Called for every controller that is here at launch and again for each one that arrives
    /// mid-session, on its first frame. <see cref="SenSÉ.App.Console.OskPrewarmSet.Start"/>
    /// answers "already resident" for a keyboard that is already waiting, so a controller seen twice
    /// is pre-warmed once.
    /// </remarks>
    void PrewarmOsk(string controllerId, ControllerKind kind)
    {
        if (controllerId == "hid:pending")
        {
            return;
        }

        try
        {
            // A keyboard built for this family, exactly as a toggle would choose it: a Steam
            // Controller types on its trackpads and a PS5 or Xbox pad on its sticks.
            var overlayName = OverlayExecutableFor(kind);

            var overlayPath = Path.Combine(AppContext.BaseDirectory, overlayName);

            // The generic build is the fallback, so a machine that has not been given the
            // specialised pair still opens a keyboard.
            if (!File.Exists(overlayPath))
            {
                overlayPath = Path.Combine(AppContext.BaseDirectory, "SenSÉ.Osk.exe");
            }

            if (!File.Exists(overlayPath))
            {
                DLog($"OSK prewarm: no overlay executable found; {controllerId} will cold-start its keyboard.");
                return;
            }

            // Opening the instance makes its pipe listen before the overlay starts, which is
            // the order the overlay requires. The prewarm then costs nothing to find again.
            var instance = oskInstances.Open(controllerId);

            SenSÉ.App.Console.OskPrewarmSet.Start(
                overlayPath,
                instance.Naming,
                OskFloatingFor(FamilyIdFor(controllerId), profileBook, log),
                message => log.Info(LogCategory.Osk, message));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DLog($"OSK prewarm for {controllerId}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    padSender.Start();
    log.Info(LogCategory.Pipe, "PadData pipe server started (SenSÉ_OskPad).");
    var seconds = ReadSecondsOption(args);
    if (seconds is { } durationSeconds)
    {
        cancellation.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
    }

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    // And every other way of being asked to stop. CancelKeyPress covers Ctrl+C and nothing else;
    // the window's close button, a log-off and a shutdown all reached the end of this process
    // without any of the cleanup below ever running.
    SenSÉ.App.Console.GracefulShutdown.Arm(cancellation);

    DLog($"Initial mode: {initialMode}");
    DLog($"Switch buttons: {switchButtons}");
    DLog($"Mode switch enabled: {enableModeSwitch}");

    var mapper = loadedSettings is not null ? new ControllerOutputMapper(loadedSettings) : new ControllerOutputMapper();

    /// <summary>
    /// Rebuilds the controllers whose own profile was just saved, and nobody else's.
    /// </summary>
    /// <remarks>
    /// It used to rebuild every session on any change, which was wrong in both directions: saving
    /// the Steam Controller's profile threw away the DualSense's chord timers, trackball inertia and
    /// sub-pixel carry, while saving the DualSense's own profile did nothing at all — the watcher
    /// only looked at the file the bridge was launched with.
    ///
    /// <para>
    /// The overlay state survives the rebuild per session, inside
    /// <see cref="ControllerSessionSet.Reload"/> — each rebuilt controller keeps its own keyboard
    /// state. What this method does not do any more is capture it from the last frame's controller
    /// and pour it onto the bootstrap session, which crossed the controllers and ghosted keystrokes.
    /// </para>
    /// </remarks>
    void ReloadProfile(IReadOnlyCollection<string> changedProfiles)
    {
        var touched = sessions.Reload(
            id => changedProfiles.Contains(
                profileBook.ProfileFor(FamilyIdFor(id)),
                StringComparer.OrdinalIgnoreCase));

        log.Info(LogCategory.Session,
            $"Profiles changed [{string.Join(", ", changedProfiles)}]: {touched} controller(s) rebuilt.");

        // The launch profile also feeds process-wide state: the Xbox layout served to controllers
        // with no profile of their own, and the rumble translation. Only when that one changed.
        if (string.IsNullOrEmpty(profileName)
            || !changedProfiles.Contains(profileName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var reloaded = ProfileMapper.LoadDetailed(profileName);
        mapper = new ControllerOutputMapper(reloaded.Settings);

        ControllerOutputMapper.DefaultButtonMap = XboxButtonMap.FromDictionary(reloaded.Settings.XboxButtons);
        ControllerOutputMapper.DefaultTuning = reloaded.Settings.XboxTuning;
        TritonHapticReportBuilder.TriggerActuatorIndex = reloaded.Settings.XboxTuning.TriggerActuatorIndex;

        log.Info(LogCategory.Session, $"*** PROFILE RELOADED from {reloaded.FilePath} ***");
        log.WriteBlock(LogLevel.Info, LogCategory.Session, "effective settings (reloaded)",
            SessionReport.EffectiveSettings(reloaded.Settings));
        Console.WriteLine("Profile reloaded.");
    }

    // Before anything virtual exists. The pads SenSÉ creates are themselves XInput devices, so
    // taken afterwards this snapshot would include our own output — and reading that back is a
    // closed loop: every report emitted becomes the next report read.
    var physicalXInputSlots = SenSÉ.Windows.XInputControllerSource.ConnectedSlots();
    log.Info(
        LogCategory.Session,
        $"Physical XInput slots before any virtual pad: [{string.Join(", ", physicalXInputSlots)}].");

    // One virtual pad per physical controller, connected as soon as the controller is attached.
    // The always-connected single pad this replaces showed up to every game as a connected player
    // from the moment the Core started — a phantom occupying player one even with nothing attached
    // — which is the failure mode the per-controller design exists to prevent. The pad is now
    // connected per controller at attach time: the per-controller split stays, and the "nothing is
    // attached" case still connects nothing, because the pads are created only for controllers the
    // enumeration actually found.

    // Rumble reaches the physical controller that owns each virtual pad. The mapping from a game's
    // Xbox 360 feedback is per family (its own tuning), and the target is per controller: a Steam
    // Controller through the shared haptics sink, an Xbox pad on its own XInput slot, a DualSense
    // on its own HID stream. The DualSense writer lives here because the Core is deliberately free
    // of Windows; this process is where the platform meets the pads.
    await using var dualSenseRumbler = new DualSenseRumbler(DLog);

    /// <summary>The tuning in force for a controller's family, read at the moment it is needed.</summary>
    /// <remarks>
    /// Read from the live session rather than cached: ReloadProfile swaps sessions when a profile
    /// changes, and a rumble arriving in the same second should already feel the new tuning. Safe
    /// from the rumble threads because <see cref="ControllerSessionSet"/> guards its dictionary.
    /// </remarks>
    XboxTuning TuningFor(string controllerId) => sessions.For(controllerId).XboxMapper.Tuning;

    // Rumble is wired per pad rather than once on a single whole-bridge pad. Games address
    // feedback to the pad of the player being hit, and each pad is its own ViGEm device, so the
    // translation has to exist on every one of them. The factory receives the owning controller's
    // identity — the only thing that says which physical device a game's rumble belongs to.
    await using var virtualPads = new VirtualPadSet(
        identity =>
        {
            var sink = new ViGEmXbox360Sink();
            sink.RumbleReceived += (_, rumble) =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // The Steam Controller is the one Xbox-family pad with no XInput slot of its
                        // own, so it vibrates through the haptics report. A DualSense and an Xbox pad
                        // now also own an Xbox 360 pad — their switch emulates a gamepad — so their
                        // rumble goes back to the physical pad: the DualSense over the HID stream
                        // (it has no XInput slot), the Xbox pad straight to its own XInput slot,
                        // which SenSÉ still owns while cloaked. All three retune from the session
                        // at the moment of the rumble, so a profile change reaches the very next one.
                        if (identity.Kind == ControllerKind.SteamController)
                        {
                            var hapticMapper = new XboxRumbleToSteamHapticsMapper
                            {
                                Tuning = TuningFor(identity.Id),
                            };
                            await haptics.SubmitAsync(hapticMapper.Map(rumble), CancellationToken.None)
                                .ConfigureAwait(false);
                            return;
                        }

                        if (identity.Kind == ControllerKind.DualSense)
                        {
                            var tuning = TuningFor(identity.Id);
                            await dualSenseRumbler.RumbleAsync(
                                    identity.Id,
                                    tuning.ApplyVibration(rumble.LeftMotor),
                                    tuning.ApplyVibration(rumble.RightMotor),
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                            return;
                        }

                        var xboxTuning = TuningFor(identity.Id);
                        SenSÉ.Windows.XInputRumble.SetVibration(
                            identity.Slot,
                            xboxTuning.ApplyVibration(rumble.LeftMotor),
                            xboxTuning.ApplyVibration(rumble.RightMotor));
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
                    {
                        DLog($"Rumble disabled after error: {exception.Message}");
                    }
                });
            };
            return sink;
        },
        identity =>
        {
            var sink = new ViGEmDS4Sink();
            sink.RumbleReceived += (_, rumble) =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var tuning = TuningFor(identity.Id);
                        await dualSenseRumbler.RumbleAsync(
                                identity.Id,
                                tuning.ApplyVibration(rumble.LeftMotor),
                                tuning.ApplyVibration(rumble.RightMotor),
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
                    {
                        DLog($"DualSense rumble disabled after error: {exception.Message}");
                    }
                });
            };
            return sink;
        },
        message => log.Info(LogCategory.Mapping, message),

        // Windows records every device that has ever appeared and keeps the record for good. Each
        // virtual pad leaves three, two of which carry a fresh instance number every time, so they
        // pile up: twenty-nine had gathered by 12 August, and a crowd of indistinguishable XUSB
        // candidates is what makes slot-to-pad matching give up. Nothing is removed here — that
        // needs administrator, and this process deliberately has none. It only writes down what we
        // made, so the uninstaller removes exactly ours and nothing a customer owns.
        () => SenSÉ.Windows.VirtualPadLedger.RecordCreation(
            message => log.Info(LogCategory.Mapping, message)));

    // The overlay keyboard process asks for haptics over a pipe rather than opening its own
    // HID stream, so this sink stays the single writer for the device. Each keyboard's requests
    // arrive on its own pipe, created with the instance inside OskInstanceSet above.
    log.Info(LogCategory.Pipe, "Haptic request pipes are created with each keyboard instance.");

    // No pad here. The controllers attached at this point were given their pads when they were
    // opened; a game that starts now enumerates an XInput device that already exists, instead of
    // having one hot-plugged into it at the moment of the switch to Xbox.
    Console.WriteLine(enableModeSwitch
        ? $"Mode switch enabled. Current mode: {modeSwitcher.CurrentMode}."
        : $"Mode switch disabled. Current mode: {modeSwitcher.CurrentMode}.");
    Console.WriteLine($"Mode switch button(s): {switchButtons}");
    Console.WriteLine("Press Ctrl+C to stop.");

    var lastStatus = DateTimeOffset.UtcNow;

    // Menu + View held together for two seconds powers the controller off. Both are low-traffic
    // buttons, and the detector requires them down simultaneously and rearms only on a full release,
    // so a game that uses Start and Back cannot stumble into it.
    //
    // The detector and gate live on the controller's session, not here. Shared across controllers,
    // a second pad's idle frames reset the hold every few milliseconds and the chord can never
    // accumulate — the same bug as the mode chord, one session too late.
    const SteamControllerButtons PowerOffChord =
        SteamControllerButtons.Menu | SteamControllerButtons.View;
    var powerOffRequested = false;

    // Previous output mode, so the transition into and out of Xbox can be acted on rather than
    // the state merely observed. A pad created on entry has to be released on exit.
    var lastOutputMode = initialMode;

    // What the virtual pad actually received, drained into the per-second line. Counted here rather
    // than in the shared counters because they only mean anything in Xbox mode.
    var xboxButtonFrames = 0;
    var xboxStickFrames = 0;
    var xboxTriggerFrames = 0;
    var xboxSubmitFailures = 0;
    var xboxButtons = Xbox360Buttons.None;

    // Start the overlay resident and hidden now, while nobody is waiting on it, rather than paying
    // its four-second cold start on the first toggle.
    // See the preheat loop below, where the controllers that are here each get their own keyboard.

    // Ownership arbitration: Steam and SenSÉ cannot drive the controller at the same time.
    var steamWatcher = new SteamPresenceWatcher();
    // Opt-in. As an always-on default this fought the user: it re-evaluated the foreground every
    // 750 ms and forced a mode from it, so a manual Quick Access switch was undone as soon as focus
    // moved, and a game that briefly stopped covering its monitor was dropped out of Xbox360 mode
    // mid-session. Manual switching is the contract; automatic switching is an extra.
    var autoModeSwitch = args.Contains("--auto-mode", StringComparer.OrdinalIgnoreCase);
    var foregroundArbiter = autoModeSwitch && OperatingSystem.IsWindows()
        ? new ForegroundModeArbiter()
        : null;
    DLog($"Automatic foreground mode switching: {autoModeSwitch}");

    // Standing down for Steam concerns the Steam Controller and nothing else.
    //
    // Steam owns that pad natively: it drives it without us, so competing over it gives the user two
    // programs fighting for one device. It has no such claim on a DualSense or an Xbox pad — those
    // keep their profile and their native switch whether Steam is running or not, and handing them
    // over meant that launching Steam killed every controller on the machine.
    //
    // Asked of the hardware rather than remembered: the answer has to hold before any device is
    // opened, which is where this decision sits.
    bool SteamControllerPresent()
    {
        try
        {
            return new SenSÉ.Hid.SteamHidDiscovery().ListValveDevices().Count > 0;
        }
        catch (Exception failure)
        {
            // Unable to tell: keep the old behaviour rather than seize a pad Steam may be driving.
            DLog($"looking for a Steam Controller: {failure.GetType().Name}: {failure.Message}");
            return true;
        }
    }

    // If Steam is already up when we start, stand down before touching the device at all.
    steamWatcher.Poll(DateTimeOffset.UtcNow);
    if (steamWatcher.Owner == ControllerOwner.Steam && SteamControllerPresent())
    {
        DLog("Steam already running at startup; standing down.");
        Console.WriteLine("Steam is running: SenSÉ is standing by.");
        haptics.Muted = true;
    }

    while (!cancellation.Token.IsCancellationRequested)
    {
        // While Steam owns the controller SenSÉ holds nothing open: no HID stream, no virtual
        // pad, no haptics. Wait here until Steam goes away.
        //
        // Only when there is a Steam Controller to hand over. A session of DualSense and Xbox pads
        // has nothing Steam owns, and waiting here would idle them for as long as Steam ran.
        if (steamWatcher.Owner == ControllerOwner.Steam && SteamControllerPresent())
        {
            profileMapper.OskActive = false;
            profileMapper.DaisywheelActive = false;
            profileMapper.Reset();
            mapper.ResetTransientState();

            if (!await WaitWhileSteamOwnsAsync(
                    steamWatcher, virtualPads, haptics, oskInstances, DLog, log, cancellation.Token))
            {
                break;
            }

            DLog("Steam gone; reclaiming the controller.");
            Console.WriteLine("Steam closed: SenSÉ is taking over.");
        }

        // Read once per reconnect attempt, not per frame: the forced source is fixed at launch.
        var forced = ReadForcedSource(args);

        IPhysicalControllerSource? source = null;
        try
        {
            DLog("Opening HID device...");
            LogHidEnumeration(DLog);

            // Asked, not attempted. The Steam source's constructor succeeds whether or not a
            // controller is there — the failure only surfaces later, inside ReadFramesAsync — so
            // wrapping the constructor in a try caught nothing, the fallback never ran, and the
            // Core sat in a reconnection loop with an Xbox pad connected and ignored.
            // --source steam|xinput overrides the choice. "Steam first" is the right default, but it
            // means an Xbox pad can never be used while a Steam Controller is merely *enumerated* —
            // including one that is present and answering nothing, which is exactly the case this
            // flag exists to get out of. Until the Core can drive several controllers at once, the
            // user needs a way to say which one.
            var steamPresent = forced switch
            {
                "steam" => true,
                "xinput" => false,
                _ => new SteamHidDiscovery(DLog).FindPreferredControllerDevice() is not null,
            };

            if (forced.Length > 0)
            {
                log.Info(LogCategory.Session, $"Input source forced by --source {forced}.");
            }

            // Every attached controller, not one chosen from among them. Choosing is what made
            // split-screen impossible and what silently bound the whole bridge to a third-party
            // virtual gamepad that answers XInput with zeros.
            // Before anything is opened: a session killed earlier may have left a controller hidden
            // from every game on the machine, and this is where that gets repaired.
            SenSÉ.App.Console.ControllerCloak.Reset(log);

            var attached = SenSÉ.App.Console.AttachedControllers.Open(physicalXInputSlots, forced, DLog);

            SenSÉ.App.Console.ControllerCloak.Apply(log);

            // Le point d'ecoute du serveur MCP. En lecture seule : il repond ce que le Core tient
            // deja, et ne peut rien lui faire faire — le pont appelle cette fonction et rien d'autre.
            //
            // Demarre ici, apres l'ouverture des manettes, pour que « manettes » ait quelque chose a
            // dire des la premiere question. Sur son propre fil, en arriere-plan : une question du
            // modele ne doit jamais retarder une trame.
            SenSÉ.App.Console.McpBridge.Start(
                question => question switch
                {
                    "manettes" => string.Join(
                        " | ",
                        attached.Select(c => $"{c.Identity.Kind} {c.Identity.Id} slot={c.Identity.Slot}")),
                    "ping" => "SenSÉ.Core",
                    _ => $"question inconnue: {question}",
                },
                message => log.Info(LogCategory.Session, message));

            // Every attached controller gets its virtual pad now, whatever mode we are in — not on
            // the first frame it sends in Xbox mode. A pad created at the moment of the switch is a
            // hot-plug into whatever game is already running, and games that enumerate controllers
            // at launch never see a device that appears mid-session: the switch to Xbox read as
            // "the controller stopped responding". The pad now exists before the game starts and is
            // simply fed neutral reports in Profile mode. In Xbox mode it is fed the mapped gamepad.
            //
            // Families first, before anything below can cause a session to be built. A mapper is
            // built from its family's profile, and a family that is not yet known resolves to the
            // empty string — which matches no assignment, so the controller silently inherits
            // whichever profile the bridge was launched with. That is one family's tuning applied to
            // a pad whose own profile exists and was never opened.
            //
            // Found 14 August: controller-profiles.json held fam:ps5 and fam:xbox, and the log still
            // said "has no profile of its own" for both pads at startup. The registration further
            // down came before the pre-warm but after this, and this is where the first sessions
            // appear. Recording a kind twice costs nothing; recording it late costs the user their
            // settings.
            foreach (var controller in attached)
            {
                kindByIdentity[controller.Identity.Id] = controller.Identity.Kind;
            }

            // Before the pending fallback below: the "waiting for a controller" placeholder must not
            // claim a slot, and the fallback list is exactly the case where nothing real is attached.
            foreach (var controller in attached)
            {
                // Rien pour une manette qui demarre au bureau. Un pad se cree au moment ou sa
                // manette passe en mode natif, pas au branchement : sinon toute manette allumee est
                // un joueur connecte pour le jeu, meme celle dont le proprietaire est en train de
                // se servir comme d'une souris. Le jeu compte les manettes connectees, pas celles
                // qui envoient quelque chose.
                //
                // La branche mode Xbox demande son pad a chaque trame et le cree s'il manque, donc
                // rien a rebrancher a la main.
                if (initialMode != ControllerOutputMode.Xbox360)
                {
                    continue;
                }

                // Une manette demarree en mode natif emule son pad Xbox 360 virtuel, comme a la
                // bascule. Chaque famille passe par le meme pad : un DualSense et une Xbox aussi,
                // depuis que leur bascule emule un slot plutot que de rendre la manette physique.
                try
                {
                    await virtualPads.ForAsync(controller.Identity, cancellation.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    DLog($"connecting a virtual pad for {controller.Identity.Id}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            if (attached.Count == 0)
            {
                // Nothing is attached. The Steam source is still opened, because it is the one that
                // knows how to wait for a controller to appear and reconnect to it.
                attached =
                [
                    (new ControllerIdentity(ControllerKind.SteamController, "hid:pending", "Steam Controller", -1),
                        new TritonSteamControllerSource(
                            new SteamHidDiscovery(DLog),
                            new TritonInputReportParser(),
                            readTimeoutMs: 20,
                            manageNativeLayer: true,
                            initialNativeLayerEnabled: false,
                            log: DLog)),
                ];

                log.Info(LogCategory.Session, "No controller found; waiting for a Steam Controller.");
            }

            // The overlay keyboards for the controllers that are here, started resident and hidden
            // now rather than cold on the first toggle — the four seconds the .NET host needs before
            // the first line of that program runs are exactly what the first press used to pay.
            // One keyboard per controller, each on its own channels: the instance opened below binds
            // the pipe the overlay connects to, and the overlay is then asked to wait. Nothing for
            // the "waiting for a controller" placeholder, which has no pad to type on.
            // Each controller's family is recorded before the pre-warm below, so the keyboard's
            // floating choice already sees the family's own profile.
            foreach (var identity in attached.Select(controller => controller.Identity))
            {
                kindByIdentity[identity.Id] = identity.Kind;
            }

            foreach (var controller in attached)
            {
                PrewarmOsk(controller.Identity.Id, controller.Identity.Kind);
            }

            // The rescan closure re-enumerates exactly as the first call did, so a pad switched on
            // later is opened the same way as one present at launch. The XInput snapshot stays the
            // one taken before any virtual pad existed: a slot that fills afterwards is ours.
            // Released at the end of every turn of this loop. It was not, and the loop turns every
            // time the last controller is switched off and comes back — so each nap left a source
            // behind whose arrivals watcher went on rescanning for the rest of the session, opening
            // devices and announcing controllers next to its own replacement. Two naps, three
            // watchers, and the same pad reported as arriving three times.
            await using var multi = new SenSÉ.Windows.ParallelControllerSource(
                attached,
                DLog,
                onLeft: identity =>
                {
                    _ = virtualPads.ForgetAsync(identity.Id);

                    // The session and the keyboard are that controller's memory: its chords, its
                    // haptics, its pipes. Released here, at the moment it goes away, so a pad that
                    // returns starts clean and the sets do not grow for the whole session.
                    sessions.Forget(identity.Id);
                    _ = oskInstances.CloseAsync(identity.Id);

                    // And its device goes back to everyone else at the same moment. The reason it
                    // was hidden — that its buttons would reach the foreground as well as us — ends
                    // when the controller does, not when SenSÉ does.
                    SenSÉ.App.Console.ControllerCloak.ReleaseOne(identity.Id, log);
                },
                rescan: () =>
                {
                    var found = SenSÉ.App.Console.AttachedControllers.Open(
                        physicalXInputSlots, forced, DLog);

                    // A pad plugged in mid-session has to be hidden too, or it is the one controller
                    // whose buttons reach the foreground.
                    SenSÉ.App.Console.ControllerCloak.Apply(log);

                    return found;
                });
            source = attached[0].Source;

            log.WriteBlock(
                LogLevel.Info,
                LogCategory.Session,
                $"Reading {attached.Count} controller(s) in parallel:",
                attached.Select(c => $"{c.Identity.DisplayName} [{c.Identity.Id}]"));

            log.Info(LogCategory.Session,
                "Pointer driven by every controller: pads as the profile binds them, sticks for the same two roles.");

            await foreach (var frame in multi.ReadAllAsync(cancellation.Token).WithCancellation(cancellation.Token))
            {
                // Local copy: the power-off chord strips its own buttons before the mappers see the
                // frame, and a foreach variable cannot be reassigned.
                var state = frame.State;
                var frameSource = frame.Source.Id;

                // The family, remembered before the session below is built so its profile lookup
                // finds it. A controller that arrived mid-session (found by a rescan) is not known
                // at attach time, so this is recorded again on every frame rather than once.
                kindByIdentity[frameSource] = frame.Source.Kind;

                // Before anything reads it. Everything below this line works on the session of the
                // controller that sent this frame, never on another player's memory — of what was
                // pressed, of where the chord was, or of when its last frame arrived.
                session = sessions.For(frameSource);
                profileMapper = session.ProfileMapper;
                modeSwitcher = session.ModeSwitcher;

                // Une manette arrivee en cours de session, a sa premiere trame. Comme au demarrage,
                // elle n'obtient un pad que si elle est en mode natif : "quel que soit le mode" est
                // ce qui faisait de chaque manette allumee un joueur connecte.
                if (modeSwitcher.CurrentMode == ControllerOutputMode.Xbox360
                    && !virtualPads.Has(frameSource))
                {
                    try
                    {
                        // Same rule as at attach: one virtual Xbox 360 pad per controller, for every
                        // family — DualSense and Xbox included, since their switches emulate a
                        // normal gamepad rather than lending the physical pad to the system.
                        await virtualPads.ForAsync(frame.Source, cancellation.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        DLog($"connecting a virtual pad for {frameSource}: {exception.GetType().Name}: {exception.Message}");
                    }

                    // A controller that arrived mid-session is treated like one present at launch:
                    // its keyboard is pre-warmed the moment its first frame is read, so the first
                    // toggle pays a file rather than the four seconds of a cold start.
                    PrewarmOsk(frameSource, frame.Source.Kind);
                }

                // The keyboard's claim on this pad, weighed against whether the keyboard is actually
                // there. The flag below is the core's intent and was being read as the truth; once
                // the overlay became resident it could hide without saying so, and the pointer path
                // stayed suspended for the rest of the session. The overlay now beats a file while
                // it is on screen, and a claim nobody backs up lapses.
                var checkedAt = DateTimeOffset.UtcNow;

                if (profileMapper.ReconcileOverlay(
                        SenSÉ.App.Console.OskPresence.IsShowing(OskInstanceNaming.For(frameSource), checkedAt),
                        checkedAt))
                {
                    log.Info(LogCategory.Osk,
                        $"profile given back to {Shorten(frameSource)}: the keyboard claimed the pad and is not on screen.");
                }

                // This controller's own typing state, mirrored onto nobody: each controller now has
                // its own keyboard with its own channels, so one player opening one must not put
                // every other pad into typing mode.
                var oskActive = profileMapper.OskActive;
                mapper = session.XboxMapper;

                // Pads are no longer tied to Xbox mode. Each controller's pad connects when the
                // controller is attached and is released only when it leaves, so switching modes
                // mid-game never unplugs and replugs a device a running game may have enumerated at
                // launch. In Profile mode the pads are fed neutral reports below.
                //
                // The disposal this replaces existed for an older failure: pads were created on the
                // first Xbox frame and nothing ever took them away, so each switch to Xbox added one
                // more pad that games kept seeing for the rest of the session — a phantom player
                // accumulating on every toggle. Creating a pad once at attach closes both ends of
                // that: there is nothing to accumulate, and nothing to tear down on the way out.
                //
                // When nobody is in Xbox mode any more — not when this frame's controller is not.
                // The mode is still tracked per frame because the per-second line reports it; it
                // just no longer decides the life of the virtual pads.
                var anyInXbox = sessions.All.Any(s => s.ModeSwitcher.CurrentMode == ControllerOutputMode.Xbox360);

                lastOutputMode = anyInXbox ? ControllerOutputMode.Xbox360 : ControllerOutputMode.Profile;

                // Always cheap: the ring buffer keeps context in memory and is only written to the log
                // when an event asks for it. The unconditional per-frame write this replaces produced
                // megabytes per minute and buried everything else.
                frameBuffer.Add(state);

                var rightUnderFinger = state.RightPad.IsTouched || state.RightPad.IsPressed;
                var leftUnderFinger = state.LeftPad.IsTouched || state.LeftPad.IsPressed;

                // Named per source: one controller should be one source, and the counter line calls
                // it out when it is not. A pad read twice over has its button edges computed across
                // two interleaved streams, which fires them dozens of times a second.
                counters.Frame(rightUnderFinger, leftUnderFinger, Shorten(frameSource));

                // Where the fingers are, so the distance they cover can be told apart from the time
                // they spend resting. "Touched" alone cannot: a thumb laid still on the pad is
                // touched on every frame and must move nothing, and reading that as a fault reported
                // twenty-one imaginary defects in one session on 12 August.
                counters.PadAt($"{Shorten(frameSource)}/r", rightUnderFinger, state.RightPad.X, state.RightPad.Y);
                counters.PadAt($"{Shorten(frameSource)}/l", leftUnderFinger, state.LeftPad.X, state.LeftPad.Y);

                // A finger on the pad while the overlay owns it. The pointer path is skipped by
                // design here, and from outside that is indistinguishable from a pointer that broke
                // — the two need opposite fixes, so the reason is recorded rather than inferred.
                if (oskActive && (rightUnderFinger || leftUnderFinger))
                {
                    counters.PadIgnored("OSK actif");
                }

                if (log.IsEnabled(LogLevel.Trace, LogCategory.Frame))
                {
                    log.Trace(LogCategory.Frame,
                        $"btn={state.Buttons} ls=({state.LeftStick.X:F3},{state.LeftStick.Y:F3}) rs=({state.RightStick.X:F3},{state.RightStick.Y:F3}) lt={state.LeftTrigger:F3} rt={state.RightTrigger:F3} lp=({state.LeftPad.X:F3},{state.LeftPad.Y:F3} t={state.LeftPad.IsTouched} c={state.LeftPad.IsPressed}) rp=({state.RightPad.X:F3},{state.RightPad.Y:F3} t={state.RightPad.IsTouched} c={state.RightPad.IsPressed})");
                }

                // The detector reads the frame as it arrived; the mappers read it through the gate.
                // Masking the chord only once both buttons were down was too late — two fingers never
                // land on the same frame, so the first button had already fired its own action.
                var frameTime = DateTimeOffset.UtcNow;

                // Menu + View held for two seconds is the power-off (and for a DualSense over
                // Bluetooth, the link drop that resets it). It is a Profile-mode tool: in Xbox mode
                // — which for a PS5 or an Xbox is the native mode — Menu and View are the game's
                // own buttons, so the chord must not fire and the gate must not withhold them. The
                // native pad owns them there; SenSÉ only comes back to them on the return to
                // Profile.
                bool powerOffRequestedHere = false;
                if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile)
                {
                    var chordComplete = session.PowerOffChordDetector.Update(state.Buttons, frameTime);
                    var chordEngaged = chordComplete || (state.Buttons & PowerOffChord) == PowerOffChord;

                    state = state with { Buttons = session.PowerOffChordGate.Filter(state.Buttons, frameTime, chordEngaged) };
                    powerOffRequestedHere = chordComplete;
                }
                else
                {
                    session.PowerOffChordDetector.Reset();
                    session.PowerOffChordGate.Reset();
                }

                if (powerOffRequestedHere)
                {
                    DumpFrameContext("power-off chord held");
                    log.Info(LogCategory.Session, "*** Power-off chord (Menu + View, 2s) ***");

                    // The chord and power-off request belong to this physical controller only.
                    await virtualPads.NeutralizeForAsync(frameSource, cancellation.Token);

                    bool poweredOff;
                    try
                    {
                        // The pad whose chord fired, not "the first attached controller": with
                        // several players each has their own pad, and only the one that asked
                        // should go to sleep. The parallel source finds the matching child, since
                        // this loop no longer holds a source per controller itself.
                        poweredOff = multi.TrySendPowerOff(frame.Source);
                    }
                    catch (Exception exception)
                    {
                        poweredOff = false;
                        log.Warn(LogCategory.Session,
                            $"Power-off report rejected by the device: {exception.GetType().Name}: {exception.Message}");
                    }

                    if (!poweredOff)
                    {
                        // The chord resolved but the controller is still on — a DualSense over
                        // Bluetooth, where Windows exposes no working power-off command, or a pad
                        // that refused. Standing by as if it had gone to sleep would tear the
                        // session down (pads released, streams reopened) for a pad that never left.
                        log.Info(LogCategory.Session,
                            frame.Source.Kind == ControllerKind.DualSense
                                ? "DualSense cannot be powered off from Windows over Bluetooth; hold the PS button for about ten seconds instead."
                                : "Controller stayed on; continuing without standing by.");
                        Console.WriteLine("Controller not powered off. (For a Bluetooth DualSense: hold the PS button for about ten seconds.)");
                        continue;
                    }

                    log.Info(LogCategory.Session, "Power-off request sent to the controller.");
                    Console.WriteLine("Powering the controller off.");

                    // Give the firmware a moment to act before the process tears the stream down.
                    await Task.Delay(400, CancellationToken.None);

                    // Stand by rather than exit. Switching the controller off is not "I am done with
                    // SenSÉ": the user expects it to be picked up again when it comes back on,
                    // and a process that has exited cannot do that.
                    powerOffRequested = true;
                    break;
                }

                if (enableModeSwitch && modeSwitcher.Update(state, frame.Source.Kind))
                {
                    // An explicit toggle beats automatic switching for the app in front.
                    foregroundArbiter?.SuspendForForegroundApp();
                    DumpFrameContext($"manual mode switch -> {modeSwitcher.CurrentMode}");
                    // Named, because the mode belongs to one controller and not to the session. Every
                    // pad has its own ModeSwitcher, so one going to Xbox leaves the others on their
                    // profile — and a line that says only "MODE SWITCH -> Xbox360" reads, in a log
                    // shared by four controllers, as though the whole session had moved. On 12 August
                    // that is exactly how it was read: ordinary keyboard toggles from a pad still on
                    // its profile were taken for proof that shortcuts were firing during a game, and
                    // a defect was reported that does not exist.
                    log.Info(LogCategory.Mode,
                        $"*** MODE SWITCH -> {modeSwitcher.CurrentMode} (manual) for {Shorten(frameSource)} ***");
                    mapper.ResetTransientState();
                    profileMapper.Reset();

                    // Modes are per controller. Neutralising every pad here interrupts players that
                    // are still in Xbox mode.
                    await virtualPads.NeutralizeForAsync(frameSource, cancellation.Token);

                    // Puis debrancher, si cette manette repasse au bureau. Neutraliser ne suffit
                    // pas : un pad neutre reste un pad CONNECTE, et un jeu compte les manettes
                    // connectees, pas celles qui bougent. Trois manettes branchees dont deux sur le
                    // bureau donnaient trois joueurs, et celle qu'on tient pilote le joueur 2 ou 3.
                    // C'est le "toutes les manettes se melangent" du 15 aout.
                    //
                    // Le rebranchement est automatique : la branche mode Xbox demande son pad a
                    // chaque trame et le cree s'il manque. Une manette qui revient au jeu le
                    // retrouve a la trame suivante.
                    if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile)
                    {
                        await virtualPads.ForgetAsync(frameSource);
                    }

                    Console.WriteLine($"Mode switched to {modeSwitcher.CurrentMode}.");
                }

                if (modeSwitcher.SteamLaunchRequested)
                {
                    DLog("*** Steam launch requested ***");
                    InputHelper.LaunchSteam();

                    // Le retrait devant Steam n'appartient qu'a la manette Steam. Elle seule a une
                    // couche firmware que Steam reprend, donc elle seule doit cesser d'ecrire.
                    //
                    // Le "break" ci-dessous casse la boucle de TOUTES les manettes : la source HID
                    // est fermee, rouverte, chaque manette part et revient, et chaque session est
                    // reconstruite avec le mode de depart. Autrement dit un appui sur le bouton PS
                    // ou Xbox d'une seule manette remettait toutes les autres en mode profil, sans
                    // qu'aucune bascule n'apparaisse dans le journal.
                    //
                    // Mesure le 15 aout a 00:30:10 : bouton Xbox presse, "Steam launch requested" a
                    // .986, "Main loop ended" a 00:30:11.093, les deux manettes parties et revenues
                    // a .100, profils recharges a .287. La manette passee en natif 18 secondes plus
                    // tot etait revenue en profil sans que rien ne le dise, et l'appui suivant
                    // produisait un deuxieme "MODE SWITCH -> Xbox360" pour la meme manette.
                    //
                    // Demande le 13 aout : les manettes PS5 et Xbox "gardent leur switch profil a
                    // natif et ignore steam software". Lancer Steam, oui. Se retirer, non. Seule la
                    // manette Steam se retire ; verifier l'espece.
                    if (frame.Source.Kind == ControllerKind.SteamController)
                    {
                        // Hand over before Steam is observable: the process takes seconds to appear
                        // and SenSÉ must not still be writing to the device meanwhile.
                        steamWatcher.HandOverToSteam(DateTimeOffset.UtcNow);
                        Console.WriteLine("Launching Steam, controller handed over.");
                        break;
                    }

                    Console.WriteLine("Launching Steam.");
                }


                if (profileWatcher.TryConsumeChange(out var changedProfiles))
                {
                    ReloadProfile(changedProfiles);
                }

                // Steam may also have been started from outside SenSÉ entirely.
                if (steamWatcher.Poll(DateTimeOffset.UtcNow) && steamWatcher.Owner == ControllerOwner.Steam)
                {
                    DumpFrameContext("Steam detected, handing over");
                    log.Info(LogCategory.Owner, "*** Steam detected; handing the controller over ***");
                    Console.WriteLine("Steam detected: handing the controller over.");
                    break;
                }

                // Desktop versus game, the way Steam swaps its desktop and per-game configs.
                if (foregroundArbiter?.Poll() is { } suggestedMode && suggestedMode != modeSwitcher.CurrentMode)
                {
                    DumpFrameContext($"auto mode switch -> {suggestedMode}");
                    log.Info(LogCategory.Mode,
                        $"*** AUTO MODE -> {suggestedMode} for {Shorten(frameSource)} "
                        + $"(foreground={foregroundArbiter.LastForegroundProcess}) ***");
                    modeSwitcher.SetMode(suggestedMode);
                    mapper.ResetTransientState();
                    profileMapper.Reset();

                    // The foreground decision changes this controller's session, not every pad.
                    await virtualPads.NeutralizeForAsync(frameSource, cancellation.Token);

                    // Et debrancher au retour au bureau, comme pour la bascule manuelle : un pad
                    // neutre reste un pad connecte, donc un joueur de plus pour le jeu.
                    if (suggestedMode == ControllerOutputMode.Profile)
                    {
                        await virtualPads.ForgetAsync(frameSource);
                    }

                    Console.WriteLine($"Mode switched to {suggestedMode} ({foregroundArbiter.LastForegroundProcess}).");
                }

                // Steam ouvert = manette en natif, tant que Steam tourne. C'est la regle demandee :
                // "le switch reste sur xbox360 jusqu'a la fermeture de steam software". Quand Steam
                // part, chaque manette revient au mode ou elle etait avant — pas a un defaut, sinon
                // une manette deja en natif serait jettee au bureau a la fermeture de Steam.
                //
                // Place apres le chord manuel et l'arbitre de premier plan, pour que "Steam ouvert"
                // soit le dernier mot sur le mode a chaque trame : quoi qu'il l'ait bouge, Steam
                // ouvert le ramene en natif, et le mode d'avant revient quand Steam ferme. Le pad,
                // lui, n'a rien a faire ici : la branche mode Xbox demande le sien a chaque trame
                // et le cree s'il manque, comme apres une bascule manuelle.
                if (steamWatcher.Owner == ControllerOwner.Steam)
                {
                    if (session.ModeBeforeSteam is null)
                    {
                        session.ModeBeforeSteam = modeSwitcher.CurrentMode;
                        log.Info(LogCategory.Mode,
                            $"*** STEAM OPEN -> {Shorten(frameSource)} pinned to Xbox 360 (was {modeSwitcher.CurrentMode}) ***");
                    }

                    if (modeSwitcher.CurrentMode != ControllerOutputMode.Xbox360)
                    {
                        modeSwitcher.SetMode(ControllerOutputMode.Xbox360);
                        mapper.ResetTransientState();
                        profileMapper.Reset();
                    }
                }
                else if (session.ModeBeforeSteam is { } previousMode)
                {
                    session.ModeBeforeSteam = null;

                    if (modeSwitcher.CurrentMode != previousMode)
                    {
                        modeSwitcher.SetMode(previousMode);
                        mapper.ResetTransientState();
                        profileMapper.Reset();
                    }

                    // Steam ferme : repasser au bureau debranche le pad, comme la bascule manuelle
                    // et l'arbitre de premier plan. Un pad reste connecte pendant que personne ne
                    // joue est un joueur de plus pour le jeu.
                    if (previousMode == ControllerOutputMode.Profile)
                    {
                        await virtualPads.ForgetAsync(frameSource);
                    }

                    log.Info(LogCategory.Mode,
                        $"*** STEAM CLOSED -> {Shorten(frameSource)} back to {previousMode} ***");
                }

                if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile)
                {
                    var mappedState = modeSwitcher.ConsumeButton(state);
                    // While this controller owns the keyboard, its sticks belong to the keyboard and
                    // to nothing else. One input cannot serve two purposes: the profile here binds
                    // the left stick to the arrow keys, so without this it would type arrows *and*
                    // aim the left anchor at the same time — which is what made the overlay
                    // unusable however well the anchors resolved.
                    //
                    // The profile mapper suspends its own shortcuts while OskActive — B, A and Menu
                    // (in the daisywheel) are the way out and stay live — so the buttons can still
                    // reach it without a shortcut escaping under the overlay. Only the sticks need
                    // to be zeroed here, and only for the owner: another controller feeding the
                    // keyboard keeps its own sticks, but the desktop pointer is suspended for
                    // everyone below.
                    var forProfile = oskActive
                        ? mappedState with
                        {
                            LeftStick = new NormalizedStick(0, 0),
                            RightStick = new NormalizedStick(0, 0),
                        }
                        : mappedState;

                    profileMapper.Map(forProfile);

                    // Every controller drives the pointer with both its hands at once. The pads move
                    // it as the profile binds them — right pad the cursor, left pad the wheel — and
                    // the sticks cover the same two roles for controllers that have no pads, while
                    // still working beside the pads on a Steam Controller. The mapper accumulates the
                    // pads' motion and wheel without sending it; the sticks are mapped here. Both are
                    // offered to the one shared pointer as a single contender per controller, so a
                    // player's own stick and pad agree while two controllers pushing at once stall
                    // the pointer, which is the agreed arbitration.
                    {
                        // This controller's own previous frame. Shared, the gap measured was the
                        // interval since somebody else's frame — microseconds instead of
                        // milliseconds — so the stick moved the cursor a fraction of what it should
                        // or the guard rejected the frame outright.
                        //
                        // Kept running while the keyboard is open so the gap does not stretch across
                        // the whole session: coming back would otherwise measure from before the
                        // overlay and lunge the cursor.
                        var elapsed = state.Timestamp - session.LastFrame;
                        session.LastFrame = state.Timestamp;

                        // A source that restarts counts from zero again, so the next frame lands
                        // before the one this session last saw and the gap comes out negative — the
                        // log showed -67 seconds. Every rule downstream treats a non-positive gap as
                        // a stall and produces nothing, so the pointer freezes until the clock has
                        // climbed back past where it was. One frame is the honest reading here: the
                        // stream is new, not late.
                        if (elapsed < TimeSpan.Zero)
                        {
                            log.Info(LogCategory.Mapping,
                                $"{frameSource}: frame clock went backwards by {-elapsed.TotalMilliseconds:F0} ms "
                                + "(source restarted); counting this frame as one interval.");

                            elapsed = TimeSpan.Zero;
                        }

                        if (!oskActive)
                        {
                            // Raw values on the per-second line. "The stick does not move the pointer"
                            // has three unrelated causes — the reader returning nothing, the frame gap
                            // guard rejecting the frame, or the dead zone swallowing it — and they need
                            // opposite fixes. Guessing between them has already cost enough.
                            session.PeakX = Math.Max(session.PeakX, Math.Abs(forProfile.RightStick.X));
                            session.PeakY = Math.Max(session.PeakY, Math.Abs(forProfile.RightStick.Y));
                            session.LastGapMs = elapsed.TotalMilliseconds;

                            // forProfile rather than mappedState: the owner's sticks are zeroed while
                            // the keyboard is open, so a stick aiming at a key would otherwise move the
                            // desktop pointer at the same time. The PS5 profile stays live under the
                            // overlay otherwise — its sticks type and drag in the same gesture.
                            //
                            // The two modes are what make the comboboxes real: the right stick only
                            // drives the pointer while the profile says "Souris", and the left stick
                            // only scrolls while the profile says "Molette" — never both. Before
                            // this the stick roles were hardcoded here and the dropdowns in the
                            // Mouvements card changed nothing.
                            var stickOutput = StickPointerMapper.Map(
                                forProfile,
                                elapsed,
                                session.StickPointer,
                                rightStickPointer: profileMapper.Settings.RightStickMode == StickMotionMode.Pointer,
                                leftStickWheel: profileMapper.Settings.LeftStickMode == StickMotionMode.Wheel,
                                ref session.Carry);

                            // Applied straight away, by this controller, for this frame. No shared
                            // window, no arbitration, nothing belonging to another device in the
                            // path.
                            //
                            // There used to be a PointerArbiter here: contributions from every
                            // controller were collected over an 8 ms window and, if two of them had
                            // moved, both were dropped and the pointer froze. It was a deliberate
                            // rule for two players fighting over one pointer, and it made a single
                            // user's controller unusable — a trackpad still gliding on its inertia
                            // counts as a mover for seconds after the finger left, and every push of
                            // the DualSense stick landed in that window and was cancelled.
                            //
                            // Each physical device is an independent input. Whichever one moves,
                            // moves the pointer. None of them can block another.
                            //
                            // The rate limiting is the movement itself, not a clock: the sub-pixel
                            // carry inside the mappers only yields a whole pixel once one has been
                            // travelled, so a resting stick produces no call at all.
                            var px = stickOutput.PixelsX + profileMapper.EmittedPixelsX;
                            var py = stickOutput.PixelsY + profileMapper.EmittedPixelsY;
                            var wheel = stickOutput.WheelNotches + profileMapper.SignedWheelNotches;
                            var hwheel = profileMapper.HorizontalWheelNotches;

                            if (px != 0 || py != 0)
                            {
                                InputHelper.MouseMoveRelative(px, py);
                                counters.MouseMotion(px, py);
                            }

                            if (wheel != 0)
                            {
                                // InputHelper applies WHEEL_DELTA (120) itself; the mappers count detents.
                                InputHelper.MouseWheel(wheel);
                                counters.Wheel(Math.Abs(wheel));
                            }

                            if (hwheel != 0)
                            {
                                InputHelper.MouseHorizontalWheel(hwheel);
                            }
                        }
                        else
                        {
                            // The keyboard is open: this controller types, it does not point. The
                            // sticks are aiming at keys, so the per-second line must show zero — a
                            // pointer that is suspended, not a pointer stuck at its last deflection.
                            session.PeakX = 0;
                            session.PeakY = 0;
                            session.LastGapMs = 0;
                        }
                    }

                    if (profileMapper.PadClicked)
                        counters.PadClick();

                    // This controller's own typing state, mirrored onto nobody.
                    //
                    // It used to be copied onto every session, because the keyboard was one window
                    // and one set of channels: whoever opened it, everybody had to feed it. Now each
                    // controller has its own keyboard with its own pipes, so one player opening one
                    // must not put every other pad into typing mode — that would suspend their
                    // profiles and send their frames to a window they never asked for.

                    if (oskActive)
                    {
                        // This controller's frames feed this controller's keyboard. The sender is
                        // resolved per frame from the current session, so the overlay answers the
                        // controller that actually owns the window; the default pipe remains for an
                        // overlay without an instance. Idle frames are not dropped: a stick
                        // returning to centre is how a key is un-selected, and filtering them out is
                        // what silenced the pipe in an earlier build.
                        var sender = oskInstances.SenderFor(frameSource) ?? padSender;
                        sender.SendPadState(
                            state.RightPad, state.LeftPad, state.Buttons,
                            state.LeftStick, state.RightStick, state.LeftTrigger, state.RightTrigger,
                            frame.Source.Kind == ControllerKind.SteamController
                                ? OskControllerKind.Steam
                                : OskControllerKind.Sticks);
                    }

                    if (source is INativeLayerControl nativeLayer)
                        await nativeLayer.SetNativeLayerEnabledAsync(false);

                    // A Profile frame must keep only its own virtual pad neutral. Sending neutral to
                    // every pad here cuts input from controllers currently in Xbox mode.
                    await virtualPads.NeutralizeForAsync(frameSource, cancellation.Token);

                    if (profileMapper.OskToggleRequested)
                    {
                        DumpFrameContext($"OSK toggle (currently active={profileMapper.OskActive})");
                        log.Info(LogCategory.Osk, $"OSK toggle requested. OskActive={profileMapper.OskActive}");

                        // The keyboard belongs to the controller that asked for it, decided here at
                        // the press rather than on the next frame to arrive. Taking the first pad to
                        // speak afterwards hands the keyboard to whichever one happens to be lying
                        // on the table drifting, while the one in the user's hands types into
                        // nothing. The instance is opened below, keyed by this controller.
                        if (!profileMapper.OskActive)
                        {
                            bool steamActive = OperatingSystem.IsWindows() && InputHelper.IsSteamWindowActive();
                            DLog($"IsSteamWindowActive={steamActive}");

                            if (steamActive)
                            {
                                DLog("OSK toggle BLOCKED: Steam window is in foreground.");
                            }
                            else
                            {
                                var oskDir = AppContext.BaseDirectory;
                                // A keyboard built for this family. A Steam Controller types on its
                                // trackpads and a PS5 or Xbox pad on its sticks; the specialised builds
                                // keep one input path each instead of holding both live, where a
                                // resting thumb on one fights the other hand.
                                var overlayName = OverlayExecutableFor(frame.Source.Kind);

                                var overlayPath = Path.Combine(oskDir, overlayName);

                                // The generic build is the fallback, so a machine that has not been
                                // given the specialised pair still opens a keyboard.
                                if (!File.Exists(overlayPath))
                                {
                                    overlayPath = Path.Combine(oskDir, "SenSÉ.Osk.exe");
                                }
                                DLog($"OSK launch: dir={oskDir} exe={File.Exists(overlayPath)}");
                                if (File.Exists(overlayPath))
                                {
                                    try
                                    {
                                        // This controller's own keyboard, with its own channels. The
                                        // set opens the pipe before the window exists, so the
                                        // overlay finds a server waiting rather than retrying.
                                        var instance = oskInstances.Open(frameSource);

                                        // Already resident and waiting: showing it costs writing one
                                        // file. The overlay was started with --prewarm when this
                                        // controller was opened, and a close hides it rather than
                                        // ending it — so the four seconds the .NET host needs before
                                        // the first line of that program runs were paid once, at the
                                        // start of the session, instead of on every press.
                                        //
                                        // One resident overlay PER CONTROLLER, never one shared. A
                                        // single instance on the default channels existed once and
                                        // answered for every controller, swallowing the frames meant
                                        // for the window that had just been opened for one of them.
                                        // The suffix is what keeps them apart.
                                        if (SenSÉ.App.Console.OskPrewarmSet.Wake(instance.Naming, DLog))
                                        {
                                            profileMapper.OskActive = true;

                                            var wokenMode = OskSettings.Load().TypingMode;
                                            profileMapper.DaisywheelActive = wokenMode == OskTypingMode.Daisywheel;
                                            DLog($"OSK woken from prewarm; typing mode: {wokenMode}");
                                        }
                                        else
                                        {
                                            // Nothing resident for this controller — the prewarm
                                            // failed, or it has since died. Cold start, as before.
                                            var psi = new ProcessStartInfo
                                            {
                                                FileName = overlayPath,
                                                UseShellExecute = true,
                                            };

                                            psi.ArgumentList.Add("--instance");
                                            psi.ArgumentList.Add(instance.Naming.Suffix);

                                            // Pinned or floating, from this controller's own
                                            // profile. The overlay used to read one shared setting
                                            // file, so whichever preference was saved last applied
                                            // to every controller on the machine.
                                            psi.ArgumentList.Add("--floating");
                                            psi.ArgumentList.Add(
                                                profileMapper.Settings.OskFloating ? "1" : "0");

                                            // Started resident. Closing this keyboard now hides it
                                            // instead of ending it, so this cold start is the only
                                            // one this controller pays for the whole session — every
                                            // press after it is a signal file.
                                            //
                                            // Launched here rather than when the controller is
                                            // opened, and that is deliberate: the overlay expects its
                                            // pipe to already be listening, and the pipe is opened by
                                            // the line above. Pre-opening a pipe for every attached
                                            // controller would make the keyboard instant from the
                                            // first press too — at the cost of a server per
                                            // controller whether or not anybody types.
                                            psi.ArgumentList.Add("--prewarm");

                                            var proc = Process.Start(psi);
                                            DLog($"OSK overlay launched for {frameSource}: PID={proc?.Id}");

                                            // Bound to this session's life by the kernel. An overlay
                                            // keyboard left behind is the worst orphan this product
                                            // can make: its window puts itself above everything, and
                                            // once the session that opened it is gone, nothing on the
                                            // machine will close it.
                                            if (proc is not null)
                                            {
                                                SenSÉ.Windows.ChildProcesses.Adopt(proc, DLog);
                                                SenSÉ.App.Console.OskPrewarmSet.Remember(
                                                    instance.Naming, proc, DLog);

                                                // Started resident, so it needs the show signal the
                                                // resident path would have written — this press
                                                // opened the keyboard, and it has to appear now.
                                                // Missing this left a resident overlay with
                                                // OskActive=true and nothing on screen: the pad was
                                                // handed over, and the core then gave it back when
                                                // the beat never came.
                                                SenSÉ.App.Console.OskPrewarmSet.SignalShow(
                                                    instance.Naming, DLog);
                                            }
                                        }

                                        profileMapper.OskActive = true;

                                        // The overlay reads the same settings file; mirror the mode
                                        // here so ABXY stop running their desktop bindings.
                                        var oskMode = OskSettings.Load().TypingMode;
                                        profileMapper.DaisywheelActive = oskMode == OskTypingMode.Daisywheel;
                                        DLog($"OSK typing mode: {oskMode}");
                                    }
                                    catch (Exception ex)
                                    {
                                        DLog($"OSK overlay start FAILED: {ex.GetType().Name}: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    DLog($"OSK overlay exe NOT FOUND at: {overlayPath}");
                                }
                            }
                        }
                        else
                        {
                            DLog("OSK overlay: stopping...");
                            bool signaled = false;
                            if (OperatingSystem.IsWindows())
                            {
                                try
                                {
                                    // This controller's keyboard reads this controller's signal; the
                                    // generic file is also written for an overlay without an instance.
                                    var instance = oskInstances.InstanceFor(frameSource);
                                    var signalName = instance?.Naming.CloseSignalFile ?? "osk-close.signal";
                                    var closeSignalPath = CheminDebug.Signal(Path.GetFileNameWithoutExtension(signalName));
                                    File.WriteAllText(closeSignalPath, DateTime.UtcNow.Ticks.ToString());
                                    signaled = true;
                                    DLog($"OSK close signal file written: {closeSignalPath}");
                                }
                                catch (Exception ex)
                                {
                                    DLog($"OSK close signal file write failed ({ex.GetType().Name}): {ex.Message}");
                                }
                            }

                            if (!signaled)
                            {
                                KillOskProcesses();
                            }

                            profileMapper.OskActive = false;
                            profileMapper.DaisywheelActive = false;

                            // Insurance for the kill path, where the overlay gets no chance to release
                            // a latched SHIFT itself and would leave the physical keyboard uppercase.
                            InputHelper.KeyUp(0xA0);

                            // And the same for the mouse buttons and the volume keys. A latched SHIFT
                            // makes the keyboard shout; a latched mouse button makes the whole desktop
                            // stop answering, because every click becomes the middle of a drag.
                            profileMapper.ReleaseHeldInput();

                            log.Info(LogCategory.Osk, "OSK overlay stopped.");
                        }
                    }

                    var hapticNow = DateTimeOffset.UtcNow;
                    var cmds = new List<HapticCommand>();

                    // Force and rate come from the profile, per pad. They are read only here, after
                    // the motion frame has already been produced and sent, so tuning the feel of the
                    // feedback can never change the cursor or the scrolling itself.
                    var rightHaptics = profileMapper.Settings.RightPadHaptics;
                    var leftHaptics = profileMapper.Settings.LeftPadHaptics;

                    // Pointer motion feedback is quantised by distance travelled, not by elapsed time.
                    // Ticking every 30 ms while the cursor moved produced a continuous buzz; one tick
                    // per N pixels of travel gives a texture that scales with the gesture instead.
                    if (rightHaptics.Enabled)
                    {
                        session.HapticTravel += Math.Abs(profileMapper.EmittedPixelsX) + Math.Abs(profileMapper.EmittedPixelsY);
                        if (session.HapticTravel >= rightHaptics.TravelPerTickPixels)
                        {
                            session.HapticTravel %= rightHaptics.TravelPerTickPixels;
                            cmds.Add(new HapticCommand(
                                HapticActuator.RightTrackpad, HapticType.Tick, 0,
                                PulseWidthUs: rightHaptics.PulseWidthUs));
                        }

                        // Never throttled: a click that gets swallowed feels like a missed input.
                        if (profileMapper.PadClicked)
                            cmds.Add(new HapticCommand(
                                HapticActuator.RightTrackpad, HapticType.Click, 0,
                                PulseWidthUs: (ushort)Math.Min(ushort.MaxValue, rightHaptics.PulseWidthUs * 1.6)));
                    }

                    // One detent per scroll burst. A tick per notch would be hundreds per second at
                    // speed, so the rate is capped and the pulse widens with the notch count to keep
                    // a fast flick distinguishable from a single notch.
                    if (leftHaptics.Enabled &&
                        profileMapper.WheelNotches > 0 &&
                        (hapticNow - session.LastScrollTick).TotalMilliseconds >= leftHaptics.DetentIntervalMs)
                    {
                        var width = (ushort)Math.Clamp(
                            leftHaptics.PulseWidthUs + profileMapper.WheelNotches * 20,
                            leftHaptics.PulseWidthUs,
                            leftHaptics.PulseWidthUs * 2);
                        cmds.Add(new HapticCommand(
                            HapticActuator.LeftTrackpad, HapticType.Tick, 0, PulseWidthUs: width));
                        session.LastScrollTick = hapticNow;
                    }

                    if (cmds.Count > 0)
                    {
                        // Counting here as well as in the overlay path: instrumenting only one of the
                        // two made the log read "haptics sent=0" while commands were being submitted,
                        // which is worse than no counter at all.
                        try
                        {
                            await haptics.SubmitAsync(new HapticOutputFrame(cmds), CancellationToken.None);
                            counters.HapticSubmitted();

                            if (log.IsEnabled(LogLevel.Debug, LogCategory.Haptics))
                            {
                                log.Debug(LogCategory.Haptics,
                                    $"submitted {cmds.Count}: {string.Join(", ", cmds.Select(c => $"{c.Actuator}/{c.Type}/{c.PulseWidthUs}us"))} deviceOpen={haptics.IsDeviceOpen}");
                            }
                        }
                        catch (Exception exception)
                        {
                            counters.HapticDropped();
                            log.Warn(LogCategory.Haptics, $"haptic submit failed: {exception.GetType().Name}: {exception.Message}");
                        }
                    }
                }
                else
                {
                    var mappedState = enableModeSwitch ? modeSwitcher.ConsumeButton(state) : state;
                    if (source is INativeLayerControl nativeLayer)
                        await nativeLayer.SetNativeLayerEnabledAsync(false);

                    // Une manette en mode natif : son pad Xbox 360 virtuel est nourri ici. Toutes
                    // les familles passent par ce pad — la Xbox aussi, depuis que sa bascule emule
                    // un slot plutot que de rendre la manette physique — donc le jeu ne voit jamais
                    // que des manettes Xbox 360 ordinaires.
                    //
                    // Un "if" et non un "continue" : la ligne de compteurs par seconde est plus bas
                    // dans la boucle, et sauter la trame la sauterait aussi. Sur une machine dont la
                    // seule manette est en natif, le journal deviendrait entierement muet — l'etat
                    // exact que ces compteurs ont ete ecrits pour supprimer.

                    // Xbox mode used to report nothing at all: the per-second line showed frames and
                    // a mode, and stayed silent on whether the virtual pad was being fed. "It does
                    // not work" was then impossible to place — mapper producing nothing, or submit
                    // failing? These three numbers separate the two.
                    //
                    // The counters are family-agnostic: every family is counted through its virtual
                    // Xbox 360 report, so one line serves all of them.
                    var output = mapper.Map(mappedState);
                    var report = output.Gamepad;
                    if (report.Buttons != Xbox360Buttons.None)
                    {
                        xboxButtonFrames++;
                        xboxButtons |= report.Buttons;
                    }

                    if (report.LeftThumbX != 0 || report.LeftThumbY != 0 ||
                        report.RightThumbX != 0 || report.RightThumbY != 0)
                    {
                        xboxStickFrames++;
                    }

                    if (report.LeftTrigger != 0 || report.RightTrigger != 0)
                    {
                        xboxTriggerFrames++;
                    }

                    try
                    {
                        // To this controller's own virtual pad. Routing every controller to one
                        // pad is what made split-screen impossible: the game would see a single
                        // player receiving two people's inputs interleaved, which is not two
                        // players — it is one player being fought over. Every family switches to
                        // an ordinary Xbox 360 slot — DualSense and Xbox included — and the slot
                        // fed here must be the same one its switch created.
                        var pad = await virtualPads.ForAsync(frame.Source, cancellation.Token);
                        await pad.SubmitAsync(report, cancellation.Token);
                    }
                    catch (Exception exception)
                    {
                        // Previously this propagated and killed the loop. A driver that rejects
                        // one report should cost one frame, and should say so.
                        xboxSubmitFailures++;
                        log.Warn(LogCategory.Mapping,
                            $"gamepad submit failed: {exception.GetType().Name}: {exception.Message}");
                    }
                }

                var now = DateTimeOffset.UtcNow;
                var sinceStatus = now - lastStatus;
                if (sinceStatus >= TimeSpan.FromSeconds(1))
                {
                    var summary = counters.DrainToLine(
                        sinceStatus,
                        modeSwitcher.CurrentMode.ToString(),
                        steamWatcher.Owner.ToString());

                    // Appended in Profile mode: the sticks are always live there, so the per-second
                    // line can say whether each one is actually reaching the pointer.
                    if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile)
                    {
                        // One figure per controller, named. Pooled across all of them, a phantom pad
                        // reporting zeros beside a real one can hold the total at zero while a stick
                        // is at full deflection — which is exactly what sent this investigation
                        // after the wrong suspect.
                        // Readers and enumerations beside the frame rate. A rate that climbs on its
                        // own means either several readers on one device or a device emitting
                        // faster, and from outside the two are identical — duplicate readers share
                        // an identity and collapse into a single session, so the controller list
                        // stays reassuringly correct either way.
                        summary += $" | readers={multi.ActiveReaders} enum={multi.Enumerations}";

                        foreach (var tracked in sessions.All)
                        {
                            // The dead zone moved onto the per-controller line with the settings it
                            // belongs to. One figure for the whole machine was a summary of nothing
                            // once each controller carried its own.
                            summary += $" | {Shorten(tracked.Id)} peak=({tracked.PeakX:F2},{tracked.PeakY:F2})"
                                     + $" dz={tracked.StickPointer.DeadZone:F2}"
                                     + $" gap={tracked.LastGapMs:F1}ms";

                            tracked.PeakX = 0;
                            tracked.PeakY = 0;
                        }
                    }

                    // Appended only in Xbox mode: in Profile mode these are always zero and would
                    // just make the line harder to read.
                    //
                    // Gated on lastOutputMode, the same value that writes "mode=Xbox360" at the head
                    // of this line. It used to ask modeSwitcher.CurrentMode instead — the mode of
                    // whichever controller happened to send the last frame of the second. With three
                    // sources feeding one line that is almost never the pad in Xbox mode, so the line
                    // announced mode=Xbox360 and then omitted every Xbox figure. The one path this
                    // product exists for was invisible for as long as more than one controller was
                    // connected, which is to say always. Found 12 August, after "j'ai switch en xbox
                    // pour lancer le jeu et rien ne répondait" could not be checked against anything.
                    if (lastOutputMode == ControllerOutputMode.Xbox360)
                    {
                        summary += $" | xbox buttons={xboxButtonFrames} sticks={xboxStickFrames}"
                                 + $" triggers={xboxTriggerFrames} submitFail={xboxSubmitFailures}"
                                 + $" pressed={xboxButtons}";

                        xboxButtonFrames = 0;
                        xboxStickFrames = 0;
                        xboxTriggerFrames = 0;
                        xboxSubmitFailures = 0;
                        xboxButtons = Xbox360Buttons.None;
                    }

                    // Once a second, alongside the counters. Rebuilding every session rather than
                    // only the ones whose assignment changed: a session is cheap, and working out
                    // which controllers a rewritten file affects is more code than simply starting
                    // them all again from what it now says. The overlay state survives per session,
                    // inside ControllerSessionSet.Reload, so an open keyboard stays open.
                    var bookStamp = ControllerProfilesTimestamp();
                    if (bookStamp != profileBookStamp)
                    {
                        profileBookStamp = bookStamp;
                        profileBook = LoadControllerProfiles(
                            profileName ?? CoreCommandLine.DefaultProfile, log);

                        sessions.Reload();
                        session = sessions.For(frameSource);
                        profileMapper = session.ProfileMapper;
                        modeSwitcher = session.ModeSwitcher;
                        mapper = session.XboxMapper;

                        log.Info(LogCategory.Session, "Per-controller profiles reloaded.");
                    }

                    log.Info(LogCategory.Counters, summary);
                    Console.WriteLine(summary);
                    lastStatus = now;
                }
            }
            DLog("Main loop ended (no more frames).");
        }
        catch (OperationCanceledException)
        {
            DLog("Cancelled (Ctrl+C or timeout).");
            break;
        }
        catch (Exception ex)
        {
            DLog($"Connection error: {ex.GetType().Name}: {ex.Message}");
            DLog($"Stack: {ex.StackTrace}");
            Console.WriteLine($"Connection error: {ex.Message}");
        }
        finally
        {
            // Before anything else: give back what the mapper is holding. A controller switched off
            // mid-hold sends no further frames, so the release edge never comes and the button stays
            // down for the whole machine — the second road to the same defect as the overlay one.
            profileMapper.ReleaseHeldInput();

            if (source is not null)
            {
                DLog("Disposing HID source...");
                await source.DisposeAsync();
                DLog("HID source disposed.");
            }
        }

        if (cancellation.Token.IsCancellationRequested)
            break;

        if (powerOffRequested)
        {
            powerOffRequested = false;

            // The detector latches until the chord is fully released, and no frames arrive from a
            // controller that is off — so without this it would still be latched on the next power
            // up, and the chord would never fire again. Every session's, because the one that fired
            // is the one that left.
            foreach (var s in sessions.All)
            {
                s.PowerOffChordDetector.Reset();
                s.PowerOffChordGate.Reset();
            }

            if (!await WaitForControllerReturnAsync(virtualPads, haptics, oskInstances, physicalXInputSlots, forced, DLog, cancellation.Token))
            {
                break;
            }

            // Straight back to opening the device: no reset delay, nothing disconnected unexpectedly.
            continue;
        }

        // Handing over to Steam is a deliberate release, not a disconnect: skip the reset delay and
        // the device scan, and go straight back to the stand-down wait at the top of the loop.
        if (steamWatcher.Owner == ControllerOwner.Steam)
            continue;

        DLog("Waiting 10s for controller to reset after disconnect...");
        Console.WriteLine("Waiting for controller to reset...");
        try { await Task.Delay(10000, cancellation.Token); }
        catch (OperationCanceledException) { break; }

        bool found = false;
        for (int retry = 0; retry < 10; retry++)
        {
            DLog($"Reconnection attempt {retry + 1}/10 - enumerating all Valve HID devices...");

            try
            {
                var allValve = HidSharp.DeviceList.Local
                    .GetHidDevices(SteamHidConstants.ValveVendorId)
                    .ToArray();
                DLog($"  Visible Valve HID devices: {allValve.Length}");
                foreach (var dev in allValve)
                {
                    int inLen = 0, outLen = 0, featLen = 0;
                    bool canOpen = false;
                    try { inLen = dev.GetMaxInputReportLength(); } catch { }
                    try { outLen = dev.GetMaxOutputReportLength(); } catch { }
                    try { featLen = dev.GetMaxFeatureReportLength(); } catch { }
                    try { if (dev.TryOpen(out var s)) { canOpen = true; s.Dispose(); } } catch { }
                    DLog($"    PID=0x{dev.ProductID:X4} in={inLen} out={outLen} feat={featLen} canOpen={canOpen} path={dev.DevicePath}");
                }
            }
            catch (Exception ex)
            {
                DLog($"  Enumeration error: {ex.GetType().Name}: {ex.Message}");
            }

            // A disconnected controller may come back as any family — Steam, PlayStation or Xbox —
            // so the probe is the same one the power-off wait uses, not the Valve-only check this
            // loop used to make.
            try
            {
                if (SenSÉ.App.Console.AttachedControllers.AnyAttached(physicalXInputSlots, forced, DLog))
                {
                    DLog($"Controller found on retry {retry + 1}.");
                    Console.WriteLine("Controller reconnected.");
                    found = true;
                    break;
                }
                DLog($"Controller not found on attempt {retry + 1}/10.");
            }
            catch (Exception ex)
            {
                DLog($"Controller discovery error on attempt {retry + 1}/10: {ex.GetType().Name}: {ex.Message}");
            }

            if (retry < 9)
            {
                int delay = 2000 * (retry + 1);
                DLog($"Retrying in {delay}ms...");
                try { await Task.Delay(delay, cancellation.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (!found)
        {
            DLog("Controller disconnected after 10 retries. Exiting.");
            Console.WriteLine("Controller disconnected. Exiting.");
            break;
        }

        DLog("Reconnection successful, resuming main loop.");
    }

    // Before anything else: a hidden overlay that outlives us would hold the pad pipe open.


    log.Info(LogCategory.Session, "Releasing virtual Xbox 360 pads.");
    Console.WriteLine("Virtual Xbox 360 pads released.");
    await padSender.DisposeAsync();

    // The controllers go back to everyone else. Only the tidy exits reach this line — a crash or a
    // force-kill does not — which is why the reset at startup exists rather than only this.
    SenSÉ.App.Console.ControllerCloak.Release(log);

    // The resident keyboards are asked to leave, not merely hidden. A close signal now means "hide"
    // for them, so without this they would sit invisible until the kernel ends them with this
    // session — which it would, but a process asked to leave puts its own pipes away first.
    SenSÉ.App.Console.OskPrewarmSet.StopAll(message => log.Info(LogCategory.Osk, message));

    log.Info(LogCategory.Session, "=== SenSÉ session end ===");

    // Releases whoever asked us to close. A close request is being held until this line, so that
    // the controllers are given back before the window disappears.
    SenSÉ.App.Console.GracefulShutdown.Done();

    // The log is disposed by its using declaration.
}

static ControllerOutputMode ReadInitialOutputMode(string[] args)
{
    return ReadOptionValue(args, "--start-mode")?.ToLowerInvariant() switch
    {
        "xbox" => ControllerOutputMode.Xbox360,
        "xbox360" => ControllerOutputMode.Xbox360,
        "gamepad" => ControllerOutputMode.Xbox360,
        "native" => ControllerOutputMode.Xbox360,
        "profile" => ControllerOutputMode.Profile,
        _ => ControllerOutputMode.Profile
    };
}

/// <summary>
/// Pulses each candidate side byte in turn, announcing it, so the operator can say which pad
/// actually buzzed. Settles the side mapping by observation instead of by assumption.
/// <summary>
/// Sweeps the haptic side byte to find out which actuators this firmware actually has.
/// </summary>
/// <remarks>
/// Sides 0x00 and 0x01 are the two halves of the controller and are known to work. Anything above is
/// unknown: the report format is reverse-engineered, and whether this controller has trigger
/// actuators has never been established. This pulses each index in turn and asks what was felt, so
/// the answer comes from the hardware instead of from a guess.
/// <summary>
/// Sends the controller power-off command, or sweeps the candidate variants to find which one works.
/// </summary>
/// <remarks>
/// The command has never been confirmed on this firmware. The envelope is now the same one the
/// native-layer commands use, which are known to work, but the payload is still a guess. Running
/// this with --probe tries each candidate in turn and stops as soon as the controller goes quiet.
/// </remarks>
static void RunPowerOffProbe(string[] args)
{
    var probe = args.Contains("--probe", StringComparer.OrdinalIgnoreCase);

    var discovery = new SteamHidDiscovery(Console.WriteLine);
    var device = discovery.FindPreferredControllerDevice();
    if (device is null)
    {
        Console.WriteLine("No controller found. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the device. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    var gate = new object();

    using (stream)
    {
        stream.WriteTimeout = 500;

        if (!probe)
        {
            try
            {
                SteamControllerPowerOff.Send(stream, gate);
                Console.WriteLine($"Sent: {SteamControllerPowerOff.Variants[0].Name}");
                Console.WriteLine("If the controller is still on, run again with --probe.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"The device rejected the report: {exception.GetType().Name}: {exception.Message}");
            }

            return;
        }

        Console.WriteLine("Power-off probe");
        Console.WriteLine("===============");
        Console.WriteLine();
        Console.WriteLine("Each variant is sent in turn. Stop as soon as the controller switches off:");
        Console.WriteLine("the variant just named is the working one, and its number is what to report.");
        Console.WriteLine();

        for (var index = 0; index < SteamControllerPowerOff.Variants.Count; index++)
        {
            var (name, _) = SteamControllerPowerOff.Variants[index];
            Console.WriteLine($"--- variant {index}: {name} ---");

            try
            {
                SteamControllerPowerOff.SendVariant(stream, gate, index);
                Console.WriteLine("  sent");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"  rejected: {exception.GetType().Name}: {exception.Message}");
            }

            Console.Write("  Did the controller switch off? (enter to try the next one) ");
            Console.ReadLine();
        }

        Console.WriteLine();
        Console.WriteLine("If none of them worked, this firmware does not accept any of these payloads.");
    }
}

/// <summary>
/// Sends the DualSense Bluetooth power-off feature report once and reports what Windows did.
/// </summary>
/// <remarks>
/// The report is the 48-byte <c>0x08</c> form Windows presents for the Bluetooth control channel,
/// checksummed with the <c>0xA3</c> seed the controller verifies. Use this to test a single pad
/// without going through the chord: if the pad's light turns off, the command was delivered and
/// accepted.
/// </remarks>
static void RunDualSensePowerOffProbe()
{
    // A DualSense has no power-off command. It switches itself off when it loses its Bluetooth
    // link, so the only probe worth running is the one that drops the link. This name is kept
    // because it is what the command line offers, and what the user is actually asking for.
    Console.WriteLine("A DualSense has no power-off report; dropping its Bluetooth link instead.");
    RunDualSenseBtCut();
}
static void RunDualSenseBtCut()
{
    var device = SenSÉ.Hid.DualSenseControllerSource.Discover(Console.WriteLine).FirstOrDefault();
    if (device is null)
    {
        Console.WriteLine("No DualSense found. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    Console.WriteLine($"Device: {device.DevicePath}");
    Console.WriteLine("Cutting the Bluetooth link to the controller...");

    // Through the guard like everything else. A diagnostic that takes the shortcut is still a
    // shortcut, and it is the one somebody copies when they want to disconnect something that is
    // not a controller.
    var probed = new ControllerIdentity(
        ControllerKind.DualSense,
        ControllerIdentityFactory.FromHidPath(device.DevicePath),
        "Manette PS5",
        Slot: -1);

    if (!SenSÉ.Windows.DualSenseShutdown.PowerOff(probed, device.DevicePath, Console.WriteLine))
    {
        Console.WriteLine("No Bluetooth link was cut. See the lines above for why.");
        return;
    }

    for (var i = 0; i < 40; i++)
    {
        Thread.Sleep(250);
        if (!SenSÉ.Hid.DualSenseControllerSource.Discover().Any())
        {
            Console.WriteLine($"The controller went offline after {(i + 1) * 0.25:F2}s — it powered off.");
            return;
        }
    }

    Console.WriteLine("The controller is still present; it may have reconnected instead of powering off.");
}
static void RunHapticActuatorProbe(string[] args)
{
    int maxIndex = int.TryParse(ReadOptionValue(args, "--max"), out var parsed) ? Math.Clamp(parsed, 1, 32) : 8;

    Console.WriteLine("Haptic actuator probe");
    Console.WriteLine("=====================");
    Console.WriteLine();
    Console.WriteLine("Hold the controller normally, with a finger resting on each trigger.");
    Console.WriteLine("For each index, note what moved: left grip, right grip, left pad, right pad,");
    Console.WriteLine("left trigger, right trigger, or nothing at all.");
    Console.WriteLine();
    Console.WriteLine("Sides 0 and 1 are the known-good halves; expect those to work. They are your");
    Console.WriteLine("reference for how strong a real pulse feels.");
    Console.WriteLine();

    var discovery = new SteamHidDiscovery(Console.WriteLine);
    var device = discovery.FindPreferredControllerDevice();
    if (device is null)
    {
        Console.WriteLine("No controller found. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the device. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    using (stream)
    {
        stream.WriteTimeout = 250;
        int reportLength = Math.Max(7, device.GetMaxOutputReportLength());
        var builder = new TritonHapticReportBuilder();

        for (int side = 0; side <= maxIndex; side++)
        {
            Console.WriteLine($"--- side 0x{side:X2} ---");

            for (int burst = 0; burst < 6; burst++)
            {
                try
                {
                    stream.Write(builder.BuildRawSidePulse((byte)side, onUs: 528, reportLength));
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"  write failed: {exception.GetType().Name}: {exception.Message}");
                    break;
                }

                Thread.Sleep(120);
            }

            Console.Write("  What did you feel? (enter to continue) ");
            Console.ReadLine();
        }
    }

    Console.WriteLine();
    Console.WriteLine("Done. If a trigger responded, set xboxTriggerActuatorIndex in the profile to");
    Console.WriteLine("the LEFT trigger's index; the right one is assumed to be the next index up.");
    Console.WriteLine("The setting lives in %LOCALAPPDATA%\\SenSÉ\\profiles, with the rest of the Xbox layout.");
    Console.WriteLine("If nothing above side 0x01 ever responded, this firmware has no trigger");
    Console.WriteLine("actuators and the setting should stay off.");
}
/// </summary>
static void RunHapticSideSweep()
{
    Console.WriteLine("Haptic side sweep.");
    Console.WriteLine("Hold the controller and note which pad vibrates for each announced value.");
    Console.WriteLine();

    var discovery = new SteamHidDiscovery();
    var device = discovery.FindPreferredControllerDevice();
    if (device is null)
    {
        Console.WriteLine("No controller found.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the controller. Stop SenSÉ first, it holds the device.");
        return;
    }

    using (stream)
    {
        stream.WriteTimeout = 250;
        var reportLength = Math.Max(8, device.GetMaxOutputReportLength());
        var builder = new TritonHapticReportBuilder();

        foreach (byte side in new byte[] { 0x00, 0x01, 0x02, 0x03 })
        {
            Console.WriteLine($"  side = 0x{side:X2} ... (3 pulses)");

            for (var i = 0; i < 3; i++)
            {
                try
                {
                    stream.Write(builder.BuildRawSidePulse(side, onUs: 600, reportLength));
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"    write failed: {exception.GetType().Name}: {exception.Message}");
                    break;
                }

                Thread.Sleep(250);
            }

            Thread.Sleep(1200);
        }
    }

    Console.WriteLine();
    Console.WriteLine("Report which side value hit the LEFT pad and which hit the RIGHT pad.");
}

/// <summary>
/// Diagnoses how the LEFT pad is addressed. The side sweep showed no pulse side byte (0x00-0x03)
/// drives the left pad, so this sweeps the alternative report formats — most notably the rumble
/// report, which carries the two halves by byte position rather than by a side byte.
/// </summary>
static void RunHapticFormatTest()
{
    Console.WriteLine("Haptic format test.");
    Console.WriteLine("Hold the controller. For each format, note which pad moved:");
    Console.WriteLine("LEFT, RIGHT, both, or none. Press Enter after each to continue.");
    Console.WriteLine();

    var discovery = new SteamHidDiscovery();
    var device = discovery.FindPreferredControllerDevice();
    if (device is null)
    {
        Console.WriteLine("No controller found. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the device. Stop SenSÉ first, then run this as administrator.");
        return;
    }

    using (stream)
    {
        stream.WriteTimeout = 250;
        int reportLength = Math.Max(10, device.GetMaxOutputReportLength());
        var builder = new TritonHapticReportBuilder();

        var formats = new (string Label, byte[] Report)[]
        {
            ("pulse side 0x00 (control: expect RIGHT)", builder.BuildRawSidePulse(0x00, onUs: 528, reportLength)),
            ("pulse side 0x01 (control: nothing so far)", builder.BuildRawSidePulse(0x01, onUs: 528, reportLength)),
            ("rumble LEFT half only", builder.BuildRawRumble(leftHalf: true, rightHalf: false, intensity: 20, reportLength)),
            ("rumble RIGHT half only", builder.BuildRawRumble(leftHalf: false, rightHalf: true, intensity: 20, reportLength)),
            ("rumble both halves", builder.BuildRawRumble(leftHalf: true, rightHalf: true, intensity: 20, reportLength)),
            ("pulse side 0x04", builder.BuildRawSidePulse(0x04, onUs: 528, reportLength)),
            ("pulse side 0x05", builder.BuildRawSidePulse(0x05, onUs: 528, reportLength)),
        };

        foreach (var (label, report) in formats)
        {
            Console.WriteLine($"--- {label} ---");

            for (int burst = 0; burst < 6; burst++)
            {
                try
                {
                    stream.Write(report);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"  write failed: {exception.GetType().Name}: {exception.Message}");
                    break;
                }

                Thread.Sleep(120);
            }

            Console.Write("  What moved? (enter to continue) ");
            Console.ReadLine();
        }
    }

    Console.WriteLine();
    Console.WriteLine("Done. If the rumble LEFT half moved the left pad, the tick path must use the");
    Console.WriteLine("rumble report for left-side commands. Tell me which format hit the LEFT pad.");
}

/// <summary>
/// One-shot diagnostic report: identity of all three executables, drivers, HID state, resolved
/// settings and running processes. Written to stdout and to a file so it can be pasted whole.
/// </summary>
static void RunDiagnosticReport(string[] args)
{
    var lines = new List<string>();
    void Section(string title)
    {
        lines.Add("");
        lines.Add($"=== {title} ===");
    }

    lines.Add("SenSÉ diagnostic report");
    Section("identity (this process)");
    lines.AddRange(SessionReport.Identity(args));

    Section("executables next to this one");
    foreach (var name in new[]
    {
        "SenSÉ.exe",
        "SenSÉ.Core.exe",
        "SenSÉSteam.Osk.exe",
        "SenSÉPS5.Osk.exe",
        "SenSÉXbox.Osk.exe",
        "SenSÉPads.Osk.exe",
        "SenSÉSticks.Osk.exe",
        "SenSÉ.Osk.exe",
    })
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?";
            lines.Add($"{name,-22} v{version,-10} {info.Length,12:N0} bytes  {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            lines.Add($"{name,-22} MISSING  <-- the GUI and core launch each other from this directory");
        }
    }

    Section("profile");
    var profileName = ReadOptionValue(args, "--profile") ?? "Default";
    var profileResult = ProfileMapper.LoadDetailed(profileName);
    lines.AddRange(SessionReport.Profile(profileName, profileResult));

    Section("effective settings");
    lines.AddRange(SessionReport.EffectiveSettings(profileResult.Settings));

    Section("overlay keyboard settings");
    lines.AddRange(SessionReport.OverlayKeyboard(OskSettings.Load()));

    Section("processes");
    foreach (var name in new[]
    {
        "steam",
        "SenSÉ",
        "SenSÉ.Core",
        "SenSÉPads.Osk",
        "SenSÉSticks.Osk",
        "SenSÉ.Osk",
    })
    {
        var found = System.Diagnostics.Process.GetProcessesByName(name);
        try
        {
            lines.Add($"{name,-22} {(found.Length == 0 ? "not running" : $"{found.Length} instance(s): {string.Join(", ", found.Select(p => p.Id))}")}");
        }
        finally
        {
            foreach (var process in found) { try { process.Dispose(); } catch { } }
        }
    }

    Section("HID (Valve devices)");
    try
    {
        var devices = HidSharp.DeviceList.Local.GetHidDevices(SteamHidConstants.ValveVendorId).ToArray();
        lines.Add($"total Valve HID interfaces: {devices.Length}");
        foreach (var device in devices)
        {
            int input = 0, output = 0, feature = 0;
            bool canOpen = false;
            try { input = device.GetMaxInputReportLength(); } catch { }
            try { output = device.GetMaxOutputReportLength(); } catch { }
            try { feature = device.GetMaxFeatureReportLength(); } catch { }
            try { if (device.TryOpen(out var stream)) { canOpen = true; stream.Dispose(); } } catch { }
            lines.Add($"  PID=0x{device.ProductID:X4} in={input} out={output} feat={feature} canOpen={canOpen}");
        }

        var preferred = new SteamHidDiscovery().FindPreferredControllerDevice();
        lines.Add($"preferred controller: {(preferred is null ? "NONE FOUND" : $"PID=0x{preferred.ProductID:X4}")}");
    }
    catch (Exception exception)
    {
        lines.Add($"HID enumeration failed: {exception.GetType().Name}: {exception.Message}");
    }

    Section("log files");
    var oskDir = Path.Combine(CheminDebug.Racine, CheminDebug.SousDossierOsk);
    var dbgDir = Path.Combine(CheminDebug.Racine, CheminDebug.SousDossierDebug);
    var logNames = new[]
        {
            Path.Combine(dbgDir, "SenSÉ-debug.log"),
            Path.Combine(dbgDir, "SenSÉ-debug.log.1"),
            Path.Combine(oskDir, "SenSÉ-osk-debug.log"),
        }
            .Concat(Directory.Exists(oskDir)
                ? Directory.EnumerateFiles(oskDir, "*.log")
                : []);

    foreach (var path in logNames.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var name = Path.GetFileName(path) ?? path;
        lines.Add(File.Exists(path)
            ? $"{name,-28} {new FileInfo(path).Length,12:N0} bytes  {new FileInfo(path).LastWriteTime:yyyy-MM-dd HH:mm:ss}"
            : $"{name,-28} absent");
    }

    var report = string.Join(Environment.NewLine, lines);
    Console.WriteLine(report);

    try
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "SenSÉ-diag.txt");
        File.WriteAllText(outputPath, report);
        Console.WriteLine();
        Console.WriteLine($"Report written to: {outputPath}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Could not write the report file: {exception.Message}");
    }
}

/// <summary>
/// Reads --log-level. Defaults to Debug so a normal run captures decisions, and to Trace when
/// --debug is passed so the Frame category has something to emit.
/// </summary>
static LogLevel ReadLogLevel(string[] args)
{
    var value = ReadOptionValue(args, "--log-level");
    if (value is not null && Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed))
    {
        return parsed;
    }

    return args.Contains("--debug", StringComparer.OrdinalIgnoreCase) ? LogLevel.Trace : LogLevel.Debug;
}

/// <summary>
/// Reads --log-categories as a comma-separated list, or "all". Frame tracing is opt-in because it
/// costs roughly 200 lines per second.
/// </summary>
static LogCategory ReadLogCategories(string[] args)
{
    var value = ReadOptionValue(args, "--log-categories");
    if (string.IsNullOrWhiteSpace(value))
    {
        return LogCategory.Default;
    }

    if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
    {
        return LogCategory.All;
    }

    var result = LogCategory.None;
    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Enum.TryParse<LogCategory>(token, ignoreCase: true, out var category))
        {
            result |= category;
        }
        else
        {
            Console.WriteLine($"Unknown log category '{token}'. Valid: {string.Join(", ", Enum.GetNames<LogCategory>())}");
        }
    }

    // Session is always useful; never let a filter hide the identity header.
    return result == LogCategory.None ? LogCategory.Default : result | LogCategory.Session;
}

static SteamControllerButtons ReadModeSwitchButtons(string[] args)
{
    return ReadOptionValue(args, "--switch-button")?.ToLowerInvariant() switch
    {
        "quick" => SteamControllerButtons.QuickAccess,
        "quick-access" => SteamControllerButtons.QuickAccess,
        "qam" => SteamControllerButtons.QuickAccess,
        "steam-or-quick-access" => SteamControllerButtons.Steam | SteamControllerButtons.QuickAccess,
        "steam-or-qam" => SteamControllerButtons.Steam | SteamControllerButtons.QuickAccess,
        "steam" => SteamControllerButtons.Steam,
        "guide" => SteamControllerButtons.Steam,
        "xbox" => SteamControllerButtons.Steam,
        _ => SteamControllerButtons.QuickAccess
    };
}

static string? ReadOptionValue(string[] args, string optionName)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void StopOtherInstances(bool waitForExit)
{
    using var current = Process.GetCurrentProcess();
    var currentPath = TryGetMainModuleFileName(current);
    var stopped = 0;
    var skipped = 0;

    foreach (var process in Process.GetProcessesByName(current.ProcessName))
    {
        using (process)
        {
            if (process.Id == current.Id)
            {
                continue;
            }

            if (!IsSameExecutable(process, currentPath))
            {
                skipped++;
                continue;
            }

            try
            {
                // Asked before being killed. This used to go straight to Kill, which is why the
                // session's cleanup — giving the controllers back, releasing the virtual pads,
                // turning HidHide's cloaking off — was never reached by any of the three ways a user
                // has of stopping SenSÉ. A controller stayed hidden from every game as a result.
                if (SenSÉ.App.Console.PoliteStop.Ask(process, TimeSpan.FromSeconds(6)))
                {
                    stopped++;
                    continue;
                }

                Console.WriteLine(
                    $"Process {process.Id} did not answer the stop request; stopping it outright.");

                process.Kill(entireProcessTree: true);
                if (waitForExit)
                {
                    process.WaitForExit(5000);
                }

                stopped++;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                Console.WriteLine($"Could not stop process {process.Id}: {exception.Message}");
            }
        }
    }

    Console.WriteLine(stopped == 0
        ? "No existing SenSÉ process found."
        : $"Stopped {stopped} existing SenSÉ process(es).");

    if (skipped > 0)
    {
        Console.WriteLine($"Skipped {skipped} process(es) with the same name but a different executable path.");
    }
}

static void ConfigureHidHide()
{
    var appPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrWhiteSpace(appPath))
    {
        throw new InvalidOperationException("Unable to resolve current executable path for HidHide registration.");
    }

    var configurator = new HidHideConfigurator();
    var result = configurator.SetupForSenSÉ(appPath);

    Console.WriteLine($"Registered HidHide application: {result.ApplicationPath}");
    if (result.HiddenDevices.Count == 0)
    {
        Console.WriteLine("No Valve Steam Controller HID devices were found in HidHide's device list.");
    }
    else
    {
        Console.WriteLine("Hidden Valve Steam Controller HID devices:");
        foreach (var device in result.HiddenDevices)
        {
            Console.WriteLine($"  {device}");
        }
    }

    Console.WriteLine("HidHide cloaking is now enabled.");
}

static void PrintHidHideStatus()
{
    Console.WriteLine(new HidHideConfigurator().GetStatus());
}

static void DisableHidHide()
{
    new HidHideConfigurator().DisableCloaking();
    Console.WriteLine("HidHide cloaking disabled.");
}

static bool IsSameExecutable(Process process, string? currentPath)
{
    if (string.IsNullOrWhiteSpace(currentPath))
    {
        return string.Equals(process.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    var processPath = TryGetMainModuleFileName(process);
    return string.Equals(processPath, currentPath, StringComparison.OrdinalIgnoreCase);
}

static string? TryGetMainModuleFileName(Process process)
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

static int? ReadSecondsOption(string[] args)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], "--seconds", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[index + 1], out var seconds) &&
            seconds > 0)
        {
            return seconds;
        }
    }

    return null;
}

static void RunHidDiagnostic()
{
    var discovery = new SteamHidDiscovery();

    Console.WriteLine("=== HID Diagnostic ===");
    Console.WriteLine();

    Console.WriteLine("--- ListValveDevices() ---");
    var devices = discovery.ListValveDevices();
    Console.WriteLine($"  Found {devices.Count} device(s)");
    foreach (var d in devices)
    {
        Console.WriteLine($"  {d.ProductName} ({d.ProductIdHex})");
        Console.WriteLine($"    Reports: input={d.MaxInputReportLength}, output={d.MaxOutputReportLength}, feature={d.MaxFeatureReportLength}");
        Console.WriteLine($"    CanOpen: {d.CanOpen}");
        if (d.OpenError is not null) Console.WriteLine($"    OpenError: {d.OpenError}");
        Console.WriteLine($"    IsKnownSteamController: {d.IsKnownSteamController}");
        Console.WriteLine($"    Path: {d.DevicePath}");
    }

    Console.WriteLine();
    Console.WriteLine("--- FindPreferredControllerDevice() (original) ---");
    try
    {
        var preferred = discovery.FindPreferredControllerDevice();
        if (preferred is null)
        {
            Console.WriteLine("  Result: NULL");
        }
        else
        {
            Console.WriteLine($"  Result: {preferred.ProductID} path={preferred.DevicePath}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }

    Console.WriteLine();
    Console.WriteLine("--- Manual step-by-step (mirrors FindPreferredControllerDevice) ---");
    try
    {
        var allDevices = HidSharp.DeviceList.Local
            .GetHidDevices(SteamHidConstants.ValveVendorId)
            .ToArray();
        Console.WriteLine($"  Total Valve HID devices: {allDevices.Length}");

        foreach (var device in allDevices)
        {
            Console.WriteLine();
            Console.WriteLine($"  Device: VID=0x{device.VendorID:X4} PID=0x{device.ProductID:X4} Path={device.DevicePath}");
            Console.WriteLine($"    IsKnownProduct: {SteamHidConstants.IsKnownSteamControllerProduct(device.ProductID)}");

            int inputLen = -1, outputLen = -1, featureLen = -1;
            string? inputErr = null, outputErr = null, featureErr = null;
            try { inputLen = device.GetMaxInputReportLength(); }
            catch (Exception ex) { inputErr = $"{ex.GetType().Name}: {ex.Message}"; }
            try { outputLen = device.GetMaxOutputReportLength(); }
            catch (Exception ex) { outputErr = $"{ex.GetType().Name}: {ex.Message}"; }
            try { featureLen = device.GetMaxFeatureReportLength(); }
            catch (Exception ex) { featureErr = $"{ex.GetType().Name}: {ex.Message}"; }

            Console.WriteLine($"    GetMaxInputReportLength:  {(inputErr is null ? inputLen.ToString() : $"ERROR ({inputErr})")}");
            Console.WriteLine($"    GetMaxOutputReportLength: {(outputErr is null ? outputLen.ToString() : $"ERROR ({outputErr})")}");
            Console.WriteLine($"    GetMaxFeatureReportLength:{(featureErr is null ? featureLen.ToString() : $"ERROR ({featureErr})")}");

            bool canOpen = false;
            string? openErr = null;
            try
            {
                if (device.TryOpen(out var stream))
                {
                    canOpen = true;
                    stream.Dispose();
                }
                else
                {
                    openErr = "TryOpen returned false";
                }
            }
            catch (Exception ex) { openErr = $"{ex.GetType().Name}: {ex.Message}"; }

            Console.WriteLine($"    CanOpen: {canOpen}");
            if (openErr is not null) Console.WriteLine($"    OpenError: {openErr}");

            bool isControllerState = inputLen >= 54 && outputLen > 0 && featureLen > 0;
            Console.WriteLine($"    IsControllerStateInterface: {isControllerState} (input={inputLen} >=54: {inputLen >= 54}, output={outputLen} >0: {outputLen > 0}, feature={featureLen} >0: {featureLen > 0})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

static void PrintUsage()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  hid-list       List Valve HID interfaces visible to HidSharp.");
    Console.WriteLine("  hid-probe      Capture raw input reports for 3 seconds.");
    Console.WriteLine("  hid-diag       Diagnostic: compare ListValveDevices vs FindPreferredControllerDevice.");
    Console.WriteLine("  haptic-sides   Pulse each side byte in turn to identify which one drives which pad.");
    Console.WriteLine("  haptic-format-test  Sweep report formats (pulse, rumble halves) to find the LEFT pad.");
    Console.WriteLine("  diag           Full report: binary versions, drivers, HID, resolved settings,");
    Console.WriteLine("                 running processes. Writes SenSÉ-diag.txt. Add --profile NAME.");
    Console.WriteLine("  haptic-test    Send low-power Steam Controller 2026 trackpad haptic reports; requires --yes.");
    Console.WriteLine("  xbox-run       Stream Steam Controller input to a virtual Xbox 360 controller.");
    Console.WriteLine("                Options: --seconds N, --no-haptics, --restart");
    Console.WriteLine("                         --start-mode xbox360|profile");
    Console.WriteLine("                         --no-mode-switch");
    Console.WriteLine("                         --auto-mode  Also switch Profile/Xbox360 from the foreground window");
    Console.WriteLine("                         --switch-button steam|quick-access|steam-or-quick-access");
    Console.WriteLine("                         --debug  Mirror the log to the console and enable frame tracing");
    Console.WriteLine("                         --log-level error|warn|info|debug|trace");
    Console.WriteLine("                         --log-categories all | hid,mapping,haptics,osk,owner,mode,pipe,frame,counters");
    Console.WriteLine("  stop           Kill other running SenSÉ instances from the same executable path.");
    Console.WriteLine("  hidhide-setup  Register SenSÉ with HidHide and cloak Valve physical HID devices.");
    Console.WriteLine("  hidhide-status Print HidHide state.");
    Console.WriteLine("  power-off      Switch the controller off. Options: --probe to sweep the variants.");
    Console.WriteLine("  ds-power-off   Send the DualSense Bluetooth power-off feature report and check the pad.");
    Console.WriteLine("  ds-bt-cut      Cut the DualSense Bluetooth link (the link-drop power-off) and check the pad.");
    Console.WriteLine("  haptic-probe   Sweep haptic actuator indices to find out which exist.");
    Console.WriteLine("                Options: --max N (default 8)");
    Console.WriteLine("  hidhide-off    Disable HidHide cloaking.");
    Console.WriteLine("  pads-cleanup   Remove the device records our own virtual pads left behind.");
    Console.WriteLine("                 Needs administrator. Run by the uninstaller.");
    Console.WriteLine("  help           Print this help.");
    Console.WriteLine("  sanity         Run a static mapping sanity check.");
}

internal static partial class NativeMethods
{
    public const int SwHide = 0;

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr windowHandle, int command);
}
