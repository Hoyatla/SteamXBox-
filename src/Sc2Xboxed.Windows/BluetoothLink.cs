using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Cuts the Bluetooth connection Windows holds to a DualSense so the controller powers itself off.
/// </summary>
/// <remarks>
/// Windows delivers no working power-off feature report over Bluetooth HID — HidD_SetFeature is
/// refused outright on some machines — so the pad has to be switched off the way the controller
/// already knows how: a dropped link. A DualSense that loses its Bluetooth connection powers
/// itself off, so cutting the link is the power-off.
///
/// <para>
/// The primary cut is <c>IOCTL_BTH_DISCONNECT_DEVICE</c>, sent to the local radio holding the
/// link. The remote address is the pad's, which is read out of its device-tree ancestry: the
/// BTHENUM node Windows creates for a DualSense carries the controller address in its instance id.
/// Every radio is tried, so a machine with more than one adapter still finds the one that holds
/// the link.
/// </para>
///
/// <para>
/// If no radio accepts the disconnect, the fallback disables and re-enables the pad's own PnP
/// device nodes, which drops the link the same way. The radio and everything above the pad's own
/// nodes are never touched.
/// </para>
/// </remarks>
public static class BluetoothLink
{
    private const uint IoctlBthDisconnectDevice = 0x41000C;
    private const int DisableBetweenMs = 4000;
    private const int CrSuccess = 0;

    /// <summary>
    /// Drops the Bluetooth connection of the controller behind a HID interface path.
    /// </summary>
    /// <returns>True when a link was actually cut; the controller should power itself off.</returns>
    public static bool Cut(string interfacePath, Action<string>? log)
    {
        var address = MacFromAncestry(interfacePath, log);

        if (address is not null && DisconnectOverRadios(address.Value, log))
        {
            return true;
        }

        return CutViaPnp(interfacePath, log);
    }

    /// <summary>
    /// The controller's Bluetooth address from its device tree, if one of its ancestors carries it.
    /// </summary>
    /// <remarks>
    /// The BTHENUM node Windows creates for a Bluetooth DualSense stores the address in the last
    /// instance-id segment, written as the little-endian <c>BTH_ADDR</c> the driver accepts: the
    /// twelve hex characters are already the value <c>IOCTL_BTH_DISCONNECT_DEVICE</c> expects.
    /// </remarks>
    private static long? MacFromAncestry(string interfacePath, Action<string>? log)
    {
        var mac = new Regex(@"(?:DEV_|&)([0-9A-Fa-f]{12})(?![0-9A-Fa-f])");

        foreach (var instanceId in DeviceTree.AncestorInstanceIds(interfacePath))
        {
            var match = mac.Match(instanceId);
            if (match.Success)
            {
                log?.Invoke($"DualSense Bluetooth address {match.Groups[1].Value} read from {instanceId}.");
                return Convert.ToInt64(match.Groups[1].Value, 16);
            }
        }

        log?.Invoke("no Bluetooth address in the DualSense device tree.");
        return null;
    }

    private static bool DisconnectOverRadios(long address, Action<string>? log)
    {
        var findParams = new BluetoothFindRadioParams { DwSize = (uint)Marshal.SizeOf<BluetoothFindRadioParams>() };

        var findHandle = BluetoothFindFirstRadio(ref findParams, out var radio);
        if (findHandle == IntPtr.Zero)
        {
            log?.Invoke($"no Bluetooth radio found (error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        var handles = new List<IntPtr>();

        try
        {
            while (radio != IntPtr.Zero)
            {
                handles.Add(radio);

                var radioName = RadioName(radio);
                var bytesReturned = 0;

                if (DeviceIoControl(
                        radio, IoctlBthDisconnectDevice, ref address, 8,
                        IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero))
                {
                    log?.Invoke($"Bluetooth link to the DualSense cut via radio \"{radioName}\".");
                    return true;
                }

                var error = Marshal.GetLastWin32Error();
                log?.Invoke(
                    $"radio \"{radioName}\" refused the disconnect (error {error}: {new Win32Exception(error).Message}); "
                    + "trying the next radio.");

                if (BluetoothFindNextRadio(findHandle, out radio) == IntPtr.Zero)
                {
                    break;
                }
            }
        }
        finally
        {
            BluetoothFindRadioClose(findHandle);

            foreach (var handle in handles)
            {
                CloseHandle(handle);
            }
        }

        log?.Invoke("no Bluetooth radio accepted the disconnect.");
        return false;
    }

    private static string RadioName(IntPtr radio)
    {
        var info = new BluetoothRadioInfo { DwSize = (uint)Marshal.SizeOf<BluetoothRadioInfo>() };

        return BluetoothGetRadioInfo(radio, ref info) ? info.SzName : "";
    }

    /// <summary>
    /// Drops the link by disabling the pad's own PnP nodes and bringing them back after a moment.
    /// </summary>
    /// <remarks>
    /// The first two ancestors of the HID interface are the pad's HID node and its BTHENUM service
    /// node; both are specific to this controller and neither is the radio, which must stay up.
    /// </remarks>
    private static bool CutViaPnp(string interfacePath, Action<string>? log)
    {
        var ids = DeviceTree.AncestorInstanceIds(interfacePath).Take(2).ToList();
        if (ids.Count == 0)
        {
            log?.Invoke("no DualSense device node found to cut the Bluetooth link.");
            return false;
        }

        var nodes = new List<uint>();
        foreach (var id in ids)
        {
            if (CM_Locate_DevNodeW(out var devInst, id, 0) == CrSuccess)
            {
                nodes.Add(devInst);
                log?.Invoke($"disabling {id} to cut the Bluetooth link.");
            }
            else
            {
                log?.Invoke($"could not locate {id} to disable it.");
            }
        }

        var disabled = nodes.Count(node => CM_Disable_DevNode(node, 0) == CrSuccess);

        log?.Invoke($"{disabled}/{nodes.Count} DualSense nodes disabled; waiting {DisableBetweenMs}ms for the controller to power off.");

        Thread.Sleep(DisableBetweenMs);

        foreach (var node in nodes)
        {
            var result = CM_Enable_DevNode(node, 0);
            log?.Invoke($"re-enabled a DualSense node (hr=0x{result:X}).");
        }

        return disabled > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public uint DwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothRadioInfo
    {
        public uint DwSize;
        public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string SzName;
        public uint ClassOfDevice;
        public ushort LmpSubversion;
        public ushort Manufacturer;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio([In] ref BluetoothFindRadioParams findParams, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindNextRadio(IntPtr findHandle, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothGetRadioInfo(IntPtr radioHandle, [In, Out] ref BluetoothRadioInfo radioInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint ioControlCode,
        ref long input,
        int inputSize,
        IntPtr output,
        int outputSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);
}
