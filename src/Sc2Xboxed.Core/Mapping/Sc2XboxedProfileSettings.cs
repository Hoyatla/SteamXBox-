namespace Sc2Xboxed.Core.Mapping;

public sealed record Sc2XboxedProfileSettings
{
    // Values arrived at by tuning against the hardware; the profile editor's "Default" mirrors them.
    public static Sc2XboxedProfileSettings Default { get; } = new()
    {
        StickDeadZone = 0.06,
        GamepadStickDeadZone = 0.018,
        RightPadTrackball = RightTouchpadTrackballSettings.Default with
        {
            InvertY = true,
            PixelsPerPadUnit = 380.0,
            MotionDeadZone = 0.00015,
            AccelerationExponent = 2.0,
            EdgeSpeedPixelsPerSecond = 750.0,
            InertiaDecayPerSecond = 2.0,
        },
        LeftPadTrackball = RightTouchpadTrackballSettings.Default with
        {
            InvertY = true,
            AccelerationExponent = 2.0,
            InertiaDecayPerSecond = 2.0,
        },
        LeftPadScroll = LeftTouchpadScrollSettings.Default with
        {
            InvertVertical = true,
            WheelDeltaPerPadUnit = 4.8,
            MotionDeadZone = 0.002,
            AccelerationExponent = 1.5,
            InertiaDecayPerSecond = 2.0,
        },
    };

    public double StickDeadZone { get; init; } = 0.06;

    public double GamepadStickDeadZone { get; init; } = 0.018;

    // ---- What each control drives ----
    // Previously hardcoded in ProfileMapper while the profile editor wrote a "motions" section that
    // nothing ever read, so the three dropdowns had no effect at all.

    public PadMotionMode RightPadMode { get; init; } = PadMotionMode.Trackball;

    public PadMotionMode LeftPadMode { get; init; } = PadMotionMode.Scroll;

    public StickMotionMode LeftStickMode { get; init; } = StickMotionMode.ArrowKeys;

    /// <summary>
    /// Drives the mouse pointer on stick-only controllers (PS5/Xbox) while Pointer mode is active.
    /// Never mapped onto a touchpad: those families have no pads.
    /// </summary>
    public StickMotionMode RightStickMode { get; init; } = StickMotionMode.Pointer;

    public LeftTouchpadScrollSettings LeftPadScroll { get; init; } = LeftTouchpadScrollSettings.Default;

    public RightTouchpadTrackballSettings RightPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    /// <summary>
    /// Used when the left pad is set to trackball. Separate from the right pad's settings, which it
    /// used to borrow wholesale, including that pad's sensitivity and invert flags.
    /// </summary>
    public RightTouchpadTrackballSettings LeftPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    public PadHapticSettings LeftPadHaptics { get; init; } = PadHapticSettings.Default;

    public PadHapticSettings RightPadHaptics { get; init; } = PadHapticSettings.Default;

    public TouchpadTapSettings TouchpadTap { get; init; } = TouchpadTapSettings.Default;

    // ---- Xbox360 mode ----
    // Carried inside the desktop profile since the merge: one file moves desktop mapping and the
    // gamepad layout together, and the runtime no longer needs a separate --xbox-profile argument.

    /// <summary>Physical button name to Xbox 360 button name.</summary>
    public Dictionary<string, string> XboxButtons { get; init; } = XboxButtonMap.Default.ToDictionary();

    public XboxTuning XboxTuning { get; init; } = new();
}
