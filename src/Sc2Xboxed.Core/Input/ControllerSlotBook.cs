namespace Sc2Xboxed.Core.Input;

/// <summary>One controller's remembered place, and when it was last seen.</summary>
/// <param name="Slot">The number shown to the user, from 1.</param>
/// <param name="LastSeen">When this controller last turned up.</param>
public readonly record struct ControllerSlot(int Slot, DateTimeOffset LastSeen);

/// <summary>
/// Which number belongs to which controller, kept between sessions.
/// </summary>
/// <remarks>
/// "Manette 1" has to be the same pad tomorrow. Numbering by position in the attached list — which
/// is what the interface did — renumbers everybody the moment somebody switches a controller on in a
/// different order: player two's profile is still filed correctly, but the number on the chip he
/// clicks is now someone else's.
///
/// <para>
/// Only durable keys are remembered. An XInput slot already names a connection order, so filing a
/// remembered number under it would be a second layer of the same instability — and the entry would
/// silently claim a number that belongs to another pad.
/// </para>
///
/// <para>
/// The clock is passed in rather than read. Expiry is the part worth testing, and a test that has to
/// wait a month to observe it is a test nobody writes.
/// </para>
/// </remarks>
public sealed class ControllerSlotBook
{
    private readonly Dictionary<string, ControllerSlot> _slots = new(StringComparer.Ordinal);

    /// <summary>How many controllers are remembered.</summary>
    public int Count => _slots.Count;

    /// <summary>
    /// The number for a controller, claiming the lowest free one the first time it is seen.
    /// </summary>
    /// <remarks>
    /// Lowest free rather than next-highest: a household that pairs and unpairs pads would otherwise
    /// see numbers climb forever, and "Manette 7" with two controllers in the room reads as a fault.
    /// </remarks>
    public int SlotFor(string controllerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(controllerId))
        {
            return 0;
        }

        if (_slots.TryGetValue(controllerId, out var existing))
        {
            _slots[controllerId] = existing with { LastSeen = now };
            return existing.Slot;
        }

        var taken = _slots.Values.Select(s => s.Slot).ToHashSet();

        var slot = 1;
        while (taken.Contains(slot))
        {
            slot++;
        }

        _slots[controllerId] = new ControllerSlot(slot, now);

        return slot;
    }

    /// <summary>Whether this controller already has a remembered number.</summary>
    public bool Knows(string controllerId) => _slots.ContainsKey(controllerId);

    /// <summary>Forgets one controller, freeing its number.</summary>
    public void Forget(string controllerId) => _slots.Remove(controllerId);

    /// <summary>
    /// Forgets controllers not seen for a while.
    /// </summary>
    /// <remarks>
    /// By age, never by absence. A pad switched off is absent and must keep its number — that is the
    /// whole point of remembering it. Only one that has not appeared for a long time is a guest's,
    /// and holding its number forever is what pushes a household's numbering up into the twenties.
    /// </remarks>
    /// <returns>How many entries were dropped.</returns>
    public int DropUnseenSince(DateTimeOffset cutoff)
    {
        var stale = _slots.Where(pair => pair.Value.LastSeen < cutoff).Select(pair => pair.Key).ToList();

        foreach (var id in stale)
        {
            _slots.Remove(id);
        }

        return stale.Count;
    }

    /// <summary>Everything worth writing to disk.</summary>
    /// <remarks>
    /// Durable keys only. A remembered number filed under an XInput slot would claim, tomorrow,
    /// whichever pad happened to be switched on first.
    /// </remarks>
    public IReadOnlyDictionary<string, ControllerSlot> Persistable()
        => _slots
            .Where(pair => ControllerIdentityFactory.IsStable(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Everything, including what only holds for this session.</summary>
    public IReadOnlyDictionary<string, ControllerSlot> All()
        => new Dictionary<string, ControllerSlot>(_slots, StringComparer.Ordinal);

    /// <summary>Restores saved numbers, refusing any that are not durable.</summary>
    /// <remarks>
    /// Guarded on the way in as well as out: a file written by an older build may hold slot keys, and
    /// trusting them would hand one player's number to another.
    /// </remarks>
    public void Load(IReadOnlyDictionary<string, ControllerSlot>? saved)
    {
        if (saved is null)
        {
            return;
        }

        foreach (var (id, slot) in saved)
        {
            if (ControllerIdentityFactory.IsStable(id) && slot.Slot > 0)
            {
                _slots[id] = slot;
            }
        }
    }
}
