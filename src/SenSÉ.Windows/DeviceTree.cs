using System.Runtime.InteropServices;
using System.Text;
using SenSÉ.Core.Input;

namespace SenSÉ.Windows;

/// <summary>
/// Walks up from a HID interface to the device it belongs to.
/// </summary>
/// <remarks>
/// The missing half of <see cref="DurableControllerKey"/>. That class knows how to read a durable
/// identity out of a device instance id; this one knows where to find those ids, which is above the
/// interface the controller is read through.
///
/// <para>
/// Split that way because only one of the two can be tested. Parsing an instance id is pure and is
/// pinned against strings a real machine produced; walking the device tree needs Windows, a driver
/// stack and a plugged-in controller, and no test can stand in for that.
/// </para>
/// </remarks>
public static class DeviceTree
{
    private const int CrSuccess = 0;
    private const int MaxDeviceIdLength = 200;

    /// <summary>
    /// The durable key of the controller behind a HID interface path, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Null is a real answer, not a failure to report. A pad whose ancestry carries nothing durable
    /// — as an XInput slot does not — must be treated as recognisable only for this session, and
    /// saying so is the whole point of not inventing a key.
    /// </remarks>
    public static string? DurableKeyFor(string interfacePath, Action<string>? log = null)
    {
        try
        {
            var chain = AncestorInstanceIds(interfacePath).ToList();

            if (chain.Count == 0)
            {
                log?.Invoke($"no device node found for {interfacePath}");
                return null;
            }

            var key = DurableControllerKey.From(chain);

            if (key is not null)
            {
                log?.Invoke($"durable key {key} from {chain[0]}");
                return key;
            }

            // Nothing in the instance ids identifies the device: a pad that enumerates by port and
            // reports no serial, which is common on third-party controllers. Windows keeps its own
            // answer to "which physical device is this" — the container id — and that is better than
            // the alternative here, which is a slot number that moves between controllers.
            //
            // Its limit is worth stating rather than hiding: for a device with no serial Windows
            // derives the container id from where it is plugged in, so it survives reboots and
            // reconnections to the same port, and changes if the pad is moved to another one. The
            // prefix says so, and the log line names it, so a profile that stops matching after a
            // pad is moved is explainable instead of mysterious.
            if (ContainerIdOf(chain[0]) is { } container)
            {
                // The model is part of the key, not decoration. A container id derived from a port
                // is reused by whatever is plugged into that port next, so a different pad in the
                // same socket would inherit this one's profile — the silent swap the durable-key
                // rules exist to prevent. With the vendor and product in the key, only the same
                // model in the same port can inherit, and inheriting from an identical model is a
                // defensible outcome rather than a wrong one.
                var containerKey = $"dev:{ModelOf(chain)}{container}";

                log?.Invoke($"durable key {containerKey} (container id) from {chain[0]}");
                return containerKey;
            }

            log?.Invoke($"no durable identity in [{string.Join(" <- ", chain)}]");
            return null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"resolving the durable key: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The device instance behind the interface, then each of its ancestors.</summary>
    public static IEnumerable<string> AncestorInstanceIds(string interfacePath)
    {
        var instanceId = ToInstanceId(interfacePath);

        if (instanceId.Length == 0 || CM_Locate_DevNodeW(out var node, instanceId, 0) != CrSuccess)
        {
            yield break;
        }

        // Bounded rather than "until it fails". A malformed tree that reported itself as its own
        // parent would spin here forever, and a controller is never more than a handful of levels
        // below the root.
        for (var depth = 0; depth < 16; depth++)
        {
            var id = InstanceIdOf(node);
            if (id.Length == 0)
            {
                yield break;
            }

            yield return id;

            if (CM_Get_Parent(out var parent, node, 0) != CrSuccess)
            {
                yield break;
            }

            node = parent;
        }
    }

    /// <summary>
    /// Turns an interface path into the device instance id it belongs to.
    /// </summary>
    /// <remarks>
    /// The two are the same identifier written differently:
    /// <c>\\?\hid#{svc}_vid&amp;0002054c_pid&amp;0ce6#8&amp;15f755c8&amp;3&amp;0000#{class-guid}</c>
    /// against <c>HID\{svc}_VID&amp;0002054C_PID&amp;0CE6\8&amp;15F755C8&amp;3&amp;0000</c>. The
    /// prefix goes, the trailing class GUID goes, and the remaining separators become backslashes.
    /// </remarks>
    public static string ToInstanceId(string interfacePath)
    {
        if (string.IsNullOrWhiteSpace(interfacePath))
        {
            return "";
        }

        var path = interfacePath;

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        // The class GUID is the last segment and belongs to the interface, not to the device.
        var lastHash = path.LastIndexOf('#');
        if (lastHash > 0 && path.IndexOf('{', lastHash) > 0)
        {
            path = path[..lastHash];
        }

        return path.Replace('#', '\\');
    }

    private static string InstanceIdOf(uint node)
    {
        var buffer = new StringBuilder(MaxDeviceIdLength);

        return CM_Get_Device_IDW(node, buffer, MaxDeviceIdLength, 0) == CrSuccess
            ? buffer.ToString()
            : "";
    }

    /// <summary>The vendor and product of the first node that names them, as a key fragment.</summary>
    /// <remarks>
    /// Empty when nothing in the chain says: the container id alone is still better than a slot, and
    /// refusing a key here would leave the pad with no durable identity at all.
    /// </remarks>
    private static string ModelOf(IEnumerable<string> chain)
    {
        foreach (var id in chain)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                id, @"VID[_&]([0-9A-Fa-f]{4}).*?PID[_&]([0-9A-Fa-f]{4})");

            if (match.Success)
            {
                return $"vid_{match.Groups[1].Value.ToLowerInvariant()}&pid_{match.Groups[2].Value.ToLowerInvariant()}:";
            }
        }

        return "";
    }

    /// <summary>
    /// Whether this device is a keyboard or a mouse, or carries one underneath it.
    /// </summary>
    /// <remarks>
    /// <b>The question to ask before hiding anything from the rest of Windows.</b> A pad-and-keyboard
    /// combination arrives as one composite device whose children are a gamepad on one interface and
    /// a keyboard and a mouse on another. Hiding the gamepad interface is right and is what happens
    /// today; hiding the parent would take the keyboard and the mouse with it, and leave the machine
    /// with no way to type — including no way to reach the setting that would undo it.
    ///
    /// <para>
    /// Measured on the development machine, which has two such devices: <c>VID_37D7&amp;PID_2501</c>
    /// exposes <c>MI_00</c> as an Xbox pad and <c>MI_01</c> as both a keyboard and a mouse, on one
    /// plug. Nothing in the code prevented the parent from being chosen — it simply never was.
    /// </para>
    ///
    /// <para>
    /// The subtree is walked rather than the name inspected. A rule based on the shape of an
    /// identifier — "refuse anything without an interface number" — would also refuse every
    /// single-interface gamepad, and would still be guessing.
    /// </para>
    /// </remarks>
    public static bool IsOrCarriesKeyboardOrMouse(string instanceId)
    {
        try
        {
            if (instanceId.Length == 0 || CM_Locate_DevNodeW(out var node, instanceId, 0) != CrSuccess)
            {
                // Unknown is treated as dangerous. A device Windows will not describe is not one to
                // hide on the assumption that it is only a gamepad.
                return true;
            }

            return SubtreeHasInputDevice(node, depth: 0);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Walks a device and everything below it, looking for a keyboard or a mouse.</summary>
    private static bool SubtreeHasInputDevice(uint node, int depth)
    {
        // Deep enough for any real device tree, and a bound rather than a promise: a malformed tree
        // must not become an endless walk inside the code that decides what to hide.
        if (depth > 8)
        {
            return false;
        }

        if (ClassOf(node) is { } name
            && (name.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Mouse", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (CM_Get_Child(out var child, node, 0) != CrSuccess)
        {
            return false;
        }

        while (true)
        {
            if (SubtreeHasInputDevice(child, depth + 1))
            {
                return true;
            }

            if (CM_Get_Sibling(out var sibling, child, 0) != CrSuccess)
            {
                return false;
            }

            child = sibling;
        }
    }

    /// <summary>The device class of a node — "Keyboard", "Mouse", "XnaComposite" — or null.</summary>
    private static string? ClassOf(uint node)
    {
        var key = DevpkeyDeviceClass;
        var buffer = new byte[256];
        var size = (uint)buffer.Length;

        if (CM_Get_DevNode_PropertyW(node, ref key, out var type, buffer, ref size, 0) != CrSuccess
            || type != DevpropTypeString)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    /// <summary>DEVPKEY_Device_Class.</summary>
    private static DevpropKey DevpkeyDeviceClass => new()
    {
        Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Pid = 9,
    };

    private const uint DevpropTypeString = 0x00000012;

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Child(out uint child, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Sibling(out uint sibling, uint devInst, uint flags);

    /// <summary>
    /// Windows' own identifier for the physical device an instance belongs to, or null.
    /// </summary>
    /// <remarks>
    /// All the interfaces a composite device exposes — a pad's gamepad, audio and vendor collections
    /// — share one container id, which is exactly the grouping wanted here: one key per controller,
    /// not one per collection.
    /// </remarks>
    private static string? ContainerIdOf(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var node, instanceId, 0) != CrSuccess)
            {
                return null;
            }

            var key = DevpkeyDeviceContainerId;
            var buffer = new byte[16];
            var size = (uint)buffer.Length;

            if (CM_Get_DevNode_PropertyW(node, ref key, out var type, buffer, ref size, 0) != CrSuccess
                || type != DevpropTypeGuid
                || size != 16)
            {
                return null;
            }

            var container = new Guid(buffer);

            return container == Guid.Empty ? null : container.ToString("N");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>DEVPKEY_Device_ContainerId.</summary>
    private static DevpropKey DevpkeyDeviceContainerId => new()
    {
        Fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),
        Pid = 2,
    };

    /// <summary>DEVPKEY_Device_Service — the driver that owns a node.</summary>
    private static DevpropKey DevpkeyDeviceService => new()
    {
        Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Pid = 6,
    };

    /// <summary>The bus driver whose children are the pads this product creates.</summary>
    private const string VirtualPadBusService = "ViGEmBus";

    /// <summary>
    /// Whether a pad is one the machine emulates rather than one somebody is holding.
    /// </summary>
    /// <remarks>
    /// SenSÉ creates a virtual Xbox 360 pad so that games see a controller it understands. Its
    /// own discovery loop then found that pad and adopted it as an arriving controller — measured on
    /// 12 August, four tenths of a second after creating it. It went on to allocate the phantom a
    /// keyboard instance, launch an overlay process for it, and hide it with HidHide: the output
    /// existed so games could see it, and the product concealed it from them.
    ///
    /// <para>
    /// <b>The vendor and product cannot be the test.</b> A ViGEmBus pad presents itself as
    /// <c>VID_045E&amp;PID_028E</c> precisely because that is a wired Xbox 360 controller — being
    /// indistinguishable is its entire purpose. Filtering on that pair would throw away every real
    /// Xbox 360 pad a customer owns.
    /// </para>
    ///
    /// <para>
    /// The ancestry answers it instead. A physical pad hangs off a USB hub and, above that, a PCI
    /// host controller; an emulated one hangs off a root-enumerated bus node — on this machine
    /// <c>ROOT\SYSTEM\0003</c>, whose instance id says nothing at all. So the node is asked which
    /// driver owns it, which is exact and cannot collide with a real device.
    /// </para>
    /// </remarks>
    public static bool IsEmulatedPad(string interfacePath, Action<string>? log = null)
    {
        try
        {
            foreach (var ancestor in AncestorInstanceIds(interfacePath))
            {
                if (!string.Equals(ServiceOf(ancestor), VirtualPadBusService, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                log?.Invoke($"emulated pad ignored: {interfacePath} hangs off {ancestor} ({VirtualPadBusService}).");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            // Not emulated is the safe answer: a pad wrongly called virtual disappears for the user,
            // while one wrongly called real is the behaviour that existed before this check.
            log?.Invoke($"deciding whether {interfacePath} is emulated: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The device nodes an emulated pad occupies, from its interface up to but not including the bus.
    /// </summary>
    /// <remarks>
    /// One virtual pad is not one record. Measured on 12 August, a single one left three: the pad
    /// itself as <c>USB\VID_045E&amp;PID_028E\01</c>, and an XInput and a HID interface beneath it,
    /// each carrying an instance number Windows mints fresh every time. The pad node is reused; the
    /// two interfaces are not, which is how twenty-nine of them accumulated.
    ///
    /// <para>
    /// The bus is excluded deliberately. It is the driver's own node, installed with ViGEmBus and
    /// belonging to it — removing that would uninstall the bus out from under every application on
    /// the machine that uses it, not just this one.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> EmulatedPadNodes(string interfacePath)
    {
        var nodes = new List<string>();

        try
        {
            foreach (var ancestor in AncestorInstanceIds(interfacePath))
            {
                if (string.Equals(ServiceOf(ancestor), VirtualPadBusService, StringComparison.OrdinalIgnoreCase))
                {
                    // Reached the bus: everything gathered below it is the pad, and the bus is not ours.
                    return nodes;
                }

                nodes.Add(ancestor);
            }
        }
        catch
        {
            return [];
        }

        // Walked to the top without meeting the bus, so this was never an emulated pad.
        return [];
    }

    private static string? ServiceOf(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var node, instanceId, 0) != CrSuccess)
            {
                return null;
            }

            var key = DevpkeyDeviceService;
            var buffer = new byte[256];
            var size = (uint)buffer.Length;

            if (CM_Get_DevNode_PropertyW(node, ref key, out var type, buffer, ref size, 0) != CrSuccess
                || type != DevpropTypeString
                || size == 0)
            {
                return null;
            }

            return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        }
        catch
        {
            return null;
        }
    }

    private const uint DevpropTypeGuid = 0x0000000D;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevpropKey
    {
        public Guid Fmtid;
        public uint Pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint devInst, ref DevpropKey propertyKey, out uint propertyType,
        byte[] buffer, ref uint bufferSize, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);
}
