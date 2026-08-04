using System.Runtime.InteropServices;
using HidSharp;

namespace SteamXBox.Shell.Devices;

/// <summary>A gamepad Windows can see, other than the Steam Controller.</summary>
/// <param name="Name">Product name as the device reports it.</param>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
public sealed record RivalController(string Name, int VendorId, int ProductId);

/// <summary>What is competing with SteamXBox for the game's attention.</summary>
/// <param name="OccupiedSlots">Which of the four XInput slots are taken.</param>
/// <param name="Rivals">Other gamepads present on the machine.</param>
/// <param name="SteamControllerPresent">Whether a Valve controller is enumerated at all.</param>
/// <param name="SteamControllerUsable">Whether that controller can actually be opened.</param>
public sealed record ControllerSurvey(
    IReadOnlyList<bool> OccupiedSlots,
    IReadOnlyList<RivalController> Rivals,
    bool SteamControllerPresent,
    bool SteamControllerUsable);

/// <summary>
/// Reports the gamepads Windows sees, and which XInput slots are taken.
/// </summary>
/// <remarks>
/// This exists because of a failure that cost several rounds to find. Xbox mode did nothing in a
/// game while the core was provably healthy — virtual pad connected, mapping loaded, reports
/// submitted, no warnings anywhere. The cause was outside SteamXBox entirely: a real Xbox
/// controller was paired over Bluetooth and held XInput slot 0, the slot nearly every game reads
/// for player one. The virtual pad landed in a later slot and was never read.
///
/// SteamXBox cannot fix that — slot order is Windows', assigned as devices connect. What it can do
/// is say so, instead of leaving the user to conclude the software is broken.
/// </remarks>
public static class ControllerInventory
{
    /// <summary>XInput exposes four slots, assigned in connection order.</summary>
    public const int SlotCount = 4;

    private const int ValveVendorId = 0x28DE;
    private const uint ErrorSuccess = 0;

    public static ControllerSurvey Survey()
    {
        var slots = new bool[SlotCount];
        for (uint i = 0; i < SlotCount; i++)
        {
            slots[i] = IsSlotOccupied(i);
        }

        var rivals = new List<RivalController>();
        var steamPresent = false;
        var steamUsable = false;

        try
        {
            foreach (var device in DeviceList.Local.GetHidDevices())
            {
                if (device.VendorID == ValveVendorId)
                {
                    steamPresent = true;

                    // Enumerated but unopenable is the signature of a device Windows failed to
                    // start — the controller shows up with a Code 10 and can send nothing to
                    // anyone. Worth separating from "not connected", which looks the same to a user.
                    //
                    // The stream is disposed at once: this asks whether the device can be opened,
                    // it does not intend to keep it. Holding it would take the controller away from
                    // the core, which is the one process that actually needs it.
                    if (device.TryOpen(out var probe))
                    {
                        steamUsable = true;
                        probe.Dispose();
                    }

                    continue;
                }

                if (!LooksLikeGamepad(device))
                {
                    continue;
                }

                rivals.Add(new RivalController(SafeName(device), device.VendorID, device.ProductID));
            }
        }
        catch
        {
            // Enumeration can fail while a device is being attached. An incomplete survey is worth
            // more than no diagnostics at all.
        }

        return new ControllerSurvey(slots, rivals, steamPresent, steamUsable);
    }

    /// <summary>
    /// Whether a device is a gamepad, from its HID usage.
    /// </summary>
    /// <remarks>
    /// Usage page 1 (generic desktop), usage 4 (joystick) or 5 (game pad). Filtering by name would
    /// miss anything not called "controller" and would catch keyboards that are.
    /// </remarks>
    private static bool LooksLikeGamepad(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            return descriptor.DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(usage => usage is 0x00010004 or 0x00010005));
        }
        catch
        {
            // Some devices refuse to hand over their descriptor without being opened. Not a gamepad
            // as far as this survey is concerned.
            return false;
        }
    }

    private static string SafeName(HidDevice device)
    {
        try
        {
            return string.IsNullOrWhiteSpace(device.GetFriendlyName())
                ? $"HID {device.VendorID:X4}:{device.ProductID:X4}"
                : device.GetFriendlyName();
        }
        catch
        {
            return $"HID {device.VendorID:X4}:{device.ProductID:X4}";
        }
    }

    private static bool IsSlotOccupied(uint index)
    {
        try
        {
            return XInputGetState(index, out _) == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    // xinput1_4.dll ships with Windows 8 and later, so no redistributable is involved.
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
}
