namespace Sc2Xboxed.Core.Mapping;

public sealed record Sc2XboxedProfileSettings
{
    public static Sc2XboxedProfileSettings Default { get; } = new();

    public double StickDeadZone { get; init; } = 0.08;

    public LeftTouchpadScrollSettings LeftPadScroll { get; init; } = LeftTouchpadScrollSettings.Default;

    public RightTouchpadTrackballSettings RightPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    public TouchpadTapSettings TouchpadTap { get; init; } = TouchpadTapSettings.Default;
}
