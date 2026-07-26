namespace Sc2Xboxed.Core.Haptics;

public sealed class XboxRumbleToSteamHapticsMapper
{
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

        int leftGain = (int)(-24 + rumble.LeftMotor * 30);
        int rightGain = (int)(-24 + rumble.RightMotor * 30);

        return new HapticOutputFrame(new[]
        {
            new HapticCommand(HapticActuator.LeftRumble, HapticType.Rumble, leftGain),
            new HapticCommand(HapticActuator.RightRumble, HapticType.Rumble, rightGain)
        });
    }
}
