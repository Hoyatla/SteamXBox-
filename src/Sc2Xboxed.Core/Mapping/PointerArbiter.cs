namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Settles the one pointer between however many controllers are pushing it.
/// </summary>
/// <remarks>
/// Every controller is an input, but the desktop has a single pointer. The rule, decided with the
/// author: no controller owns it and none is locked out — whichever one sends movement moves it. If
/// two send movement in the same window, their contributions cancel and the pointer stalls.
///
/// <para>
/// Cancelling is a deliberate choice over the two obvious alternatives, and both were rejected for
/// the same reason: they fail invisibly.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Electing an owner</b> — the second player's controller does nothing at all, with no way to
/// tell that from a flat battery or a bad pairing.
/// </item>
/// <item>
/// <b>Summing</b> — two people pushing in opposite directions get a pointer that drifts somewhere
/// neither asked for, which reads as the pointer being broken rather than as contention.
/// </item>
/// </list>
/// <para>
/// A pointer that will not move while two hands fight over it is understood in about a second. That
/// is the whole justification: it is the only one of the three that explains itself.
/// </para>
/// </remarks>
public sealed class PointerArbiter
{
    private readonly Dictionary<string, (int X, int Y, int Wheel, int HorizontalWheel)> _pending = new(StringComparer.Ordinal);

    /// <summary>Controllers that asked to move the pointer since the last resolution.</summary>
    public int Contenders => _pending.Count(p => p.Value.X != 0 || p.Value.Y != 0);

    /// <summary>
    /// Records what one controller wants the pointer to do.
    /// </summary>
    /// <remarks>
    /// Accumulated per controller rather than applied straight away. Applying on arrival is what
    /// makes contention invisible: two controllers each moving a little would both be obeyed in
    /// turn, and the pointer would jitter between two intents instead of refusing to take either.
    /// </remarks>
    public void Offer(string controllerId, int pixelsX, int pixelsY, int wheelNotches, int horizontalWheelNotches = 0)
    {
        if (pixelsX == 0 && pixelsY == 0 && wheelNotches == 0 && horizontalWheelNotches == 0)
        {
            return;
        }

        _pending.TryGetValue(controllerId, out var current);
        _pending[controllerId] = (
            current.X + pixelsX,
            current.Y + pixelsY,
            current.Wheel + wheelNotches,
            current.HorizontalWheel + horizontalWheelNotches);
    }

    /// <summary>
    /// What the pointer should actually do, and clears the slate for the next window.
    /// </summary>
    /// <remarks>
    /// The wheels are arbitrated separately from the motion. They are different surfaces of the same
    /// desktop and one is far more often incidental — a thumb resting on a stick produces scroll
    /// long before it produces travel — so letting a stray notch block another player's pointer
    /// would make contention look like a fault.
    /// </remarks>
    public (int PixelsX, int PixelsY, int Wheel, int HorizontalWheel) Resolve()
    {
        var movers = _pending.Where(p => p.Value.X != 0 || p.Value.Y != 0).ToList();
        var scrollers = _pending.Where(p => p.Value.Wheel != 0).ToList();
        var horizontalScrollers = _pending.Where(p => p.Value.HorizontalWheel != 0).ToList();

        var motion = movers.Count == 1 ? (movers[0].Value.X, movers[0].Value.Y) : (0, 0);
        var wheel = scrollers.Count == 1 ? scrollers[0].Value.Wheel : 0;
        var horizontalWheel = horizontalScrollers.Count == 1 ? horizontalScrollers[0].Value.HorizontalWheel : 0;

        _pending.Clear();

        return (motion.Item1, motion.Item2, wheel, horizontalWheel);
    }

    /// <summary>Forgets a controller's pending motion, for one that has gone away mid-push.</summary>
    /// <remarks>
    /// Without this, a controller that disconnects while its stick is deflected leaves a pending
    /// entry that contends with everyone else forever — a pointer frozen by a pad that is no longer
    /// in the room.
    /// </remarks>
    public void Forget(string controllerId) => _pending.Remove(controllerId);
}
