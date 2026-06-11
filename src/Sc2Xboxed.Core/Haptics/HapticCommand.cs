namespace Sc2Xboxed.Core.Haptics;

public readonly record struct HapticCommand(
    HapticActuator Actuator,
    double FrequencyHz,
    double Amplitude,
    TimeSpan Duration)
{
    public static HapticCommand Stop(HapticActuator actuator)
    {
        return new HapticCommand(actuator, 0.0, 0.0, TimeSpan.Zero);
    }

    public HapticCommand Normalize()
    {
        return new HapticCommand(
            Actuator,
            Math.Clamp(FrequencyHz, 0.0, 1000.0),
            Math.Clamp(Amplitude, 0.0, 1.0),
            Duration < TimeSpan.Zero ? TimeSpan.Zero : Duration);
    }
}
