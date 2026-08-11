using System.Runtime.InteropServices;
using System.Text;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Finds the durable identity of the pad behind an XInput slot.
/// </summary>
/// <remarks>
/// A slot number is not an identity. <c>xinput-slot:0</c> is the position Windows happened to give a
/// pad this session; unplug it and plug it back, or turn on a second one first, and the number moves
/// to another controller. Settings filed under it follow the position rather than the pad, which is
/// how one controller ends up wearing another's profile.
///
/// <para>
/// XInput itself offers nothing to identify a device — no path, no serial, not even a vendor id
/// through the documented API. Two undocumented pieces are needed, and both are used here in the
/// narrowest way that still answers the question:
/// </para>
/// <list type="number">
/// <item>
/// <c>XInputGetCapabilitiesEx</c>, exported by ordinal 108 with no name, returns the vendor and
/// product id for a slot. That says <i>what</i> the pad is, not which one.
/// </item>
/// <item>
/// The XUSB device interface class lists the pads Windows has. Their instance ids carry the same
/// vendor and product id, and — unlike the slot — a durable node above them.
/// </item>
/// </list>
///
/// <para>
/// The two are matched on vendor and product id, and <b>only when the match is unique</b>. Two
/// identical Xbox pads on the same machine cannot be told apart this way, and guessing between them
/// would file one pad's settings under the other. In that case this returns null and the caller
/// keeps the slot, which is at least honest for the length of one session.
/// </para>
/// </remarks>
public static class XInputDurableIdentity
{
    /// <summary>The XUSB device interface class. Every XInput-compatible pad exposes one.</summary>
    private static readonly Guid XusbInterfaceClass = new("EC87F1E3-C13B-4100-B5F7-8B84D54260CB");

    /// <summary>
    /// The durable key for the pad in <paramref name="slot"/>, or null when it cannot be established.
    /// </summary>
    /// <remarks>
    /// Null rather than a fallback, for the reason <see cref="Sc2Xboxed.Core.Input.DurableControllerKey"/>
    /// gives: a key invented from something non-durable persists, looks right, and quietly attaches
    /// settings to the wrong pad.
    /// </remarks>
    /// <summary>
    /// Whether the pad in <paramref name="slot"/> is real hardware, or null when it cannot be told.
    /// </summary>
    /// <remarks>
    /// This replaces a snapshot of the occupied slots taken before SteamXBox created anything
    /// virtual, whose rule was "a slot that fills afterwards is ours". That rule kept our own ViGEm
    /// pads out, and it also froze the list: a controller plugged in later was classified as ours
    /// and ignored for the rest of the session, which is the whole of "I swapped controllers and the
    /// new one does nothing".
    ///
    /// <para>
    /// The device tree answers it without freezing anything. A real pad hangs off a physical
    /// transport — <c>USB\</c> or <c>BTHENUM\</c> — while a ViGEm pad hangs off its own virtual bus
    /// under <c>ROOT\</c>. Verified against this machine's devices, where every Xbox-compatible pad
    /// present resolved through <c>USB\</c> or <c>BTHENUM\</c>.
    /// </para>
    ///
    /// <para>
    /// Null when the slot cannot be matched to a device — several identical pads, or the ordinal
    /// unavailable. The caller then falls back to the snapshot, which errs towards ignoring a new
    /// pad rather than towards reading our own output back in as input.
    /// </para>
    /// </remarks>
    public static bool? LooksPhysical(int slot, Action<string>? log = null)
    {
        if (DeviceForSlot(slot, log) is not { } device)
        {
            return null;
        }

        foreach (var ancestor in DeviceTree.AncestorInstanceIds(device))
        {
            var id = ancestor.ToUpperInvariant();

            if (id.StartsWith("USB\\", StringComparison.Ordinal)
                || id.StartsWith("BTHENUM\\", StringComparison.Ordinal)
                || id.StartsWith("BTH\\", StringComparison.Ordinal))
            {
                return true;
            }

            if (id.Contains("VIGEM", StringComparison.Ordinal))
            {
                log?.Invoke($"XInput slot {slot}: virtual pad on the ViGEm bus; not read back as input.");
                return false;
            }
        }

        return null;
    }

    public static string? For(int slot, Action<string>? log = null)
        => DeviceForSlot(slot, log) is { } device ? DeviceTree.DurableKeyFor(device, log) : null;

    /// <summary>
    /// The interface path of the real pad on a slot, for hiding it from everything else.
    /// </summary>
    /// <remarks>
    /// Physical pads only, and that guard is the whole reason this is not just
    /// <see cref="DeviceForSlot"/> made public. The pads SteamXBox creates for games are XInput
    /// devices too; hiding one would take away the very thing the game is supposed to see, and the
    /// symptom — "Xbox mode stopped working" — would point nowhere near this line.
    ///
    /// <para>
    /// Null when the slot cannot be resolved, or when it resolves to something that is not certainly
    /// physical. Uncertain means not hidden: failing to hide a pad costs a duplicated button press,
    /// while hiding the wrong device costs the user a controller that works nowhere.
    /// </para>
    /// </remarks>
    public static string? PhysicalInterfacePathFor(int slot, Action<string>? log = null)
        => LooksPhysical(slot, log) == true ? DeviceForSlot(slot, log) : null;

    /// <summary>The XUSB device behind a slot, or null when it cannot be told which.</summary>
    private static string? DeviceForSlot(int slot, Action<string>? log)
    {
        var devices = XusbInterfacePaths(log);

        if (devices.Count == 0)
        {
            log?.Invoke($"XInput slot {slot}: no XUSB device interface present; keeping the slot.");
            return null;
        }

        string? match = null;

        if (TryReadVendorAndProduct(slot, out var vendorId, out var productId, out var why))
        {
            // Instance ids spell it this way: VID_045E&PID_02FD.
            var marker = $"vid_{vendorId:x4}&pid_{productId:x4}";

            var byIds = devices
                .Where(path => path.Replace('#', '\\').ToLowerInvariant().Contains(marker, StringComparison.Ordinal))
                .ToList();

            if (byIds.Count == 1)
            {
                match = byIds[0];
            }
            else
            {
                log?.Invoke(
                    $"XInput slot {slot}: {byIds.Count} XUSB device(s) match {marker}; "
                    + "identical pads cannot be told apart this way.");
            }
        }
        else
        {
            log?.Invoke($"XInput slot {slot}: {why}");
        }

        // Nothing undocumented needed for this one: with a single XUSB device on the machine, there
        // is only one pad it can be, whichever slot is asking. It covers the ordinary case — one
        // Xbox controller — on machines where the unnamed export does not answer.
        if (match is null && devices.Count == 1)
        {
            match = devices[0];
            log?.Invoke($"XInput slot {slot}: one XUSB device present, so it is this pad.");
        }

        if (match is null)
        {
            log?.Invoke(
                $"XInput slot {slot}: {devices.Count} XUSB devices and no way to say which; keeping the slot.");
        }

        return match;
    }

    /// <summary>Vendor and product id for a slot, through the unnamed ordinal 108 export.</summary>
    /// <remarks>
    /// Resolved on demand and allowed to fail: on a machine where the ordinal is missing the pad
    /// keeps working, it simply has no durable identity, and the log says so rather than the source
    /// refusing to open.
    /// </remarks>
    private static bool TryReadVendorAndProduct(int slot, out ushort vendorId, out ushort productId, out string why)
    {
        vendorId = 0;
        productId = 0;
        why = "";

        try
        {
            var getCapabilitiesEx =
                NativeOrdinal.Resolve<XInputGetCapabilitiesExDelegate>("xinput1_4.dll", 108);

            if (getCapabilitiesEx is null)
            {
                why = "xinput1_4.dll exports no ordinal 108.";
                return false;
            }

            var capabilities = default(XInputCapabilitiesEx);
            var result = getCapabilitiesEx(1, (uint)slot, 0, ref capabilities);

            if (result != 0)
            {
                why = $"ordinal 108 returned {result} for this slot.";
                return false;
            }

            vendorId = capabilities.VendorId;
            productId = capabilities.ProductId;

            if (vendorId == 0)
            {
                why = "ordinal 108 answered with no vendor id.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            why = $"ordinal 108 threw {exception.GetType().Name}.";
            return false;
        }
    }

    /// <summary>Every XUSB device interface Windows currently exposes.</summary>
    private static IReadOnlyList<string> XusbInterfacePaths(Action<string>? log)
    {
        try
        {
            var guid = XusbInterfaceClass;

            if (CM_Get_Device_Interface_List_SizeW(out var length, ref guid, null, CmGetDeviceInterfaceListPresent)
                != CrSuccess || length <= 1)
            {
                return [];
            }

            var buffer = new char[length];

            if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, (uint)buffer.Length,
                    CmGetDeviceInterfaceListPresent) != CrSuccess)
            {
                return [];
            }

            // A REG_MULTI_SZ: strings back to back, the last one empty.
            return new string(buffer)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        catch (Exception exception)
        {
            log?.Invoke($"enumerating XUSB devices: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }

    private delegate uint XInputGetCapabilitiesExDelegate(
        uint reserved, uint userIndex, uint flags, ref XInputCapabilitiesEx capabilities);

    /// <summary>
    /// The layout ordinal 108 fills in: the documented XINPUT_CAPABILITIES followed by the ids.
    /// </summary>
    /// <remarks>
    /// Only the two ids are read. The rest is present so the structure is the size the export
    /// expects — handing it a short buffer is how an undocumented call corrupts the stack.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputCapabilitiesEx
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;

        public ushort GamepadButtons;
        public byte GamepadLeftTrigger;
        public byte GamepadRightTrigger;
        public short GamepadThumbLX;
        public short GamepadThumbLY;
        public short GamepadThumbRX;
        public short GamepadThumbRY;

        public ushort VibrationLeftMotorSpeed;
        public ushort VibrationRightMotorSpeed;

        public ushort VendorId;
        public ushort ProductId;
        public ushort ProductVersion;
        public ushort Unknown1;
        public uint Unknown2;
    }

    private const int CrSuccess = 0;
    private const uint CmGetDeviceInterfaceListPresent = 0x00000001;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out uint length, ref Guid interfaceClassGuid, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, string? deviceId, char[] buffer, uint bufferLength, uint flags);
}
