namespace Sc2Xboxed.Core.Mapping;

public sealed record RightTouchpadTrackballSettings
{
    public static RightTouchpadTrackballSettings Default { get; } = new();

    public double PixelsPerPadUnit { get; init; } = 900.0;

    public double MotionDeadZone { get; init; } = 0.0015;

    public double InertiaDecayPerSecond { get; init; } = 8.0;

    public double StopSpeedPixelsPerSecond { get; init; } = 3.0;

    public double MaxSpeedPixelsPerSecond { get; init; } = 5000.0;

    public bool InvertX { get; init; }

    public bool InvertY { get; init; }
}
