using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Mapping;
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

static async Task RunXbox360LiveAsync(string[] args, Action<string>? debugLog = null)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
    StreamWriter? logFile = debugLog is not null ? null : new StreamWriter(logPath, append: false) { AutoFlush = true };
    var DLog = debugLog ?? ((string msg) =>
    {
        var line = $"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {msg}";
        logFile!.WriteLine(line);
    });

    DLog($"=== SteamXBox started ===");
    DLog($"Args: {string.Join(" ", args)}");
    DLog($"Log file: {logPath}");

    if (args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
    {
        DLog("Stopping other instances...");
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
    var profileMapper = new ProfileMapper();
    var profileName = ReadOptionValue(args, "--profile");
    if (!string.IsNullOrEmpty(profileName))
    {
        var settings = ProfileMapper.LoadFromProfilesDirectory(profileName);
        profileMapper = new ProfileMapper(settings);
        DLog($"Loaded profile '{profileName}': sensitivity={settings.RightPadTrackball.PixelsPerPadUnit}, invertY={settings.RightPadTrackball.InvertY}, deadzone={settings.StickDeadZone}");
    }
    var padSender = new PadDataSender();
    padSender.Start();
    DLog("PadData pipe server started (SteamXBox_OskPad).");
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

    var mapper = new DefaultSteamControllerMapper();

    DLog("Creating ViGEm virtual gamepad...");
    await using var gamepad = new ViGEmXbox360Sink();
    await using var haptics = new TritonHapticSink();
    var rumbleMapper = new XboxRumbleToSteamHapticsMapper();

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

    await gamepad.ConnectAsync(cancellation.Token);
    DLog("Virtual Xbox 360 controller connected.");
    Console.WriteLine("Virtual Xbox 360 controller connected.");
    Console.WriteLine(enableModeSwitch
        ? $"Mode switch enabled. Current mode: {modeSwitcher.CurrentMode}."
        : $"Mode switch disabled. Current mode: {modeSwitcher.CurrentMode}.");
    Console.WriteLine($"Mode switch button(s): {switchButtons}");
    Console.WriteLine("Press Ctrl+C to stop.");

    var frameCount = 0;
    var lastStatus = DateTimeOffset.UtcNow;
    var lastHapticTime = DateTimeOffset.MinValue;
    const double HapticIntervalMs = 30;

    while (!cancellation.Token.IsCancellationRequested)
    {
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
                initialNativeLayerEnabled: modeSwitcher.WantsNativeLayer,
                log: DLog);
            DLog($"HID device opened. nativeLayer={modeSwitcher.WantsNativeLayer}");

            await foreach (var state in source.ReadFramesAsync(cancellation.Token).WithCancellation(cancellation.Token))
            {
                DLog($"Frame: btn={state.Buttons} ls=({state.LeftStick.X:F3},{state.LeftStick.Y:F3}) rs=({state.RightStick.X:F3},{state.RightStick.Y:F3}) lt={state.LeftTrigger:F3} rt={state.RightTrigger:F3} lp=({state.LeftPad.X:F3},{state.LeftPad.Y:F3} t={state.LeftPad.IsTouched} c={state.LeftPad.IsPressed}) rp=({state.RightPad.X:F3},{state.RightPad.Y:F3} t={state.RightPad.IsTouched} c={state.RightPad.IsPressed})");

                if (enableModeSwitch && modeSwitcher.Update(state))
                {
                    DLog($"*** MODE SWITCH -> {modeSwitcher.CurrentMode} ***");
                    mapper.ResetTransientState();
                    profileMapper.Reset();
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);
                    Console.WriteLine($"Mode switched to {modeSwitcher.CurrentMode}.");
                }

                if (modeSwitcher.SteamLaunchRequested)
                {
                    DLog("*** Steam launch requested ***");
                    InputHelper.LaunchSteam();
                    await source.SetNativeLayerEnabledAsync(true);
                    Console.WriteLine("Steam launched, controller returned to native mode.");
                }
                else if (modeSwitcher.SteamKillRequested)
                {
                    DLog("*** Steam kill requested ***");
                    InputHelper.KillProcess("steam");
                    DLog("Steam killed. Breaking source loop for fresh reconnection...");
                    Console.WriteLine("Steam killed, reconnecting controller...");
                    break;
                }
                else if (modeSwitcher.WantsNativeLayer)
                {
                    await source.SetNativeLayerEnabledAsync(true);
                }

                if (modeSwitcher.CurrentMode == ControllerOutputMode.Profile && !modeSwitcher.WantsNativeLayer)
                {
                    var mappedState = modeSwitcher.ConsumeButton(state);
                    profileMapper.Map(mappedState);

                    if (profileMapper.OskActive)
                        padSender.SendPadState(state.RightPad, state.LeftPad);

                    await source.SetNativeLayerEnabledAsync(false);
                    await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);

                    if (profileMapper.OskToggleRequested)
                    {
                        DLog($"OSK toggle requested. OskActive={profileMapper.OskActive}");

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
                                        var psi = new ProcessStartInfo
                                        {
                                            FileName = overlayPath,
                                            UseShellExecute = true
                                        };
                                        var proc = Process.Start(psi);
                                        DLog($"OSK overlay launched: PID={proc?.Id}");
                                        profileMapper.OskActive = true;
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
                            DLog("OSK overlay stopped.");
                        }
                    }

                    var hapticNow = DateTimeOffset.UtcNow;
                    bool shouldPulse = (hapticNow - lastHapticTime).TotalMilliseconds >= HapticIntervalMs;
                    if (shouldPulse)
                    {
                        var cmds = new List<HapticCommand>();
                        if (profileMapper.CursorMoved)
                            cmds.Add(new HapticCommand(HapticActuator.RightTrackpad, HapticType.Tick, -12));
                        if (profileMapper.Scrolled)
                            cmds.Add(new HapticCommand(HapticActuator.LeftTrackpad, HapticType.Tick, -12));
                        if (profileMapper.PadClicked)
                            cmds.Add(new HapticCommand(HapticActuator.RightTrackpad, HapticType.Click, -8));
                        if (cmds.Count > 0)
                        {
                            try
                            {
                                await haptics.SubmitAsync(new HapticOutputFrame(cmds), CancellationToken.None);
                                lastHapticTime = hapticNow;
                            }
                            catch { }
                        }
                    }
                }
                else if (!modeSwitcher.WantsNativeLayer)
                {
                    var mappedState = enableModeSwitch ? modeSwitcher.ConsumeButton(state) : state;
                    var output = mapper.Map(mappedState);
                    await source.SetNativeLayerEnabledAsync(false);
                    await gamepad.SubmitAsync(output.Gamepad, cancellation.Token);
                }

                frameCount++;

                var now = DateTimeOffset.UtcNow;
                if (now - lastStatus >= TimeSpan.FromSeconds(1))
                {
                    Console.WriteLine(
                        $"frames={frameCount} mode={modeSwitcher.CurrentMode} native={modeSwitcher.WantsNativeLayer}");
                    frameCount = 0;
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

    DLog("Virtual Xbox 360 controller disconnected.");
    Console.WriteLine("Virtual Xbox 360 controller disconnected.");
    await padSender.DisposeAsync();
    DLog("PadSender disposed.");
    logFile?.Dispose();
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
    Console.WriteLine("  haptic-test    Send low-power Steam Controller 2026 trackpad haptic reports; requires --yes.");
    Console.WriteLine("  xbox-run       Stream Steam Controller input to a virtual Xbox 360 controller.");
    Console.WriteLine("                Options: --seconds N, --no-haptics, --restart");
    Console.WriteLine("                         --start-mode xbox360|profile");
    Console.WriteLine("                         --no-mode-switch");
    Console.WriteLine("                         --switch-button steam|quick-access|steam-or-quick-access");
    Console.WriteLine("                         --debug  Log all activity to console + steamxbox-debug.log");
    Console.WriteLine("  stop           Kill other running SteamXBox instances from the same executable path.");
    Console.WriteLine("  hidhide-setup  Register SteamXBox with HidHide and cloak Valve physical HID devices.");
    Console.WriteLine("  hidhide-status Print HidHide state.");
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
