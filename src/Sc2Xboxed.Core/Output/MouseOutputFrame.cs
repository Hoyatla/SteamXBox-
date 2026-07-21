namespace Sc2Xboxed.Core.Output;

public readonly record struct MouseOutputFrame(double DeltaX, double DeltaY, int WheelDelta)
{
    public static MouseOutputFrame Empty { get; } = new(0.0, 0.0, 0);

    public bool HasMouseMotion => Math.Abs(DeltaX) > 0.0001 || Math.Abs(DeltaY) > 0.0001;

    public bool HasWheel => WheelDelta != 0;

    public MouseOutputFrame Add(MouseOutputFrame other)
    {
        return new MouseOutputFrame(
            DeltaX + other.DeltaX,
            DeltaY + other.DeltaY,
            WheelDelta + other.WheelDelta);
    }
}
