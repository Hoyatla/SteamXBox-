namespace Sc2Xboxed.Core.Haptics;

public readonly record struct XboxRumbleFrame(double LeftMotor, double RightMotor)
{
    public static XboxRumbleFrame Silent { get; } = new(0.0, 0.0);

    public XboxRumbleFrame Normalize()
    {
        return new XboxRumbleFrame(
            Math.Clamp(LeftMotor, 0.0, 1.0),
            Math.Clamp(RightMotor, 0.0, 1.0));
    }
}
