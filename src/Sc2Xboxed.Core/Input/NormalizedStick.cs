namespace Sc2Xboxed.Core.Input;

public readonly record struct NormalizedStick(double X, double Y)
{
    public static NormalizedStick Center { get; } = new(0.0, 0.0);

    public NormalizedStick Clamp()
    {
        return new NormalizedStick(ClampAxis(X), ClampAxis(Y));
    }

    private static double ClampAxis(double value)
    {
        return Math.Clamp(value, -1.0, 1.0);
    }
}
