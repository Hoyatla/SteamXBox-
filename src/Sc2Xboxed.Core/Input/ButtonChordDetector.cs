namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Fires once when a set of buttons has been held together, continuously, for a given time.
/// </summary>
/// <remarks>
/// Used for the controller power-off chord. Deliberately strict: the hold restarts the moment any
/// button in the set is released, so a chord cannot be assembled by pressing the buttons one after
/// another over several seconds. Powering the controller off mid-game because two buttons happened
/// to overlap would be worse than not having the feature.
///
/// It also fires only once per hold: <see cref="Update"/> keeps returning false until every button
/// has been released, so a long press cannot trigger repeatedly.
/// </remarks>
public sealed class ButtonChordDetector
{
    private readonly SteamControllerButtons _chord;
    private readonly TimeSpan _holdFor;

    private DateTimeOffset? _heldSince;
    private bool _fired;

    public ButtonChordDetector(SteamControllerButtons chord, TimeSpan holdFor)
    {
        _chord = chord;
        _holdFor = holdFor;
    }

    /// <summary>How long the chord has been held, for progress feedback. Zero when it is not held.</summary>
    public TimeSpan HeldFor { get; private set; }

    /// <summary>Returns true on the single frame the chord completes.</summary>
    public bool Update(SteamControllerButtons pressed, DateTimeOffset now)
    {
        var complete = (pressed & _chord) == _chord;

        if (!complete)
        {
            _heldSince = null;
            HeldFor = TimeSpan.Zero;

            // Rearm only once the chord is fully released, so holding past the trigger does nothing.
            if ((pressed & _chord) == SteamControllerButtons.None)
            {
                _fired = false;
            }

            return false;
        }

        _heldSince ??= now;
        HeldFor = now - _heldSince.Value;

        if (_fired || HeldFor < _holdFor)
        {
            return false;
        }

        _fired = true;
        return true;
    }

    public void Reset()
    {
        _heldSince = null;
        HeldFor = TimeSpan.Zero;
        _fired = false;
    }
}
