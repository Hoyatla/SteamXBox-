using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Osk;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;
using HidSharp;
using Sc2Xboxed.Hid;
using Sc2Xboxed.VirtualGamepad;
using Sc2Xboxed.Windows;

Action<string>? DebugLog = null;

if (args.Length > 0)
{
    if (args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
        var logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };
        DebugLog = (string msg) =>
        {
            var line = $"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            logFile.WriteLine(line);
        };
        DebugLog($"=== SteamXBox debug log ===");
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
    var mapper = new DefaultSteamControllerMapper();

    var frame = SteamControllerState.Empty(TimeSpan.Zero) with
    {
        Buttons =
            SteamControllerButtons.L4 |
            SteamControllerButtons.R4 |
            SteamControllerButtons.L5 |
            SteamControllerButtons.R5
    };

    var output = mapper.Map(frame);

    Console.WriteLine("SteamXBox profile sanity check");
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
        case "power-off":
            RunPowerOffProbe(args);
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
        Console.WriteLine("Run: dotnet run --project src\\Sc2Xboxed.App.Console -- haptic-test --yes");
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
/// Stands SteamXBox down for as long as Steam owns the controller, then restores everything.
/// Returns false when cancelled, which means the caller should exit rather than reclaim.
/// </summary>
static async Task<bool> WaitWhileSteamOwnsAsync(
    SteamPresenceWatcher watcher,
    ViGEmXbox360Sink gamepad,
    TritonHapticSink haptics,
    Action<string> log,
    CancellationToken cancellationToken)
{
    log("Releasing the controller to Steam.");

    // Mute before dropping the stream so a queued overlay tick cannot reopen the device.
    haptics.Muted = true;
    haptics.Reset();

    StopOskOverlay(log);

    try
    {
        await gamepad.DisconnectAsync().ConfigureAwait(false);
        log("Virtual Xbox 360 controller unplugged.");
    }
    catch (Exception exception)
    {
        log($"Unplugging the virtual pad failed: {exception.GetType().Name}: {exception.Message}");
    }

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

        if (watcher.Poll(DateTimeOffset.UtcNow) && watcher.Owner == ControllerOwner.SteamXBox)
        {
            break;
        }
    }

    haptics.Muted = false;

    try
    {
        await gamepad.ConnectAsync(cancellationToken).ConfigureAwait(false);
        log("Virtual Xbox 360 controller replugged.");
    }
    catch (OperationCanceledException)
    {
        return false;
    }
    catch (Exception exception)
    {
        log($"Replugging the virtual pad failed: {exception.GetType().Name}: {exception.Message}");
    }

    return true;
}

/// <summary>
/// Stands by after the user switched the controller off, until it comes back on.
/// </summary>
/// <remarks>
/// Deliberately unbounded, unlike the disconnection path, which gives up after ten retries and exits.
/// A controller switched off on purpose may be switched back on in ten seconds or tomorrow morning,
/// and either way SteamXBox should still be there. Returns false only when cancelled.
/// </remarks>
static async Task<bool> WaitForControllerReturnAsync(
    ViGEmXbox360Sink gamepad,
    TritonHapticSink haptics,
    Action<string> log,
    CancellationToken cancellationToken)
{
    log("Controller powered off; standing by until it comes back.");
    Console.WriteLine("Controller off. Waiting for it to come back on...");

    haptics.Muted = true;
    haptics.Reset();
    StopOskOverlay(log);

    try
    {
        await gamepad.DisconnectAsync().ConfigureAwait(false);
    }
    catch (Exception exception)
    {
        log($"Unplugging the virtual pad failed: {exception.GetType().Name}: {exception.Message}");
    }

    var discovery = new SteamHidDiscovery(_ => { });

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

        try
        {
            if (discovery.FindPreferredControllerDevice() is not null)
            {
                break;
            }
        }
        catch (Exception exception)
        {
            log($"Controller scan failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    log("Controller detected again; resuming.");
    Console.WriteLine("Controller back on, resuming.");

    haptics.Muted = false;

    try
    {
        await gamepad.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        return false;
    }
    catch (Exception exception)
    {
        log($"Replugging the virtual pad failed: {exception.GetType().Name}: {exception.Message}");
    }

    return true;
}

/// <summary>Asks the overlay keyboard process to close, if it is running.</summary>
static void StopOskOverlay(Action<string> log)
{
    try
    {
        var closeSignalPath = Path.Combine(AppContext.BaseDirectory, "osk-close.signal");
        File.WriteAllText(closeSignalPath, DateTime.UtcNow.Ticks.ToString());

        // Same insurance as the toggle path: never leave a latched SHIFT behind.
        InputHelper.KeyUp(0xA0);

        log("OSK close signal written.");
    }
    catch (Exception exception)
    {
        log($"OSK close signal failed: {exception.GetType().Name}: {exception.Message}");
    }
}

static async Task RunXbox360LiveAsync(string[] args, Action<string>? debugLog = null)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");

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

    log.Info(LogCategory.Session, "=== SteamXBox session start ===");
    log.WriteBlock(LogLevel.Info, LogCategory.Session, "identity", SessionReport.Identity(args));
    log.Info(LogCategory.Session, $"log file       : {logPath} (level={log.Level}, categories={log.Categories})");

    if (args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
    {
        log.Info(LogCategory.Session, "Stopping other instances...");
        StopOtherInstances(waitForExit: true);
    }

    using var cancellation = new CancellationTokenSource();
    var enableModeSwitch = !args.Contains("--no-mode-switch", StringComparer.OrdinalIgnoreCase);
    var initialMode = ReadInitialOutputMode(args);
    var switchButtons = ReadModeSwitchButtons(args);
    var modeSwitcher = new InputModeHandler(
        initialMode,
        switchButtons,
        TimeSpan.FromMilliseconds(350));
    // Xbox360-mode button mapping, edited in the Xbox tab and kept apart from the desktop profile.
    var xboxProfileName = ReadOptionValue(args, "--xbox-profile") ?? XboxProfile.DefaultName;
    var xboxProfile = XboxProfile.Load(xboxProfileName);
    DefaultSteamControllerMapper.ButtonMap = xboxProfile.Map;
    DefaultSteamControllerMapper.Tuning = xboxProfile.Tuning;
    TritonHapticReportBuilder.TriggerActuatorIndex = xboxProfile.Tuning.TriggerActuatorIndex;
    var xboxProfileStamp = XboxProfileTimestamp(xboxProfileName);
    log.Info(LogCategory.Session, $"Xbox button mapping: profile '{xboxProfileName}'");

    static DateTime XboxProfileTimestamp(string name)
    {
        try
        {
            var path = XboxProfile.PathFor(name);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    var profileName = ReadOptionValue(args, "--profile");
    Sc2XboxedProfileSettings? loadedSettings = null;

    if (!string.IsNullOrEmpty(profileName))
    {
        var profileResult = ProfileMapper.LoadDetailed(profileName);
        loadedSettings = profileResult.Settings;
        log.WriteBlock(LogLevel.Info, LogCategory.Session, "profile", SessionReport.Profile(profileName, profileResult));
    }

    var effectiveSettings = loadedSettings ?? Sc2XboxedProfileSettings.Default;
    log.WriteBlock(LogLevel.Info, LogCategory.Session, "effective settings", SessionReport.EffectiveSettings(effectiveSettings));
    log.WriteBlock(LogLevel.Info, LogCategory.Session, "overlay keyboard", SessionReport.OverlayKeyboard(OskSettings.Load()));

    var profileMapper = loadedSettings is not null ? new ProfileMapper(loadedSettings) : new ProfileMapper();

    // Applying settings must not require a restart, so the profile file is watched and reloaded live.
    using var profileWatcher = string.IsNullOrEmpty(profileName)
        ? null
        : new ProfileFileWatcher(profileName, message => log.Warn(LogCategory.Session, message));
    var padSender = new PadDataSender();
    padSender.Start();
    log.Info(LogCategory.Pipe, "PadData pipe server started (SteamXBox_OskPad).");
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

    DLog($"Initial mode: {initialMode}");
    DLog($"Switch buttons: {switchButtons}");
    DLog($"Mode switch enabled: {enableModeSwitch}");

    var mapper = loadedSettings is not null ? new DefaultSteamControllerMapper(loadedSettings) : new DefaultSteamControllerMapper();

    // Rebuilds both mappers from disk, carrying over the overlay state so a reload cannot strand the
    // keyboard open with nothing driving it.
    void ReloadProfile()
    {
        if (string.IsNullOrEmpty(profileName)) return;

        var reloaded = ProfileMapper.LoadDetailed(profileName);
        var wasOskActive = profileMapper.OskActive;
        var wasDaisywheel = profileMapper.DaisywheelActive;

        profileMapper = new ProfileMapper(reloaded.Settings)
        {
            OskActive = wasOskActive,
            DaisywheelActive = wasDaisywheel,
        };
        mapper = new DefaultSteamControllerMapper(reloaded.Settings);

        log.Info(LogCategory.Session, $"*** PROFILE RELOADED from {reloaded.FilePath} ***");
        log.WriteBlock(LogLevel.Info, LogCategory.Session, "effective settings (reloaded)",
            SessionReport.EffectiveSettings(reloaded.Settings));
        Console.WriteLine("Profile reloaded.");
    }

    DLog("Creating ViGEm virtual gamepad...");
    await using var gamepad = new ViGEmXbox360Sink();
    await using var haptics = new TritonHapticSink(
        new SteamHidDiscovery(),
        new TritonHapticReportBuilder(),
        message => log.Info(LogCategory.Haptics, message));
    var rumbleMapper = new XboxRumbleToSteamHapticsMapper { Tuning = xboxProfile.Tuning };

    gamepad.RumbleReceived += (_, rumble) =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await haptics.SubmitAsync(rumbleMapper.Map(rumble), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
            {
                DLog($"Rumble haptics disabled after error: {exception.Message}");
            }
        });
    };

    // The overlay keyboard process asks for haptics over a pipe rather than opening its own
    // HID stream, so this sink stays the single writer for the device.
    await using var hapticRequests = new HapticRequestReceiver(
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
        },
        message => log.Debug(LogCategory.Pipe, message));
    hapticRequests.Start();
    log.Info(LogCategory.Pipe, "Haptic request pipe server started (SteamXBox_OskHaptic).");

    await gamepad.ConnectAsync(cancellation.Token);
    DLog("Virtual Xbox 360 controller connected.");
    Console.WriteLine("Virtual Xbox 360 controller connected.");
    Console.WriteLine(enableModeSwitch
        ? $"Mode switch enabled. Current mode: {modeSwitcher.CurrentMode}."
        : $"Mode switch disabled. Current mode: {modeSwitcher.CurrentMode}.");
    Console.WriteLine($"Mode switch button(s): {switchButtons}");
    Console.WriteLine("Press Ctrl+C to stop.");

    var lastStatus = DateTimeOffset.UtcNow;
    var lastScrollTick = DateTimeOffset.MinValue;

    /// <summary>Cursor travel accumulated since the last motion tick, in pixels.</summary>
    var cursorTravel = 0.0;

    // Menu + View held together for three seconds powers the controller off. Both are low-traffic
    // buttons, and the detector requires them down simultaneously and rearms only on a full release,
    // so a game that uses Start and Back cannot stumble into it.
    const SteamControllerButtons PowerOffChord =
        SteamControllerButtons.Menu | SteamControllerButtons.View;
    var powerOffChord = new ButtonChordDetector(PowerOffChord, TimeSpan.FromSeconds(3));
    var powerOffGate = new ChordButtonGate(PowerOffChord);
    var powerOffRequested = false;

    // Start the overlay resident and hidden now, while nobody is waiting on it, rather than paying
    // its four-second cold start on the first toggle.
    OskPrewarm.Start(AppContext.BaseDirectory, DLog);

    // Ownership arbitration: Steam and SteamXBox cannot drive the controller at the same time.
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

    // If Steam is already up when we start, stand down before touching the device at all.
    steamWatcher.Poll(DateTimeOffset.UtcNow);
    if (steamWatcher.Owner == ControllerOwner.Steam)
    {
        DLog("Steam already running at startup; standing down.");
        Console.WriteLine("Steam is running: SteamXBox is standing by.");
        haptics.Muted = true;
    }

    while (!cancellation.Token.IsCancellationRequested)
    {
        // While Steam owns the controller SteamXBox holds nothing open: no HID stream, no virtual
        // pad, no haptics. Wait here until Steam goes away.
        if (steamWatcher.Owner == ControllerOwner.Steam)
        {
            profileMapper.OskActive = false;
            profileMapper.DaisywheelActive = false;
            profileMapper.Reset();
            mapper.ResetTransientState();

            if (!await WaitWhileSteamOwnsAsync(steamWatcher, gamepad, haptics, DLog, cancellation.Token))
            {
                break;
            }

            DLog("Steam gone; reclaiming the controller.");
            Console.WriteLine("Steam closed: SteamXBox is taking over.");
        }

        TritonSteamControllerSource? source = null;
        try
        {
            DLog("Opening HID device...");
            LogHidEnumeration(DLog);
            source = new TritonSteamControllerSource(
                new SteamHidDiscovery(DLog),
                new TritonInputReportParser(),
                readTimeoutMs: 20,
                manageNativeLayer: true,
                initialNativeLayerEnabled: false,
                log: DLog);
            DLog("HID device opened.");

            await foreach (var incomingFrame in source.ReadFramesAsync(cancellation.Token).WithCancellation(cancellation.Token))
            {
                // Local copy: the power-off chord strips its own buttons before the mappers see the
                // frame, and a foreach variable cannot be reassigned.
                var state = incomingFrame;

                // Always cheap: the ring buffer keeps context in memory and is only written to the log
                // when an event asks for it. The unconditional per-frame write this replaces produced
                // megabytes per minute and buried everything else.
                frameBuffer.Add(state);
                counters.Frame(
                    state.RightPad.IsTouched || state.RightPad.IsPressed,
                    state.LeftPad.IsTouched || state.LeftPad.IsPressed);

                if (log.IsEnabled(LogLevel.Trace, LogCategory.Frame))
                {
                    log.Trace(LogCategory.Frame,
                        $"btn={state.Buttons} ls=({state.LeftStick.X:F3},{state.LeftStick.Y:F3}) rs=({state.RightStick.X:F3},{state.RightStick.Y:F3}) lt={state.LeftTrigger:F3} rt={state.RightTrigger:F3} lp=({state.LeftPad.X:F3},{state.LeftPad.Y:F3} t={state.LeftPad.IsTouched} c={state.LeftPad.IsPressed}) rp=({state.RightPad.X:F3},{state.RightPad.Y:F3} t={state.RightPad.IsTouched} c={state.RightPad.IsPressed})");
                }

                // The detector reads the frame as it arrived; the mappers read it through the gate.
                // Masking the chord only once both buttons were down was too late — two fingers never
                // land on the same frame, so the first button had already fired its own action.
                var frameTime = DateTimeOffset.UtcNow;
                var chordComplete = powerOffChord.Update(state.Buttons, frameTime);
                var chordEngaged = chordComplete || (state.Buttons & PowerOffChord) == PowerOffChord;

                state = state with { Buttons = powerOffGate.Filter(state.Buttons, frameTime, chordEngaged) };

                if (chordComplete)
                {
                    DumpFrameContext("power-off chord held");
                    log.Info(LogCategory.Session, "*** Power-off chord (Menu + View, 3s) ***");
                    Console.WriteLine("Powering the controller off.");

                    // Neutral first: the game must not be left holding Start and Back down.
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);

                    try
                    {
                        var sent = source.SendPowerOff();
                        log.Info(LogCategory.Session,
                            sent
                                ? "Power-off feature report sent."
                                : "Power-off skipped: no open HID stream.");
                    }
                    catch (Exception exception)
                    {
                        log.Warn(LogCategory.Session,
                            $"Power-off report rejected by the device: {exception.GetType().Name}: {exception.Message}");
                    }

                    // Give the firmware a moment to act before the process tears the stream down.
                    await Task.Delay(400, CancellationToken.None);

                    // Stand by rather than exit. Switching the controller off is not "I am done with
                    // SteamXBox": the user expects it to be picked up again when it comes back on,
                    // and a process that has exited cannot do that.
                    powerOffRequested = true;
                    break;
                }

                if (enableModeSwitch && modeSwitcher.Update(state))
                {
                    // An explicit toggle beats automatic switching for the app in front.
                    foregroundArbiter?.SuspendForForegroundApp();
                    DumpFrameContext($"manual mode switch -> {modeSwitcher.CurrentMode}");
                    log.Info(LogCategory.Mode, $"*** MODE SWITCH -> {modeSwitcher.CurrentMode} (manual) ***");
                    mapper.ResetTransientState();
                    profileMapper.Reset();
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);
                    Console.WriteLine($"Mode switched to {modeSwitcher.CurrentMode}.");
                }

                if (modeSwitcher.SteamLaunchRequested)
                {
                    DLog("*** Steam launch requested ***");
                    // Hand over before Steam is observable: the process takes seconds to appear and
                    // SteamXBox must not still be writing to the device meanwhile.
                    steamWatcher.HandOverToSteam(DateTimeOffset.UtcNow);
                    InputHelper.LaunchSteam();
                    Console.WriteLine("Launching Steam, controller handed over.");
                    break;
                }

                if (modeSwitcher.SteamKillRequested)
                {
                    DLog("*** Steam kill requested ***");
                    InputHelper.KillProcess(SteamPresenceWatcher.SteamProcessName);
                    steamWatcher.TakeOwnership();
                    DLog("Steam killed. Breaking source loop for fresh reconnection...");
                    Console.WriteLine("Steam killed, reconnecting controller...");
                    break;
                }

                if (profileWatcher?.TryConsumeChange() == true)
                {
                    ReloadProfile();
                }

                // The Xbox mapping is written straight to disk by the editor, so a rebinding takes
                // effect without restarting. Comparing a timestamp costs nothing next to a HID read.
                var stamp = XboxProfileTimestamp(xboxProfileName);
                if (stamp != xboxProfileStamp)
                {
                    xboxProfileStamp = stamp;
                    var reloadedXbox = XboxProfile.Load(xboxProfileName);
                    DefaultSteamControllerMapper.ButtonMap = reloadedXbox.Map;
                    DefaultSteamControllerMapper.Tuning = reloadedXbox.Tuning;
                    rumbleMapper.Tuning = reloadedXbox.Tuning;
                    TritonHapticReportBuilder.TriggerActuatorIndex = reloadedXbox.Tuning.TriggerActuatorIndex;
                    log.Info(LogCategory.Mapping, $"Xbox button mapping reloaded from '{xboxProfileName}'.");
                }

                // Steam may also have been started from outside SteamXBox entirely.
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
                    log.Info(LogCategory.Mode, $"*** AUTO MODE -> {suggestedMode} (foreground={foregroundArbiter.LastForegroundProcess}) ***");
                    modeSwitcher.SetMode(suggestedMode);
                    mapper.ResetTransientState();
                    profileMapper.Reset();
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);
                    Console.WriteLine($"Mode switched to {suggestedMode} ({foregroundArbiter.LastForegroundProcess}).");
                }

                if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile)
                {
                    var mappedState = modeSwitcher.ConsumeButton(state);
                    profileMapper.Map(mappedState);

                    if (profileMapper.EmittedPixelsX != 0 || profileMapper.EmittedPixelsY != 0)
                        counters.MouseMotion(profileMapper.EmittedPixelsX, profileMapper.EmittedPixelsY);
                    if (profileMapper.WheelNotches > 0)
                        counters.Wheel(profileMapper.WheelNotches);
                    if (profileMapper.PadClicked)
                        counters.PadClick();

                    if (profileMapper.OskActive)
                        padSender.SendPadState(state.RightPad, state.LeftPad, state.Buttons);

                    await source.SetNativeLayerEnabledAsync(false);
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);

                    if (profileMapper.OskToggleRequested)
                    {
                        DumpFrameContext($"OSK toggle (currently active={profileMapper.OskActive})");
                        log.Info(LogCategory.Osk, $"OSK toggle requested. OskActive={profileMapper.OskActive}");

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
                                var overlayPath = Path.Combine(oskDir, "Sc2Xboxed.Osk.exe");
                                DLog($"OSK launch: dir={oskDir} exe={File.Exists(overlayPath)}");
                                if (File.Exists(overlayPath))
                                {
                                    try
                                    {
                                        if (OskPrewarm.IsResident)
                                        {
                                            // Already running and hidden: a signal file is all it takes.
                                            File.WriteAllText(
                                                Path.Combine(oskDir, "osk-show.signal"),
                                                DateTimeOffset.UtcNow.Ticks.ToString());
                                            DLog("OSK overlay shown via the resident instance.");
                                        }
                                        else
                                        {
                                            var psi = new ProcessStartInfo
                                            {
                                                FileName = overlayPath,
                                                UseShellExecute = true
                                            };
                                            var proc = Process.Start(psi);
                                            DLog($"OSK overlay launched cold: PID={proc?.Id}");
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
                                    var closeSignalPath = Path.Combine(AppContext.BaseDirectory, "osk-close.signal");
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
                                foreach (var p in Process.GetProcessesByName("Sc2Xboxed.Osk"))
                                {
                                    try { p.Kill(entireProcessTree: true); } catch { }
                                }
                            }

                            profileMapper.OskActive = false;
                            profileMapper.DaisywheelActive = false;

                            // Insurance for the kill path, where the overlay gets no chance to release
                            // a latched SHIFT itself and would leave the physical keyboard uppercase.
                            InputHelper.KeyUp(0xA0);

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
                        cursorTravel += Math.Abs(profileMapper.EmittedPixelsX) + Math.Abs(profileMapper.EmittedPixelsY);
                        if (cursorTravel >= rightHaptics.TravelPerTickPixels)
                        {
                            cursorTravel %= rightHaptics.TravelPerTickPixels;
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
                        (hapticNow - lastScrollTick).TotalMilliseconds >= leftHaptics.DetentIntervalMs)
                    {
                        var width = (ushort)Math.Clamp(
                            leftHaptics.PulseWidthUs + profileMapper.WheelNotches * 20,
                            leftHaptics.PulseWidthUs,
                            leftHaptics.PulseWidthUs * 2);
                        cmds.Add(new HapticCommand(
                            HapticActuator.LeftTrackpad, HapticType.Tick, 0, PulseWidthUs: width));
                        lastScrollTick = hapticNow;
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
                    var output = mapper.Map(mappedState);
                    await source.SetNativeLayerEnabledAsync(false);
                    await gamepad.SubmitAsync(output.Gamepad, cancellation.Token);
                }

                var now = DateTimeOffset.UtcNow;
                var sinceStatus = now - lastStatus;
                if (sinceStatus >= TimeSpan.FromSeconds(1))
                {
                    var summary = counters.DrainToLine(
                        sinceStatus,
                        modeSwitcher.CurrentMode.ToString(),
                        steamWatcher.Owner.ToString());

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
            // up, and the chord would never fire again.
            powerOffChord.Reset();
            powerOffGate.Reset();

            if (!await WaitForControllerReturnAsync(gamepad, haptics, DLog, cancellation.Token))
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

            try
            {
                var probeDiscovery = new SteamHidDiscovery(DLog);
                var dev = probeDiscovery.FindPreferredControllerDevice();
                if (dev is not null)
                {
                    DLog($"Controller found on retry {retry + 1}: PID=0x{dev.ProductID:X4} path={dev.DevicePath}");
                    Console.WriteLine($"Controller reconnected: PID=0x{dev.ProductID:X4}");
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
    OskPrewarm.Stop(AppContext.BaseDirectory, DLog);

    log.Info(LogCategory.Session, "Virtual Xbox 360 controller disconnected.");
    Console.WriteLine("Virtual Xbox 360 controller disconnected.");
    await padSender.DisposeAsync();
    log.Info(LogCategory.Session, "=== SteamXBox session end ===");
    // The log is disposed by its using declaration.
}

static ControllerOutputMode ReadInitialOutputMode(string[] args)
{
    return ReadOptionValue(args, "--start-mode")?.ToLowerInvariant() switch
    {
        "xbox" => ControllerOutputMode.Xbox360,
        "xbox360" => ControllerOutputMode.Xbox360,
        "gamepad" => ControllerOutputMode.Xbox360,
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
        Console.WriteLine("No controller found. Stop SteamXBox first, then run this as administrator.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the device. Stop SteamXBox first, then run this as administrator.");
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
/// </remarks>
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
        Console.WriteLine("No controller found. Stop SteamXBox first, then run this as administrator.");
        return;
    }

    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine("Could not open the device. Stop SteamXBox first, then run this as administrator.");
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
    Console.WriteLine("Done. If a trigger responded, set triggerActuatorIndex in the Xbox profile to");
    Console.WriteLine("the LEFT trigger's index; the right one is assumed to be the next index up.");
    Console.WriteLine("The profile lives in %LOCALAPPDATA%\\SteamXBox\\xbox-profiles.");
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
        Console.WriteLine("Could not open the controller. Stop SteamXBox first, it holds the device.");
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

    lines.Add("SteamXBox diagnostic report");
    Section("identity (this process)");
    lines.AddRange(SessionReport.Identity(args));

    Section("executables next to this one");
    foreach (var name in new[] { "SteamXBox.exe", "SteamXBox.Core.exe", "Sc2Xboxed.Osk.exe" })
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
    foreach (var name in new[] { "steam", "SteamXBox", "SteamXBox.Core", "Sc2Xboxed.Osk" })
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
    foreach (var name in new[] { "steamxbox-debug.log", "steamxbox-debug.log.1", "steamxbox-osk-debug.log" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        lines.Add(File.Exists(path)
            ? $"{name,-28} {new FileInfo(path).Length,12:N0} bytes  {new FileInfo(path).LastWriteTime:yyyy-MM-dd HH:mm:ss}"
            : $"{name,-28} absent");
    }

    var report = string.Join(Environment.NewLine, lines);
    Console.WriteLine(report);

    try
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-diag.txt");
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
        ? "No existing SteamXBox process found."
        : $"Stopped {stopped} existing SteamXBox process(es).");

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
    var result = configurator.SetupForSc2Xboxed(appPath);

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
    Console.WriteLine("  diag           Full report: binary versions, drivers, HID, resolved settings,");
    Console.WriteLine("                 running processes. Writes steamxbox-diag.txt. Add --profile NAME.");
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
    Console.WriteLine("  stop           Kill other running SteamXBox instances from the same executable path.");
    Console.WriteLine("  hidhide-setup  Register SteamXBox with HidHide and cloak Valve physical HID devices.");
    Console.WriteLine("  hidhide-status Print HidHide state.");
    Console.WriteLine("  power-off      Switch the controller off. Options: --probe to sweep the variants.");
    Console.WriteLine("  haptic-probe   Sweep haptic actuator indices to find out which exist.");
    Console.WriteLine("                Options: --max N (default 8)");
    Console.WriteLine("  hidhide-off    Disable HidHide cloaking.");
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
