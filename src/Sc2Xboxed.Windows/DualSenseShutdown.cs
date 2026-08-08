using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Turns a PlayStation 5 controller off.
/// </summary>
/// <remarks>
/// A DualSense has no power-off command. It switches itself off when it loses its Bluetooth link,
/// so turning it off means dropping that link and letting it come back — the pad is gone by then,
/// and the next press of the PS button pairs it again as usual.
///
/// <para>
/// <b>This is the door; <see cref="BluetoothLink"/> is the mechanism, and it stays behind this.</b>
/// <c>BluetoothLink.Cut</c> takes any interface path, so on its own it is a way to disconnect any
/// Bluetooth device on the machine — a headset, a mouse, a keyboard. Named for what it does, it
/// eventually gets used for what it does. Named for the intent and guarded, it cannot be: this
/// accepts a controller identity, refuses anything that is not a DualSense, and takes the interface
/// path from the pad it was given rather than from the caller.
/// </para>
///
/// <para>
/// An earlier attempt sent a HID feature report with a checksum. No such report exists for this;
/// the two tests that failed were checking a mechanism that was never going to work, whatever its
/// arithmetic. <see cref="Sc2Xboxed.Core.Input.DualSenseOutputReport"/> keeps its checksum, because
/// rumble genuinely does travel as a HID report.
/// </para>
/// </remarks>
public static class DualSenseShutdown
{
    /// <summary>
    /// Switches off one DualSense.
    /// </summary>
    /// <param name="controller">
    /// The pad to switch off. Anything that is not a DualSense is refused: this is deliberately not
    /// a way to disconnect Bluetooth devices in general.
    /// </param>
    /// <param name="interfacePath">The HID interface of that same pad, as discovery found it.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <returns>True when the link was dropped.</returns>
    public static bool PowerOff(
        ControllerIdentity controller,
        string interfacePath,
        Action<string>? log = null)
    {
        if (controller.Kind != ControllerKind.DualSense)
        {
            log?.Invoke($"power off refused: {controller.DisplayName} is not a DualSense.");
            return false;
        }

        // A wired pad is powered by the cable: cutting a link it does not use would do nothing, and
        // saying so is better than a silent no-op the user reads as a broken button.
        if (!controller.Id.StartsWith("bt:", StringComparison.Ordinal))
        {
            log?.Invoke($"power off skipped: {controller.DisplayName} is not on Bluetooth.");
            return false;
        }

        log?.Invoke($"powering off {controller.DisplayName} by dropping its link.");

        return BluetoothLink.Cut(interfacePath, log);
    }
}
