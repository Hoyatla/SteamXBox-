namespace Sc2Xboxed.Core.Haptics;

public enum HapticActuator
{
    LeftRumble,
    RightRumble,
    LeftTrackpad,
    RightTrackpad,

    /// <summary>
    /// Trigger actuators, if this firmware has any.
    /// </summary>
    /// <remarks>
    /// Unconfirmed. The haptic report format is reverse-engineered, and only sides 0x00 and 0x01 —
    /// the two halves of the controller — are known to do anything. These are addressed through
    /// <c>XboxTuning.TriggerActuatorIndex</c> so the byte can be changed without a rebuild once
    /// <c>haptic-probe</c> has found which index, if any, moves a trigger.
    /// </remarks>
    LeftTrigger,
    RightTrigger
}

public enum HapticType
{
    Off = 0,
    Tick = 1,
    Click = 2,
    Tone = 3,
    Rumble = 4,
    Noise = 5
}
