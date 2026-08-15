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
/// One physical controller, as seen by the rest of the runtime.
/// </summary>
/// <param name="Kind">How it is read.</param>
/// <param name="Id">
/// Identity for this session. Two pads must not be conflated while both are attached, so each keeps
/// its own key here; it is no longer filed under.
/// </param>
/// <param name="DisplayName">What to show the user.</param>
/// <param name="Slot">XInput slot, or -1 for a controller that has none.</param>
public readonly record struct ControllerIdentity(
    ControllerKind Kind,
    string Id,
    string DisplayName,
    int Slot)
{
    /// <summary>
    /// The family this controller belongs to — the key its settings are filed under.
    /// </summary>
    /// <remarks>
    /// Settings are not filed under the controller any more. The same DualSense presented itself
    /// under two Bluetooth addresses on one machine — a public one and a rotating one — so nothing
    /// about the pad is reliable enough to file settings under and have them found again. What never
    /// changes is the family: Steam, PlayStation or Xbox. All three kinds map to one.
    /// </remarks>
    public string FamilyId => ControllerIdentityFactory.FamilyKey(Kind);
}

/// <summary>
/// Builds the keys a controller's settings are filed under.
/// </summary>
/// <remarks>
/// Settings are filed by family, not by controller. This used to build a key per physical device —
/// a Bluetooth address, a USB serial — and the config window filed a profile under it. Then one
/// DualSense turned up under two different addresses on the author's machine, and the profile
/// filed under the address the pad happened to connect with was gone the next time it connected
/// with the other one. Nothing said so: the pad ran on the defaults, and the only symptom was a
/// pad that "felt wrong". A family never has that problem — a Steam Controller is a Steam
/// Controller tomorrow, whatever address it arrives on — so the family is what settings are
/// filed under.
///
/// The per-controller key still exists (<see cref="FromHidPath"/>, <see cref="FromXInputSlot"/>)
/// because two pads must not be conflated while both are attached: sessions, deduplication and
/// the roster all need to tell them apart. It is just never persisted as the owner of settings.
/// </remarks>
public static class ControllerIdentityFactory
{
    /// <summary>The key a family of controllers' settings are filed under.</summary>
    /// <remarks>
    /// One per family, because the three have genuinely different capabilities and a profile
    /// written for trackpads means nothing on a pad that has none. An empty string for an unknown
    /// kind, which falls back to the launch profile rather than claiming a family of its own.
    /// </remarks>
    public static string FamilyKey(ControllerKind kind)
        => kind switch
        {
            ControllerKind.SteamController => "fam:steam",
            ControllerKind.DualSense => "fam:ps5",
            ControllerKind.XInput => "fam:xbox",
            _ => "",
        };
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
    /// Whether a key is one settings may be filed under.
    /// </summary>
    /// <remarks>
    /// Only family keys qualify. Everything naming a controller was withdrawn, after one DualSense
    /// appeared under two Bluetooth addresses on the author's machine: <c>bt:</c> keys were trusted
    /// to survive reconnections, and the same physical pad filed its settings under whichever
    /// address it had connected with — the settings were never found again, and nothing said so.
    /// A key that lies about durability is worse than one that admits it has none, so only the
    /// family keys, which cannot change under a controller, are accepted.
    /// </remarks>
    public static bool IsStable(string id)
        => id.StartsWith("fam:", StringComparison.Ordinal);
}
