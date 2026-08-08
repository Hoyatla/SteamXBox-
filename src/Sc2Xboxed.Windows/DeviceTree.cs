using System.Runtime.InteropServices;
using System.Text;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Windows;

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

            log?.Invoke(key is null
                ? $"no durable identity in [{string.Join(" <- ", chain)}]"
                : $"durable key {key} from {chain[0]}");

            return key;
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

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);
}
