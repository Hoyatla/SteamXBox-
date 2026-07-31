namespace Sc2Xboxed.Core.Haptics;

public readonly record struct HapticCommand(
    HapticActuator Actuator,
    HapticType Type,
    int GainDb,
    ushort Frequency = 0,
    ushort DurationMs = 0,
    ushort LfoFreq = 0,
    byte LfoDepth = 0,
    /// <summary>
    /// Pulse on-time in microseconds for <see cref="HapticType.Tick"/> and
    /// <see cref="HapticType.Click"/>. The pulse report carries no gain field on this
    /// firmware, so on-time is how strength is controlled. 0 uses the per-type default.
    /// </summary>
    ushort PulseWidthUs = 0)
{
    public static HapticCommand Stop(HapticActuator actuator)
    {
        return new HapticCommand(actuator, HapticType.Off, 0);
    }

    public static HapticCommand TouchClick(HapticActuator actuator)
    {
        return new HapticCommand(actuator, HapticType.Click, -6);
    }

    public static HapticCommand Tick(HapticActuator actuator)
    {
        return new HapticCommand(actuator, HapticType.Tick, -8);
    }
}
