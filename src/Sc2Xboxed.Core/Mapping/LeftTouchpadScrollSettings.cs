namespace Sc2Xboxed.Core.Mapping;

public sealed record LeftTouchpadScrollSettings
{
    public static LeftTouchpadScrollSettings Default { get; } = new();

    public double WheelDeltaPerPadUnit { get; init; } = 600.0;

    public double MotionDeadZone { get; init; } = 0.002;

    public bool InvertVertical { get; init; }
}
