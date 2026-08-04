using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;
using Sc2Xboxed.Hid;
using Sc2Xboxed.Windows;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// Builds a reader for every controller attached to this machine.
/// </summary>
/// <remarks>
/// Enumeration only. Which controllers exist is a question about Windows and cannot be tested
/// without hardware; what to do with the answer is <see cref="ControllerRoster"/>, which is pure and
/// is where the rules that have actually gone wrong — duplicate collections, unstable ordering —
/// are kept honest.
///
/// Nothing here ranks or excludes. That is the entire point of the change: every controller is an
/// input, so every controller gets read.
/// </remarks>
public static class AttachedControllers
{
    /// <summary>
    /// Opens a source per attached controller.
    /// </summary>
    /// <param name="forcedKind">"steam", "xinput", or empty to take everything.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <remarks>
    /// The Steam Controller is read over HID and Xbox-compatible pads through XInput, which is the
    /// same API the game uses. A PlayStation pad appears as neither unless a driver presents it as
    /// one, so it is absent here by nature rather than by choice — worth stating, because "the
    /// DualSense does nothing" otherwise looks like a bug in this file.
    /// </remarks>
    /// <param name="physicalSlots">
    /// The XInput slots that were occupied before SteamXBox created anything virtual. Required, not
    /// optional.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The virtual pads SteamXBox creates are themselves XInput devices.</b> Reading every slot
    /// that answers therefore reads our own output: the Core writes a report, the virtual pad
    /// appears on a slot, the Core reads it back, maps it, and writes again. The result is a closed
    /// loop — a selector travelling in a straight line that nothing can stop, because every frame it
    /// emits becomes the next frame it reads.
    /// </para>
    /// <para>
    /// A snapshot taken before any virtual pad exists is the only reliable way to tell ours apart.
    /// Filtering by vendor and product cannot: a ViGEm Xbox 360 pad is indistinguishable from a real
    /// one through XInput, which is exactly what makes it useful to games and useless to identify
    /// here.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)> Open(
        IReadOnlyCollection<int> physicalSlots,
        string forcedKind = "",
        Action<string>? log = null)
    {
        var opened = new List<(ControllerIdentity, IPhysicalControllerSource)>();

        var wantsSteam = forcedKind is "" or "steam";
        var wantsXInput = forcedKind is "" or "xinput";

        if (wantsSteam)
        {
            var discovery = new SteamHidDiscovery(log);
            if (discovery.FindPreferredControllerDevice() is { } device)
            {
                opened.Add((
                    new ControllerIdentity(
                        ControllerKind.SteamController,
                        ControllerIdentityFactory.FromHidPath(device.DevicePath),
                        "Steam Controller",
                        Slot: -1),
                    new TritonSteamControllerSource(
                        new SteamHidDiscovery(log),
                        new TritonInputReportParser(),
                        readTimeoutMs: 20,
                        manageNativeLayer: true,
                        initialNativeLayerEnabled: false,
                        log: log)));
            }
        }

        // A PlayStation pad appears as neither a Valve HID device nor an XInput one. It is its own
        // kind, read over plain HID, and it is included by default: "all connected controllers are
        // inputs" has to mean all of them.
        if (forcedKind is "" or "dualsense")
        {
            // One source per physical pad, not per interface. A DualSense exposes several HID
            // collections — the gamepad, plus control channels — and opening each as its own
            // controller means the same pad speaking two or three times at once, with the control
            // channels' bytes decoded as gamepad reports. On screen that is a pad that works for a
            // moment and then does something arbitrary, because a second stream started answering.
            //
            // Grouped by the device key, which already strips the collection suffix, then the
            // longest input report wins: the gamepad interface is the one that actually carries the
            // axes, and the others would open happily and deliver nonsense.
            var index = 1;
            foreach (var group in DualSenseControllerSource.Discover(log)
                         .GroupBy(d => ControllerIdentityFactory.FromHidPath(d.DevicePath)))
            {
                var device = group.OrderByDescending(d => d.GetMaxInputReportLength()).First();

                if (group.Count() > 1)
                {
                    log?.Invoke(
                        $"DualSense {group.Key}: {group.Count()} interfaces, reading the one with the "
                        + $"longest input report ({device.GetMaxInputReportLength()} bytes).");
                }

                opened.Add((
                    new ControllerIdentity(
                        ControllerKind.DualSense,
                        group.Key,
                        $"Manette PS5 {index++}",
                        Slot: -1),
                    new DualSenseControllerSource(device.DevicePath, log: log)));
            }
        }

        if (wantsXInput)
        {
            // Every answering slot, each pinned to its own reader. Pinning matters: a source left to
            // "find the first slot that answers" would have several readers racing onto the same
            // controller while the others went unread.
            // Intersected with the snapshot, never re-enumerated. A slot answering now that was not
            // occupied at startup is one of our own virtual pads.
            foreach (var slot in XInputControllerSource.ConnectedSlots().Where(physicalSlots.Contains))
            {
                opened.Add((
                    new ControllerIdentity(
                        ControllerKind.XInput,
                        ControllerIdentityFactory.FromXInputSlot(slot),
                        $"Manette Xbox {slot + 1}",
                        slot),
                    new XInputControllerSource(slot)));
            }
        }

        return opened;
    }
}
