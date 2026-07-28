namespace Sc2Xboxed.Core.Mapping;

public sealed record Sc2XboxedProfileSettings
{
    public static Sc2XboxedProfileSettings Default { get; } = new()
    {
        RightPadTrackball = RightTouchpadTrackballSettings.Default with { InvertY = true },
        LeftPadScroll = LeftTouchpadScrollSettings.Default with { InvertVertical = true, WheelDeltaPerPadUnit = 10.0 },
    };

    public double StickDeadZone { get; init; } = 0.5;

    public double GamepadStickDeadZone { get; init; } = 0.08;

    public LeftTouchpadScrollSettings LeftPadScroll { get; init; } = LeftTouchpadScrollSettings.Default;

    public RightTouchpadTrackballSettings RightPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    public TouchpadTapSettings TouchpadTap { get; init; } = TouchpadTapSettings.Default;
}
