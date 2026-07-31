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
- fractional wheel units are accumulated, so slow scrolling still moves instead of truncating to zero;
- releasing mid-flick keeps scrolling and decays exponentially; resting the finger cancels the throw;
- no virtual Xbox axis is produced by this pad.

Right touchpad:

- touched movement emits mouse movement;
- release keeps the movement velocity and decays it exponentially;
- the velocity feeding that throw is smoothed (`VelocitySmoothing`), because a single frame's delta at
  HID report rate is noisy enough that the throw would follow the last frame rather than the gesture;
- the motion dead zone is applied to the 2D magnitude, not per axis: gating each axis separately turned
  slow diagonal drags into stair steps;
- the behavior is trackball-style, suitable for camera control and desktop pointer motion.

Sub-pixel accumulation: `SendInput` only accepts whole pixels, so `ProfileMapper` carries the
fractional remainder across frames. Truncating each frame independently discarded it every time,
which meant a slow deliberate drag produced sub-pixel deltas and moved the cursor not at all.

### Direction conventions

Pad Y grows **downwards** and Windows mouse `DeltaY` grows **downwards**. The trackball negates Y once
(`pixelsY = -deltaY * PixelsPerPadUnit`) *in addition to* the `InvertY` flag, and the shipped profile
sets `InvertY = true`. Net effect: finger up moves the cursor up, finger right moves it right.
`TouchpadMapperTests` locks both signs so a future change cannot silently invert them.

### Haptics in Profile mode

Pointer motion produces **no** haptic. An earlier version ticked the right pad every 30 ms while the
cursor moved, which reads as a continuous buzz rather than feedback. What is felt instead:

- **pad click** — a click pulse, never rate-limited, since a swallowed click feels like a missed input;
- **scroll detents** — one tick per scroll burst, rate-limited to ~45 ms, with the pulse widening as
  the notch count rises so a fast flick stays distinguishable from a single notch.

Tap detection:

- touchpad samples now carry `Pressure` and `IsPressed`;
- tap detection is time/travel based and stays independent from scroll/trackball motion;
- the HID parser still has to map the real pressure/click fields from captured reports.

## Haptics

The core exposes device-independent `HapticCommand` values for:

- left/right rear rumble actuators;
- left/right trackpad haptic actuators.

The HID module contains an experimental Steam Controller 2026 report builder using Valve VID `0x28DE`, wired controller PID `0x1302`, Bluetooth controller PID `0x1303`, and Steam Puck PID `0x1304`. Haptic sending is intentionally explicit through the console `haptic-test --yes` path until the local HID reports are verified on the target firmware.

Single writer: `TritonHapticSink` serializes every write behind a semaphore, because concurrent
writes to one HID stream interleave and corrupt reports. The overlay keyboard runs in its own
process and therefore does **not** open the device — it sends `HapticCommand` values over the
`SteamXBox_OskHaptic` named pipe (`HapticRequestSender` → `HapticRequestReceiver`) and the core
forwards them to that single sink.

The pulse report carries no gain field on this firmware, so `HapticType.Tick` and
`HapticType.Click` strength is controlled by the pulse on-time via `HapticCommand.PulseWidthUs`.
The overlay derives it from the 0-100 `hapticIntensity` setting.

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

## Controller Ownership

Steam and SteamXBox cannot drive the controller at the same time: both write HID feature reports,
so overlapping ownership means they fight over the device. `ControllerOwner` makes ownership
explicit and `SteamPresenceWatcher` derives it from whether `steam.exe` is running.

Handing over to Steam (`ControllerOwner.Steam`):

- the HID stream is closed, which restores the controller firmware's native layer;
- the virtual Xbox 360 pad is **unplugged** — leaving it connected makes Steam enumerate a phantom
  controller alongside the real one, and games launched from Steam may bind to the phantom;
- haptics are muted and the haptic HID handle is dropped;
- the overlay keyboard is closed;
- SteamXBox then polls once per second and does nothing else.

Taking the controller back (`ControllerOwner.SteamXBox`) happens automatically once Steam is gone,
including when the user closes Steam from Steam's own UI. Two grace periods guard the transition:

- **3 s** after an observed Steam exits, because Steam relaunches itself during updates and
  reclaiming instantly would make ownership flap;
- **25 s** after a launch SteamXBox requested but never saw start, so a slow Steam startup does not
  get the controller pulled back from under it.

Mode switching:

- default start mode is `Xbox360`;
- a short press on the Steam/Guide button launches Steam and hands the controller over;
- Steam + Y kills Steam and takes the controller back immediately;
- the configured switch button (`--switch-button`, default Quick Access) toggles `Xbox360`/`Profile`;
- the Steam/Guide button is consumed by the mode switch and is not sent as Xbox Guide while switching is enabled.

Automatic mode switching mirrors Steam's desktop-versus-per-game configurations: a fullscreen
foreground window selects `Xbox360`, anything else selects `Profile`. A manual toggle suspends
automatic switching for the application currently in front, and automatic switching resumes once
the foreground moves elsewhere. Disable it entirely with `--no-auto-mode`.

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

## Overlay Keyboard

The overlay runs as a separate process (`Sc2Xboxed.Osk.exe`) and cannot work standalone: the core
feeds it touchpad and button state over the `SteamXBox_OskPad` pipe and relays its haptic requests.
Settings live in `%LOCALAPPDATA%\SteamXBox\osk-settings.json`, shared by all three processes, and are
global rather than per-profile.

Two typing modes:

- **Full keyboard** — one absolute cursor per trackpad over a QWERTY grid detected from the active
  Windows layout; a pad click types the highlighted key.
- **Daisywheel** — eight petals of four characters. The **left pad direction** selects the petal
  (dead zone 0.35 from centre), **ABXY** selects the slot, and the **left pad click** toggles shift.
  The eighth petal holds Space, Backspace, Enter and SYM on both pages; SYM swaps letters for digits
  and symbols.

While the daisywheel is up, ABXY do not run their desktop bindings, so the **Menu** button closes
the overlay instead of B. `ProfileMapper.DaisywheelActive` gates that behaviour and the core sets it
from the settings file when it launches the overlay.

Haptics follow Steam's feel: a tick on every key boundary crossed (`hoverHaptics`) rather than only
on keypress, which is what makes typing without looking at the overlay possible.

## Driver Boundaries

The core project does not depend on a specific HID library or virtual gamepad driver. HID parsing, HidHide configuration and ViGEm-compatible output should stay behind runtime adapters.
