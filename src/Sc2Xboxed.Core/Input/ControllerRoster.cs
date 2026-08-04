namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Every controller currently attached, each one an input in its own right.
/// </summary>
/// <remarks>
/// The list, not a choice. SteamXBox used to pick one controller by a fixed rule and ignore the
/// rest, which makes split-screen impossible by construction: two players need two physical pads
/// feeding two distinct virtual ones. Nothing here ranks or filters — ranking is what broke it.
///
/// Pure on purpose. The Windows-specific enumeration hands in what it found; the rules for turning
/// that into a roster live here where they can be tested, because the mistakes worth catching are
/// about identity and duplication, not about talking to HID.
///
/// <para>
/// How the shared devices are settled, decided with the author and recorded here because the answer
/// is not obvious from the code:
/// </para>
/// <list type="bullet">
/// <item>
/// The virtual Xbox pads are <b>one per controller</b>. That is the whole point: split-screen needs
/// each player's inputs kept apart all the way to the game.
/// </item>
/// <item>
/// The pointer, the wheel and the overlay keyboard are <b>shared</b>, because the desktop has one
/// of each. No controller is designated to drive them and none is locked out: whichever one sends
/// movement moves the pointer.
/// </item>
/// <item>
/// Two people pushing at once therefore contend, and their contributions cancel — the pointer
/// stalls rather than jumping between two intents. That is the accepted outcome, not a case to
/// arbitrate. Electing an owner would mean the second player's controller silently doing nothing,
/// which is harder to understand than a pointer that will not move while two hands fight over it.
/// </item>
/// </list>
/// </remarks>
public static class ControllerRoster
{
    /// <summary>
    /// Builds the roster from what the platform enumerated.
    /// </summary>
    /// <param name="hidPaths">Interface paths of every Valve controller interface found.</param>
    /// <param name="occupiedXInputSlots">XInput slots that answered.</param>
    /// <remarks>
    /// Ordering is deliberate and stable: Steam Controllers first, by key, then XInput slots in
    /// numerical order. The roster feeds a list the user clicks on, and a list that reshuffles
    /// itself between two refreshes is one nobody can point at.
    /// </remarks>
    public static IReadOnlyList<ControllerIdentity> Build(
        IEnumerable<string> hidPaths,
        IEnumerable<int> occupiedXInputSlots)
    {
        var roster = new List<ControllerIdentity>();

        // One controller exposes several HID collections — a keyboard, a mouse, the controller
        // state. Left as they come, one pad would appear three times and be given three profiles.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in hidPaths
                     .Select(ControllerIdentityFactory.FromHidPath)
                     .OrderBy(k => k, StringComparer.Ordinal))
        {
            if (seen.Add(key))
            {
                roster.Add(new ControllerIdentity(
                    ControllerKind.SteamController,
                    key,
                    $"Steam Controller {roster.Count(r => r.Kind == ControllerKind.SteamController) + 1}",
                    Slot: -1));
            }
        }

        foreach (var slot in occupiedXInputSlots.Distinct().OrderBy(s => s))
        {
            roster.Add(new ControllerIdentity(
                ControllerKind.XInput,
                ControllerIdentityFactory.FromXInputSlot(slot),
                $"Manette Xbox {slot + 1}",
                slot));
        }

        return roster;
    }

    /// <summary>
    /// Which controllers appeared and which went away between two rosters.
    /// </summary>
    /// <remarks>
    /// The Core reads each controller on its own loop, so it has to start one when a pad arrives and
    /// stop one when it leaves — rebuilding everything on every refresh would drop the virtual pads
    /// of players who never touched anything, which in a split-screen game means losing a player
    /// mid-match because someone else's controller went to sleep.
    /// </remarks>
    public static (IReadOnlyList<ControllerIdentity> Added, IReadOnlyList<ControllerIdentity> Removed) Diff(
        IReadOnlyList<ControllerIdentity> before,
        IReadOnlyList<ControllerIdentity> after)
    {
        var previous = before.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var current = after.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        return (
            after.Where(c => !previous.Contains(c.Id)).ToList(),
            before.Where(c => !current.Contains(c.Id)).ToList());
    }
}
