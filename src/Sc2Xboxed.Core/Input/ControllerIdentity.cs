namespace Sc2Xboxed.Core.Input;

/// <summary>How a controller is read.</summary>
public enum ControllerKind
{
    /// <summary>A Valve controller, read over HID.</summary>
    SteamController,

    /// <summary>An Xbox-compatible pad, read through XInput.</summary>
    XInput,

    /// <summary>
    /// A PlayStation 5 DualSense, read over plain HID.
    /// </summary>
    /// <remarks>
    /// Its own kind rather than folded into the others: it is not an XInput device and cannot be
    /// made into one, and it is not a Valve device either. Treating it as either is how it ended up
    /// unread while both other paths reported working normally.
    /// </remarks>
    DualSense,
}

/// <summary>
/// One physical controller, and the key its settings are filed under.
/// </summary>
/// <param name="Kind">How it is read.</param>
/// <param name="Id">
/// Stable key. Survives a disconnection, a reboot and a change in connection order — which is what
/// makes a per-controller profile mean anything.
/// </param>
/// <param name="DisplayName">What to show the user.</param>
/// <param name="Slot">XInput slot, or -1 for a controller that has none.</param>
public readonly record struct ControllerIdentity(
    ControllerKind Kind,
    string Id,
    string DisplayName,
    int Slot);

/// <summary>
/// Builds the stable key a controller's profile is filed under.
/// </summary>
/// <remarks>
/// Every controller is an input, so each needs a name of its own — one profile per controller only
/// means something if the controller can be recognised again tomorrow.
///
/// The obvious key is the XInput slot, and it is the wrong one: Windows hands slots out in
/// connection order, so today's controller 0 is tomorrow's controller 1 and the two players'
/// profiles swap between sessions. Worse, it fails silently — nothing looks broken, the settings
/// are simply attached to the wrong hands.
///
/// A HID device carries a real identity in its interface path, which contains the vendor, the
/// product and the device instance. XInput exposes no such thing: the API gives a slot index and
/// nothing else. That gap is named here rather than papered over, because it decides how far a
/// per-controller profile can be trusted.
/// </remarks>
public static class ControllerIdentityFactory
{
    /// <summary>Key for a HID controller, derived from its interface path.</summary>
    /// <remarks>
    /// Built from the vendor, the product and the device instance, and from nothing else.
    ///
    /// <para>
    /// A Windows interface path looks like
    /// <c>\\?\hid#{service}_vid&amp;0002054c_pid&amp;0ce6#8&amp;15f755c8&amp;3&amp;0000#{interface-guid}</c>.
    /// The trailing GUID is the HID <i>class</i> interface and is identical on every HID device, so
    /// keeping it adds no identity while making the key sensitive to any variation in how the path
    /// is spelled. The previous version cut at the <c>&amp;col</c> collection marker — which a Steam
    /// Controller has and a DualSense does not — so on a DualSense nothing was trimmed at all.
    /// </para>
    ///
    /// <para>
    /// The consequence was measured, not theorised: the same pad acquired two different keys between
    /// two enumerations, the hot-plug rescan saw the second as a new controller, and SteamXBox read
    /// one physical DualSense through two readers at once. That doubles the frame rate, makes two
    /// contenders for a pointer whose arbitration cancels contention, and files the controller's
    /// profile under a key that will not exist tomorrow.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Resolves a device's durable identity from its interface path, when the platform can.
    /// </summary>
    /// <remarks>
    /// Injected rather than called directly: reading the durable identity means walking the Windows
    /// device tree, and this assembly must stay free of the platform. The composition roots plug in
    /// <c>DeviceTree.DurableKeyFor</c>; left unset — in tests, or on a platform without it — the
    /// path-derived key is used and is honestly reported as non-durable.
    /// </remarks>
    public static Func<string, string?>? DurableKeyResolver { get; set; }

    public static string FromHidPath(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "hid:unknown";
        }

        // The real identity first. What follows is derived from the interface path, whose instance
        // segment Windows reissues on every Bluetooth reconnection — so it names a connection, not a
        // controller, and a profile filed under it can never find its pad again.
        if (DurableKeyResolver?.Invoke(devicePath) is { Length: > 0 } durable)
        {
            return durable;
        }

        var lowered = devicePath.ToLowerInvariant();

        // The class interface GUID, always last and always the same; dropped before anything else.
        var lastHash = lowered.LastIndexOf('#');
        if (lastHash > 0 && lowered.IndexOf('{', lastHash) > 0)
        {
            lowered = lowered[..lastHash];
        }

        // Everything from the first "vid" up to the collection marker, if any.
        var start = lowered.IndexOf("vid", StringComparison.Ordinal);
        if (start < 0)
        {
            start = 0;
        }

        var end = lowered.IndexOf("&col", start, StringComparison.Ordinal);
        var core = end > start ? lowered[start..end] : lowered[start..];

        return "hid:" + core.Trim('\\', '#', '&');
    }

    /// <summary>
    /// Key for an XInput controller.
    /// </summary>
    /// <remarks>
    /// The slot is all XInput offers, so it is what the key is built from — and it is not stable
    /// across reconnections. Marked as such in the key itself so the weakness is visible wherever
    /// the key is read, rather than being discovered when two players' settings swap.
    ///
    /// Making this durable means correlating the slot to a device instance through another API.
    /// That is worth doing, and it is not done here.
    /// </remarks>
    public static string FromXInputSlot(int slot) => $"xinput-slot:{slot}";

    /// <summary>
    /// Whether a key identifies the same physical device across sessions.
    /// </summary>
    /// <remarks>
    /// Only a key built from something burned into the device: a Bluetooth address, or a USB serial
    /// the controller reports itself. Everything else names a connection.
    ///
    /// This used to answer true for any <c>hid:</c> key, on the belief that a HID interface path was
    /// durable. It is not — Windows issues a new instance segment on every Bluetooth reconnection —
    /// and the consequence was invisible by construction: assignments were written to disk, the pad
    /// came back under a new key, and it simply ran on the defaults with nothing saying why. A
    /// method that lies about durability is worse than one that admits it has none.
    /// </remarks>
    public static bool IsStable(string id)
        => id.StartsWith("bt:", StringComparison.Ordinal)
           || id.StartsWith("usb:", StringComparison.Ordinal);
}
