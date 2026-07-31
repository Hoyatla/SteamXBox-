namespace Sc2Xboxed.Core.Mapping;

public sealed record LeftTouchpadScrollSettings
{
    public static LeftTouchpadScrollSettings Default { get; } = new();

    public double WheelDeltaPerPadUnit { get; init; } = 600.0;

    public double MotionDeadZone { get; init; } = 0.002;

    public bool InvertVertical { get; init; }

    /// <summary>
    /// How long wheel output stays frozen after the pad is pressed, in milliseconds. Same purpose as
    /// the trackball's: pressing shifts the finger, and that shift used to scroll the page at the
    /// exact moment of the click. Only the onset is frozen, so holding and moving still scrolls.
    /// </summary>
    public double ClickSettleMilliseconds { get; init; } = 90.0;

    /// <summary>Use the pad's X axis for horizontal scrolling. The axis was previously unused.</summary>
    public bool HorizontalEnabled { get; init; }

    public bool InvertHorizontal { get; init; }

    /// <summary>
    /// Speed-dependent gain, same shape as the trackball's. 1.0 is linear, so it is off by default.
    /// </summary>
    public double AccelerationExponent { get; init; } = 1.0;

    /// <summary>Gesture speed, in pad units per second, that maps to a gain of exactly 1.</summary>
    public double AccelerationReferenceSpeed { get; init; } = 1.5;

    public double MinAccelerationGain { get; init; } = 0.35;

    public double MaxAccelerationGain { get; init; } = 3.0;

    /// <summary>Exponential decay of the scroll throw after the finger lifts.</summary>
    public double InertiaDecayPerSecond { get; init; } = 6.0;

    /// <summary>Speed below which the throw stops, in wheel units per second.</summary>
    public double StopSpeedUnitsPerSecond { get; init; } = 1.5;

    /// <summary>
    /// Wheel units a gesture must cover before releasing it may coast. Stops a tiny movement from
    /// launching a scroll.
    /// </summary>
    public double MinThrowTravelUnits { get; init; } = 3.0;

    /// <summary>
    /// How consistent the gesture's direction must be, 0-1, for a throw to be allowed: net
    /// displacement divided by total distance travelled.
    /// </summary>
    /// <remarks>
    /// Lifting a finger off a touchpad usually drags it backwards a little. Those last reversed
    /// frames could outweigh the gesture and send the coast the wrong way, which reads as the page
    /// scrolling back on its own. Requiring a mostly one-way gesture rejects exactly that.
    /// </remarks>
    public double MinThrowDirectionCoherence { get; init; } = 0.75;

    /// <summary>Upper bound on the throw speed, in wheel units per second.</summary>
    public double MaxSpeedUnitsPerSecond { get; init; } = 4000.0;

    /// <summary>
    /// Hard ceiling on how many notches a single throw may emit after the finger lifts, regardless of
    /// sensitivity. Without it the coast length scales with <see cref="WheelDeltaPerPadUnit"/>, so a
    /// high sensitivity turns every flick into hundreds of notches of overshoot.
    /// </summary>
    public int MaxCoastNotches { get; init; } = 12;

    /// <summary>
    /// Frames the throw velocity is measured over. See
    /// <see cref="RightTouchpadTrackballSettings.VelocityWindowFrames"/>.
    /// </summary>
    public int VelocityWindowFrames { get; init; } = 4;
}
