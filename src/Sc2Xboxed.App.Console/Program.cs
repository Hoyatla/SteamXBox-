using System.Diagnostics;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;
using Sc2Xboxed.Hid;
using Sc2Xboxed.VirtualGamepad;
using Sc2Xboxed.Windows;

if (args.Length > 0)
{
    await RunCommandAsync(args);
    return;
}

MinimizeConsoleWindow();
await RunXbox360LiveAsync(new[]
{
    "xbox-run",
    "--restart",
    "--switch-button",
    "steam-or-quick-access"
});

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
        _ = NativeMethods.ShowWindow(window, NativeMethods.SwMinimize);
    }
}

static async Task RunCommandAsync(string[] args)
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
            await RunXbox360LiveAsync(args);
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
        new HapticCommand(HapticActuator.LeftTrackpad, 120.0, 0.20, TimeSpan.FromMilliseconds(80)),
        new HapticCommand(HapticActuator.RightTrackpad, 160.0, 0.20, TimeSpan.FromMilliseconds(80))
    });

    await sink.SubmitAsync(frame, CancellationToken.None);
    await Task.Delay(100);
    await sink.SubmitAsync(new HapticOutputFrame(new[]
    {
        HapticCommand.Stop(HapticActuator.LeftTrackpad),
        HapticCommand.Stop(HapticActuator.RightTrackpad)
    }), CancellationToken.None);

    Console.WriteLine("Sent conservative trackpad haptic test reports.");
}

static async Task RunXbox360LiveAsync(string[] args)
{
    if (args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
    {
        StopOtherInstances(waitForExit: true);
    }

    using var cancellation = new CancellationTokenSource();
    var enableRumbleHaptics = !args.Contains("--no-haptics", StringComparer.OrdinalIgnoreCase);
    var disableNativeLayer = !args.Contains("--keep-native-layer", StringComparer.OrdinalIgnoreCase);
    var enableModeSwitch = !args.Contains("--no-mode-switch", StringComparer.OrdinalIgnoreCase);
    var modeSwitcher = new SteamButtonModeSwitcher(
        ReadInitialOutputMode(args),
        ReadModeSwitchButtons(args),
        TimeSpan.FromMilliseconds(350));
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

    var mapper = new DefaultSteamControllerMapper();
    await using var source = new TritonSteamControllerSource(
        new SteamHidDiscovery(),
        new TritonInputReportParser(),
        readTimeoutMs: 20,
        manageNativeLayer: disableNativeLayer,
        initialNativeLayerEnabled: modeSwitcher.CurrentMode == ControllerOutputMode.Native);
    await using var gamepad = new ViGEmXbox360Sink();
    await using var haptics = new TritonHapticSink();
    var rumbleMapper = new XboxRumbleToSteamHapticsMapper();

    if (enableRumbleHaptics)
    {
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
                    Console.WriteLine($"Rumble haptics disabled after error: {exception.Message}");
                }
            });
        };
    }

    await gamepad.ConnectAsync(cancellation.Token);
    Console.WriteLine("Virtual Xbox 360 controller connected.");
    Console.WriteLine(enableRumbleHaptics
        ? "Rumble -> Steam haptics enabled."
        : "Rumble -> Steam haptics disabled.");
    Console.WriteLine(enableModeSwitch
        ? $"Steam button mode switch enabled. Current mode: {modeSwitcher.CurrentMode}."
        : $"Steam button mode switch disabled. Current mode: {modeSwitcher.CurrentMode}.");
    Console.WriteLine($"Mode switch button(s): {ReadModeSwitchButtons(args)}");
    Console.WriteLine(disableNativeLayer
        ? $"Native Steam Controller layer is managed by mode. Current native layer: {(modeSwitcher.CurrentMode == ControllerOutputMode.Native ? "enabled" : "disabled")}."
        : "Native Steam Controller layer: unmanaged and left enabled.");
    Console.WriteLine("Press Ctrl+C to stop.");

    var frameCount = 0;
    var lastStatus = DateTimeOffset.UtcNow;

    try
    {
        await foreach (var state in source.ReadFramesAsync(cancellation.Token).WithCancellation(cancellation.Token))
        {
            if (enableModeSwitch && modeSwitcher.Update(state))
            {
                mapper.ResetTransientState();
                await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);
                await source.SetNativeLayerEnabledAsync(modeSwitcher.CurrentMode == ControllerOutputMode.Native);
                Console.WriteLine($"Mode switched to {modeSwitcher.CurrentMode}.");
            }

            var mappedState = enableModeSwitch ? ConsumeSteamButton(state) : state;
            var output = mapper.Map(mappedState);

            if (modeSwitcher.CurrentMode == ControllerOutputMode.Xbox360)
            {
                await source.SetNativeLayerEnabledAsync(enabled: false);
                await gamepad.SubmitAsync(output.Gamepad, cancellation.Token);
            }
            else
            {
                await source.SetNativeLayerEnabledAsync(enabled: true);
                await gamepad.SubmitAsync(Xbox360Report.Neutral, cancellation.Token);
            }

            frameCount++;

            var now = DateTimeOffset.UtcNow;
            if (now - lastStatus >= TimeSpan.FromSeconds(1))
            {
                Console.WriteLine(
                    $"frames={frameCount} mode={modeSwitcher.CurrentMode} buttons={output.Gamepad.Buttons} " +
                    $"lt={output.Gamepad.LeftTrigger} rt={output.Gamepad.RightTrigger} " +
                    $"ls=({output.Gamepad.LeftThumbX},{output.Gamepad.LeftThumbY}) " +
                    $"rs=({output.Gamepad.RightThumbX},{output.Gamepad.RightThumbY}) " +
                    $"tapL={output.LeftPadTap.WasTapped} tapR={output.RightPadTap.WasTapped}");
                frameCount = 0;
                lastStatus = now;
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Normal shutdown path.
    }

    Console.WriteLine("Virtual Xbox 360 controller disconnected.");
}

static SteamControllerState ConsumeSteamButton(SteamControllerState state)
{
    return state with
    {
        Buttons = state.Buttons & ~SteamControllerButtons.Steam
    };
}

static ControllerOutputMode ReadInitialOutputMode(string[] args)
{
    return ReadOptionValue(args, "--start-mode")?.ToLowerInvariant() switch
    {
        "keyboard" => ControllerOutputMode.Native,
        "keyboard-mouse" => ControllerOutputMode.Native,
        "native" => ControllerOutputMode.Native,
        "mouse" => ControllerOutputMode.Native,
        "desktop" => ControllerOutputMode.Native,
        "xbox" => ControllerOutputMode.Xbox360,
        "xbox360" => ControllerOutputMode.Xbox360,
        "gamepad" => ControllerOutputMode.Xbox360,
        _ => ControllerOutputMode.Xbox360
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
        _ => SteamControllerButtons.Steam
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

static void PrintUsage()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  hid-list       List Valve HID interfaces visible to HidSharp.");
    Console.WriteLine("  hid-probe      Capture raw input reports for 3 seconds.");
    Console.WriteLine("  haptic-test    Send low-power Steam Controller 2026 trackpad haptic reports; requires --yes.");
    Console.WriteLine("  xbox-run       Stream Steam Controller input to a virtual Xbox 360 controller.");
    Console.WriteLine("                Options: --seconds N, --no-haptics, --restart");
    Console.WriteLine("                         --start-mode xbox360|native");
    Console.WriteLine("                         --no-mode-switch");
    Console.WriteLine("                         --switch-button steam|quick-access|steam-or-quick-access");
    Console.WriteLine("                         --keep-native-layer");
    Console.WriteLine("  stop           Kill other running SteamXBox instances from the same executable path.");
    Console.WriteLine("  hidhide-setup  Register SteamXBox with HidHide and cloak Valve physical HID devices.");
    Console.WriteLine("  hidhide-status Print HidHide state.");
    Console.WriteLine("  hidhide-off    Disable HidHide cloaking.");
    Console.WriteLine("  help           Print this help.");
    Console.WriteLine("  sanity         Run a static mapping sanity check.");
}

internal static partial class NativeMethods
{
    public const int SwMinimize = 6;

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr windowHandle, int command);
}
