# SteamXBox Mapping

## Button Mapping

| Steam Controller 2026 | Xbox 360 output |
| --- | --- |
| L4 | X |
| R4 | Y |
| L5 | A |
| R5 | B |

The default face buttons remain mapped to their Xbox equivalents. The rear buttons intentionally duplicate face-button actions so games only see a standard Xbox 360 controller.

## Touchpads

Left touchpad:

- vertical finger movement emits mouse wheel deltas;
- movement is accumulated so small motions are not lost;
- no virtual Xbox axis is produced by this pad.

Right touchpad:

- touched movement emits mouse movement;
- release keeps the last movement velocity and decays it exponentially;
- the behavior is trackball-style, suitable for camera control and desktop pointer motion.

Tap detection:

- touchpad samples now carry `Pressure` and `IsPressed`;
- tap detection is time/travel based and stays independent from scroll/trackball motion;
- the HID parser still has to map the real pressure/click fields from captured reports.

## Haptics

The core exposes device-independent `HapticCommand` values for:

- left/right rear rumble actuators;
- left/right trackpad haptic actuators.

The HID module contains an experimental Steam Controller 2026 report builder using Valve VID `0x28DE`, wired controller PID `0x1302`, Bluetooth controller PID `0x1303`, and Steam Puck PID `0x1304`. Haptic sending is intentionally explicit through the console `haptic-test --yes` path until the local HID reports are verified on the target firmware.

Local Bluetooth probe result:

- `PID 0x1303`, `col03`, input report length `54`, output report length `64`, feature report length `64`;
- input report `0x45` parses as a Triton controller state report;
- touchpad pressure and click bits are parsed but must be validated while physically touching/pressing each pad.

Debug commands:

```powershell
dotnet run --project src\Sc2Xboxed.App.Console -- hid-list
dotnet run --project src\Sc2Xboxed.App.Console -- hid-probe
dotnet run --project src\Sc2Xboxed.App.Console -- haptic-test --yes
dotnet run --project src\Sc2Xboxed.App.Console -- xbox-run
```

## Xbox 360 Virtual Layer

The virtual Xbox 360 layer is implemented through ViGEmBus and `Nefarius.ViGEm.Client`.

Runtime pipeline:

```text
Steam Controller 2026 BT HID
  -> disable firmware native mouse/keyboard mappings (lizard mode)
  -> TritonInputReportParser
  -> DefaultSteamControllerMapper
  -> ViGEmXbox360Sink
  -> Windows virtual Xbox 360 controller
```

Live command:

```powershell
dotnet run --project src\Sc2Xboxed.App.Console -- xbox-run
```

Options:

```powershell
--seconds N
--no-haptics
--restart
--start-mode xbox360|native
--no-mode-switch
--switch-button steam|quick-access|steam-or-quick-access
--keep-native-layer
```

`xbox-run` also maps Xbox rumble feedback back to Steam Controller haptics unless `--no-haptics` is specified.

Mode switching:

- default start mode is `Xbox360`;
- a short press on the Steam/Guide button toggles between `Xbox360` and `Native`;
- the Steam/Guide button is consumed by the mode switch and is not sent as Xbox Guide while switching is enabled;
- `Native` mode neutralizes the virtual Xbox 360 controller and restores the controller firmware's native Valve mouse/keyboard layer;
- `Xbox360` mode sends the virtual Xbox 360 report and disables the controller firmware's native mouse/keyboard layer through HID feature reports.

To disable the Steam-button switch and send the Steam button as Xbox Guide again:

```powershell
dotnet run --project src\Sc2Xboxed.App.Console -- xbox-run --no-mode-switch
```

SteamXBox no longer injects its own mouse in this switch path. Native mode is the controller's own firmware behavior.

Important: wired Steam Controller exposes physical HID mouse/keyboard interfaces (`VID_28DE&PID_1302&COL01/COL02`). SteamXBox now disables the controller firmware's native mouse/keyboard mappings by default while `xbox-run` is active. HidHide remains useful as a second isolation layer for games that enumerate raw HID devices directly:

```powershell
dotnet run --project src\Sc2Xboxed.App.Console -- hidhide-setup
dotnet run --project src\Sc2Xboxed.App.Console -- hidhide-status
dotnet run --project src\Sc2Xboxed.App.Console -- hidhide-off
```

Process control:

```powershell
dotnet run --project src\Sc2Xboxed.App.Console -- stop
dotnet run --project src\Sc2Xboxed.App.Console -- xbox-run --restart
```

`stop` kills other running SteamXBox processes launched from the same executable path. `xbox-run --restart` performs the same stop step before starting the live Xbox 360 emulation loop.

## Driver Boundaries

The core project does not depend on a specific HID library or virtual gamepad driver. HID parsing, HidHide configuration and ViGEm-compatible output should stay behind runtime adapters.
