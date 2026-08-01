namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Everything the Xbox tab tunes besides the button mapping: sticks, triggers and vibration.
/// </summary>
/// <remarks>
/// These controls existed in the interface long before any of them did anything. Each one here has a
/// real effect; nothing is exposed that the runtime ignores.
///
/// Defaults reproduce the previous behaviour exactly — no dead zone beyond the profile's existing
/// stick value, triggers passed straight through, vibration on at full strength — so a profile that
/// changes nothing behaves as before.
/// </remarks>
public sealed class XboxTuning
{
    // ---- Sticks ----

    /// <summary>Below this magnitude a stick axis reports centre. 0 to 0.5.</summary>
    public double StickDeadZone { get; set; } = 0.018;

    /// <summary>
    /// Response curve above the dead zone. 1 is linear; below 1 favours fine aim near centre, above
    /// 1 reaches full deflection sooner.
    /// </summary>
    public double StickCurve { get; set; } = 1.0;

    /// <summary>Multiplier applied after the curve, clamped so full deflection stays reachable.</summary>
    public double StickSensitivity { get; set; } = 1.0;

    // ---- Triggers ----

    /// <summary>Travel below which a trigger reports nothing. Removes a resting-finger creep.</summary>
    public double TriggerThreshold { get; set; } = 0.0;

    /// <summary>
    /// Travel at which a trigger reports full. Pulling it in shortens the throw, which is what a
    /// "hair trigger" does.
    /// </summary>
    public double TriggerFullPoint { get; set; } = 1.0;

    // ---- Vibration ----

    /// <summary>Whether the game's rumble reaches the controller at all.</summary>
    public bool VibrationEnabled { get; set; } = true;

    /// <summary>Scales the rumble the game asks for. 0 silences it without disabling the path.</summary>
    public double VibrationIntensity { get; set; } = 1.0;

    /// <summary>
    /// Whether rumble is also sent to the trackpad actuators, not only the grip motors.
    /// </summary>
    /// <remarks>
    /// Off by default because that is what the runtime did before this was configurable — it only
    /// ever drove the two grip motors. The old interface showed this as "enabled (default)", which
    /// was one more control describing something that was not happening.
    /// </remarks>
    public bool HapticForwarding { get; set; }

    // ---- Trigger haptics (experimental) ----

    /// <summary>
    /// Whether rumble is also sent to the trigger actuators.
    /// </summary>
    /// <remarks>
    /// Off by default, and deliberately so: the haptic report format is reverse-engineered and no
    /// trigger actuator has been confirmed to exist on this firmware. Run
    /// <c>SteamXBox.Core.exe haptic-probe</c> to find out, then set
    /// <see cref="TriggerActuatorIndex"/> to whichever index actually moved something.
    /// </remarks>
    public bool TriggerHapticsEnabled { get; set; }

    /// <summary>Scales what the triggers receive relative to the grip motors.</summary>
    public double TriggerHapticStrength { get; set; } = 0.6;

    /// <summary>
    /// Side byte used to address the left trigger actuator; the right one is this plus one. The
    /// known-good values are 0x00 and 0x01 for the two halves of the controller, so a trigger, if it
    /// exists, is somewhere above.
    /// </summary>
    public int TriggerActuatorIndex { get; set; } = 2;

    // ---- Application ----

    /// <summary>Applies dead zone, curve and sensitivity to one stick axis pair.</summary>
    public (double X, double Y) ApplyStick(double x, double y)
    {
        // Radial, not per-axis: a per-axis dead zone carves a square hole out of a round stick and
        // makes diagonals feel notched.
        var magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude <= 0)
        {
            return (0, 0);
        }

        var dead = Math.Clamp(StickDeadZone, 0.0, 0.5);
        if (magnitude <= dead)
        {
            return (0, 0);
        }

        // Rescale so the first movement past the dead zone starts from zero rather than jumping.
        var scaled = (magnitude - dead) / (1.0 - dead);

        var curve = Math.Clamp(StickCurve, 0.2, 3.0);
        if (Math.Abs(curve - 1.0) > 0.001)
        {
            scaled = Math.Pow(scaled, curve);
        }

        scaled = Math.Min(1.0, scaled * Math.Clamp(StickSensitivity, 0.25, 3.0));

        return (x / magnitude * scaled, y / magnitude * scaled);
    }

    /// <summary>Applies the threshold and full-press point to one trigger.</summary>
    public double ApplyTrigger(double value)
    {
        value = Math.Clamp(value, 0.0, 1.0);

        var low = Math.Clamp(TriggerThreshold, 0.0, 0.95);
        var high = Math.Clamp(TriggerFullPoint, low + 0.05, 1.0);

        if (value <= low)
        {
            return 0.0;
        }

        return Math.Min(1.0, (value - low) / (high - low));
    }

    /// <summary>Scales a rumble amplitude, returning zero when vibration is off.</summary>
    public double ApplyVibration(double amplitude)
        => VibrationEnabled ? Math.Clamp(amplitude, 0.0, 1.0) * Math.Clamp(VibrationIntensity, 0.0, 1.0) : 0.0;
}
