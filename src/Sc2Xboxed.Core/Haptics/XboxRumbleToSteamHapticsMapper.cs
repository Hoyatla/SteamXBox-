using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Haptics;

public sealed class XboxRumbleToSteamHapticsMapper
{
    /// <summary>
    /// Tuning applied to the game's rumble before it reaches the controller: enable, intensity,
    /// trackpad forwarding and the experimental trigger channel.
    /// </summary>
    public XboxTuning Tuning { get; set; } = new();

    public HapticOutputFrame Map(XboxRumbleFrame rumble)
    {
        rumble = rumble.Normalize();

        var left = Tuning.ApplyVibration(rumble.LeftMotor);
        var right = Tuning.ApplyVibration(rumble.RightMotor);

        if (left == 0.0 && right == 0.0)
        {
            return new HapticOutputFrame(StopCommands().ToArray());
        }

        var commands = new List<HapticCommand>
        {
            new(HapticActuator.LeftRumble, HapticType.Rumble, ToGain(left)),
            new(HapticActuator.RightRumble, HapticType.Rumble, ToGain(right)),
        };

        if (Tuning.HapticForwarding)
        {
            commands.Add(new HapticCommand(HapticActuator.LeftTrackpad, HapticType.Rumble, ToGain(left)));
            commands.Add(new HapticCommand(HapticActuator.RightTrackpad, HapticType.Rumble, ToGain(right)));
        }

        if (Tuning.TriggerHapticsEnabled)
        {
            var strength = Math.Clamp(Tuning.TriggerHapticStrength, 0.0, 1.0);
            commands.Add(new HapticCommand(HapticActuator.LeftTrigger, HapticType.Rumble, ToGain(left * strength)));
            commands.Add(new HapticCommand(HapticActuator.RightTrigger, HapticType.Rumble, ToGain(right * strength)));
        }

        return new HapticOutputFrame(commands.ToArray());
    }

    /// <summary>
    /// Stops every channel this mapper can drive, not only the two it is currently using: turning a
    /// channel off in the profile has to silence it rather than leave it running its last command.
    /// </summary>
    private IEnumerable<HapticCommand> StopCommands()
    {
        yield return HapticCommand.Stop(HapticActuator.LeftRumble);
        yield return HapticCommand.Stop(HapticActuator.RightRumble);
        yield return HapticCommand.Stop(HapticActuator.LeftTrackpad);
        yield return HapticCommand.Stop(HapticActuator.RightTrackpad);

        if (Tuning.TriggerHapticsEnabled)
        {
            yield return HapticCommand.Stop(HapticActuator.LeftTrigger);
            yield return HapticCommand.Stop(HapticActuator.RightTrigger);
        }
    }

    /// <summary>Amplitude 0..1 to the device's decibel gain range.</summary>
    private static int ToGain(double amplitude) => (int)(-24 + Math.Clamp(amplitude, 0.0, 1.0) * 30);
}
