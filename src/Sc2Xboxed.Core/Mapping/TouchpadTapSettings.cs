namespace Sc2Xboxed.Core.Mapping;

public sealed record TouchpadTapSettings
{
    public static TouchpadTapSettings Default { get; } = new();

    public TimeSpan MaxTapDuration { get; init; } = TimeSpan.FromMilliseconds(180);

    public double MaxTravel { get; init; } = 0.12;

    public double MinPressure { get; init; } = 0.0;
}
