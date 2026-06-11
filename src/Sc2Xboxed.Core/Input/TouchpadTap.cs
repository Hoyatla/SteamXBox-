namespace Sc2Xboxed.Core.Input;

public readonly record struct TouchpadTap(
    bool WasTapped,
    double X,
    double Y,
    TimeSpan Timestamp)
{
    public static TouchpadTap None { get; } = new(false, 0.0, 0.0, TimeSpan.Zero);
}
