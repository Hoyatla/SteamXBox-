namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Withholds the buttons that make up a chord just long enough to tell a chord from a real press.
/// </summary>
/// <remarks>
/// Masking the chord buttons only once both are down is too late. Two buttons pressed "together" are
/// never down on the same frame: the first one arrives a frame or two ahead, its own action fires,
/// and the Start menu is already open by the time the second button joins. That is exactly what the
/// power-off chord did.
///
/// So each chord button is held back for a short grace period after it goes down. If its partner
/// joins within that window, neither is ever released to the mappers. If it does not, the button is
/// released and behaves normally from then on, at the cost of a delay short enough not to be felt.
///
/// A button already released stays released until it is physically let go, so a press does not
/// stutter if the partner is pressed later.
/// </remarks>
public sealed class ChordButtonGate
{
    /// <summary>
    /// How long a chord button is withheld. Long enough to cover the gap between two fingers landing,
    /// short enough that a single press still feels immediate.
    /// </summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMilliseconds(250);

    private readonly SteamControllerButtons _chord;
    private readonly TimeSpan _grace;

    private readonly Dictionary<SteamControllerButtons, DateTimeOffset> _downSince = [];
    private SteamControllerButtons _released;

    public ChordButtonGate(SteamControllerButtons chord, TimeSpan? grace = null)
    {
        _chord = chord;
        _grace = grace ?? DefaultGrace;
    }

    /// <summary>
    /// Returns the buttons the rest of the pipeline should see.
    /// </summary>
    /// <param name="pressed">Buttons physically down this frame.</param>
    /// <param name="now">Frame time.</param>
    /// <param name="chordEngaged">
    /// True once the chord is complete or has fired; while set, no chord button is ever released.
    /// </param>
    public SteamControllerButtons Filter(SteamControllerButtons pressed, DateTimeOffset now, bool chordEngaged)
    {
        var result = pressed;

        foreach (var button in EachChordButton())
        {
            if (!pressed.HasFlag(button))
            {
                _downSince.Remove(button);
                _released &= ~button;
                continue;
            }

            if (_released.HasFlag(button))
            {
                // Already handed over; leave it alone so the press does not stutter.
                continue;
            }

            if (chordEngaged)
            {
                result &= ~button;
                continue;
            }

            if (!_downSince.TryGetValue(button, out var since))
            {
                since = now;
                _downSince[button] = since;
            }

            if (now - since < _grace)
            {
                result &= ~button;
            }
            else
            {
                _released |= button;
            }
        }

        return result;
    }

    public void Reset()
    {
        _downSince.Clear();
        _released = SteamControllerButtons.None;
    }

    private IEnumerable<SteamControllerButtons> EachChordButton()
    {
        foreach (SteamControllerButtons button in Enum.GetValues<SteamControllerButtons>())
        {
            if (button != SteamControllerButtons.None && _chord.HasFlag(button))
            {
                yield return button;
            }
        }
    }
}
