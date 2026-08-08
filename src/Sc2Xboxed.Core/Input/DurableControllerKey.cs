namespace Sc2Xboxed.Core.Input;

/// <summary>
/// The key a controller can still be recognised by tomorrow.
/// </summary>
/// <remarks>
/// The whole of "one profile per controller" rests on this, and the previous attempt rested on
/// nothing. A HID interface path carries a device instance segment that <i>looks</i> durable —
/// <c>8&amp;15f755c8&amp;3&amp;0000</c> — and Windows issues a new one on every Bluetooth
/// reconnection. Filing a profile under it meant the assignment could never find its controller
/// again, and nothing said so: the pad simply came back on the default settings.
///
/// <para>
/// The durable identity lives one level up, in the device the interface belongs to:
/// </para>
/// <list type="bullet">
/// <item><b>Bluetooth</b> — <c>BTHENUM\DEV_44464836686D\…</c>, where the hexadecimal run is the
/// controller's MAC address. It is burned into the pad and survives everything.</item>
/// <item><b>USB</b> — <c>USB\VID_054C&amp;PID_0CE6\<i>serial</i></c>, where the trailing segment is
/// the device serial when the controller reports one.</item>
/// </list>
///
/// <para>
/// This class does the reading of those strings, which is where the mistakes are, and leaves the
/// walking of the device tree to the platform layer. Splitting it that way is deliberate: parsing
/// can be tested against the exact identifiers a real machine produced, and the tree walk cannot be
/// tested at all.
/// </para>
/// </remarks>
public static class DurableControllerKey
{
    /// <summary>
    /// The durable key for a device, given its own instance id and those of its parents.
    /// </summary>
    /// <param name="instanceIds">
    /// The device instance and its ancestors, nearest first. The platform layer supplies them by
    /// walking up from the HID interface.
    /// </param>
    /// <returns>A durable key, or null when nothing in the chain identifies the device durably.</returns>
    /// <remarks>
    /// Null rather than a fallback. A key invented from a non-durable identifier is worse than no
    /// key: it persists, it looks right, and it silently attaches settings to the wrong pad. The
    /// caller can then say so plainly instead of pretending.
    /// </remarks>
    public static string? From(IEnumerable<string> instanceIds)
    {
        foreach (var id in instanceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var lowered = id.ToLowerInvariant();

            if (BluetoothAddress(lowered) is { } mac)
            {
                return "bt:" + mac;
            }

            if (UsbSerial(lowered) is { } serial)
            {
                return "usb:" + serial;
            }
        }

        return null;
    }

    /// <summary>
    /// The controller's MAC address inside a Bluetooth instance id.
    /// </summary>
    /// <remarks>
    /// Two shapes carry it, and only the second is the parent of a HID interface — which is the one
    /// that matters here and the one I first got wrong:
    /// <list type="bullet">
    /// <item><c>BTHENUM\DEV_44464836686D\…</c> — the radio-level node.</item>
    /// <item><c>BTHENUM\{service}_VID&amp;…_PID&amp;…\7&amp;1b869c0e&amp;1&amp;44464836686d_c00000000</c>
    /// — the device the HID interface hangs from, where the address is one field among several.</item>
    /// </list>
    ///
    /// So the address is found by shape rather than by marker: within a Bluetooth instance id, the
    /// one token that is exactly twelve hexadecimal digits. Searching for a <c>DEV_</c> prefix found
    /// nothing on a real DualSense, and returning null there would have been read as "this pad has
    /// no durable identity" — the correct-looking failure that is hardest to notice.
    /// </remarks>
    private static string? BluetoothAddress(string lowered)
    {
        if (!lowered.StartsWith("bthenum\\", StringComparison.Ordinal) &&
            !lowered.StartsWith("bth\\", StringComparison.Ordinal) &&
            !lowered.StartsWith("bthle", StringComparison.Ordinal))
        {
            return null;
        }

        // Only the last segment: the earlier ones carry the service GUID and the vendor and product
        // identifiers, which are shared by every pad of the same model.
        var tail = lowered[(lowered.LastIndexOf('\\') + 1)..];

        foreach (var token in tail.Split('&', '_', '#'))
        {
            if (token.Length == 12 && token.All(Uri.IsHexDigit))
            {
                return token;
            }
        }

        // The radio-level form, kept because it is what a device enumerated from the Bluetooth side
        // looks like.
        var marker = lowered.IndexOf("dev_", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + 4;
        var end = start;
        while (end < lowered.Length && Uri.IsHexDigit(lowered[end]))
        {
            end++;
        }

        return end - start == 12 ? lowered[start..end] : null;
    }

    /// <summary>The serial segment of a <c>USB\VID_xxxx&amp;PID_xxxx\serial</c> instance id.</summary>
    /// <remarks>
    /// Only when the controller reports a real serial. Windows fabricates one containing
    /// <c>&amp;</c> when the device has none, and that fabricated value is tied to the physical port
    /// rather than to the device — the same pad in another socket would look like a new controller.
    /// </remarks>
    private static string? UsbSerial(string lowered)
    {
        if (!lowered.StartsWith("usb\\", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = lowered.Split('\\');
        if (parts.Length < 3)
        {
            return null;
        }

        var serial = parts[2];

        return serial.Length > 0 && !serial.Contains('&') ? $"{parts[1]}:{serial}" : null;
    }
}
