namespace Sc2Xboxed.Core.Mapping;

public sealed record RightTouchpadTrackballSettings
{
    public static RightTouchpadTrackballSettings Default { get; } = new();

    public double PixelsPerPadUnit { get; init; } = 900.0;

    public double MotionDeadZone { get; init; } = 0.0015;

    public double InertiaDecayPerSecond { get; init; } = 8.0;

    public double StopSpeedPixelsPerSecond { get; init; } = 3.0;

    public double MaxSpeedPixelsPerSecond { get; init; } = 5000.0;

    /// <summary>
    /// How many recent frames the throw velocity is measured over. The window is averaged as total
    /// displacement over total time, which tracks a short flick faithfully where an exponential
    /// average would lag behind it and produce a much weaker throw.
    /// </summary>
    public int VelocityWindowFrames { get; init; } = 4;

    /// <summary>
    /// Consecutive sub-dead-zone frames before a pending throw is cancelled. A few quiet frames are a
    /// pause mid-gesture; a longer run means the finger is resting and lifting off must not throw.
    /// </summary>
    /// <remarks>
    /// Must stay above the release tail of <see cref="SmoothedTouchpadInput"/>, which reports three
    /// more "touched" frames with an unchanged position after the finger actually lifts. At 3 this
    /// cancelled the velocity during that tail and killed every single throw — inertia looked
    /// completely absent even though the code was there.
    /// </remarks>
    public int QuietFramesToCancelThrow { get; init; } = 8;

    public bool InvertX { get; init; }

    public bool InvertY { get; init; }

    // ---- Acceleration ----

    /// <summary>
    /// Shapes gain against gesture speed. 1.0 is linear, so acceleration is off by default; above 1
    /// slow movement gets finer and fast movement gets faster, which is what makes a small pad cover
    /// a large screen without losing precision.
    /// </summary>
    public double AccelerationExponent { get; init; } = 1.0;

    /// <summary>Gesture speed, in pad units per second, that maps to a gain of exactly 1.</summary>
    public double AccelerationReferenceSpeed { get; init; } = 1.5;

    /// <summary>
    /// Floor of the acceleration gain, and therefore the fine-pointing ratio: at 0.25 a slow gesture
    /// travels a quarter of the linear distance, which is four times the precision. This replaces a
    /// modifier button entirely — the curve gives it continuously, with no chord to hold.
    /// </summary>
    public double MinAccelerationGain { get; init; } = 0.10;

    public double MaxAccelerationGain { get; init; } = 3.0;

    // ---- Throw gating ----

    /// <summary>
    /// Total travel a gesture must cover, in pixels, before releasing it may throw the cursor.
    /// Without this a deliberate two-pixel adjustment ended in a long glide, because the velocity is
    /// distance over time and a short gesture can still be fast.
    /// </summary>
    public double MinThrowTravelPixels { get; init; } = 70.0;

    // ---- Involuntary contact rejection ----

    /// <summary>
    /// Distance a new contact must travel, in pad units, before it moves the pointer at all. Filters
    /// brushes and resting fingers, which register as a touch and used to nudge the cursor.
    /// </summary>
    public double TouchActivationTravel { get; init; } = 0.024;

    /// <summary>
    /// Distance, in pad units, over which a fresh contact ramps from <see cref="MinAccelerationGain"/>
    /// up to its normal gain.
    /// </summary>
    /// <remarks>
    /// Precision has to key off how far the gesture has travelled, not how fast it is going. A speed
    /// curve gives no help at all to a small but brisk correction — it reads as fast and amplifies it,
    /// which is the opposite of what a fine adjustment needs.
    /// </remarks>
    public double FinePrecisionTravel { get; init; } = 0.20;

    // ---- Click zone ----

    /// <summary>
    /// How long pointer motion stays frozen after the pad is pressed, in milliseconds. Pressing a
    /// touchpad always shifts the finger, and that shift used to drag the cursor off target at the
    /// moment of the click.
    /// </summary>
    /// <remarks>
    /// Only the onset is frozen, not the whole press: holding and moving still drags, which is what
    /// resizing a window or selecting text needs.
    /// </remarks>
    public double ClickSettleMilliseconds { get; init; } = 90.0;

    // ---- Edge continuation ----

    /// <summary>
    /// Distance from centre, 0-1, past which holding still keeps the cursor moving outward. Lets a
    /// short pad reach across a wide screen without repeated lift-and-drag.
    /// </summary>
    public double EdgeThreshold { get; init; } = 0.85;

    /// <summary>Speed of that continuation, in pixels per second. 0 disables it.</summary>
    public double EdgeSpeedPixelsPerSecond { get; init; }
}
