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
    /// The path is normalised: Windows varies its case and appends a collection suffix per
    /// interface, so the raw string differs between two enumerations of one controller. The
    /// vendor, product and instance segment are what identify the device; the rest is noise that
    /// would make the same controller look like a new one on every reconnection.
    /// </remarks>
    public static string FromHidPath(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "hid:unknown";
        }

        var lowered = devicePath.ToLowerInvariant();

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

    /// <summary>Whether a key identifies the same physical device across sessions.</summary>
    public static bool IsStable(string id)
        => id.StartsWith("hid:", StringComparison.Ordinal);
}
