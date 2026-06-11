namespace Sc2Xboxed.Core.Haptics;

public sealed class XboxRumbleToSteamHapticsMapper
{
    private const double LowMotorFrequencyHz = 75.0;
    private const double HighMotorFrequencyHz = 160.0;

    public HapticOutputFrame Map(XboxRumbleFrame rumble)
    {
        rumble = rumble.Normalize();

        if (rumble.LeftMotor == 0.0 && rumble.RightMotor == 0.0)
        {
            return new HapticOutputFrame(new[]
            {
                HapticCommand.Stop(HapticActuator.LeftRumble),
                HapticCommand.Stop(HapticActuator.RightRumble)
            });
        }

        return new HapticOutputFrame(new[]
        {
            new HapticCommand(
                HapticActuator.LeftRumble,
                LowMotorFrequencyHz,
                rumble.LeftMotor,
                TimeSpan.FromMilliseconds(80)),
            new HapticCommand(
                HapticActuator.RightRumble,
                HighMotorFrequencyHz,
                rumble.RightMotor,
                TimeSpan.FromMilliseconds(80))
        });
    }
}
