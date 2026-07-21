namespace Sc2Xboxed.Core.Input;

public readonly record struct TouchpadSample(
    bool IsTouched,
    double X,
    double Y,
    double Pressure = 0.0,
    bool IsPressed = false)
{
    public static TouchpadSample Released { get; } = new(false, 0.0, 0.0);

    public TouchpadSample Clamp()
    {
        return IsTouched
            ? new TouchpadSample(
                true,
                Math.Clamp(X, -1.0, 1.0),
                Math.Clamp(Y, -1.0, 1.0),
                Math.Clamp(Pressure, 0.0, 1.0),
                IsPressed)
            : Released;
    }
}
