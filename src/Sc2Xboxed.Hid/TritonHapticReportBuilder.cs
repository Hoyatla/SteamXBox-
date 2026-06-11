using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Hid;

public sealed class TritonHapticReportBuilder
{
    private const byte StopReportId = 0x82;
    private const byte PlayReportId = 0x83;
    private const int ReportLength = 65;

    public byte[] Build(HapticCommand command)
    {
        return Build(command, ReportLength);
    }

    public byte[] Build(HapticCommand command, int reportLength)
    {
        command = command.Normalize();
        reportLength = Math.Max(7, reportLength);

        return command.Amplitude <= 0.0 || command.FrequencyHz <= 0.0 || command.Duration == TimeSpan.Zero
            ? BuildStop(command.Actuator, reportLength)
            : BuildPlay(command, reportLength);
    }

    public byte[] BuildStop(HapticActuator actuator)
    {
        return BuildStop(actuator, ReportLength);
    }

    public byte[] BuildStop(HapticActuator actuator, int reportLength)
    {
        var report = new byte[Math.Max(2, reportLength)];
        report[0] = StopReportId;
        report[1] = ToTritonActuatorId(actuator);
        return report;
    }

    private static byte[] BuildPlay(HapticCommand command, int reportLength)
    {
        var report = new byte[reportLength];
        var frequency = (ushort)Math.Round(Math.Clamp(command.FrequencyHz, 1.0, ushort.MaxValue));

        report[0] = PlayReportId;
        report[1] = ToTritonActuatorId(command.Actuator);
        report[2] = ToConservativeGain(command.Amplitude);
        report[3] = (byte)(frequency & 0xFF);
        report[4] = (byte)(frequency >> 8);
        report[5] = 0xFF;
        report[6] = 0x7F;

        return report;
    }

    private static byte ToTritonActuatorId(HapticActuator actuator)
    {
        return actuator switch
        {
            HapticActuator.LeftRumble => 0,
            HapticActuator.RightRumble => 1,
            HapticActuator.LeftTrackpad => 3,
            HapticActuator.RightTrackpad => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(actuator), actuator, null)
        };
    }

    private static byte ToConservativeGain(double amplitude)
    {
        return (byte)Math.Round(0x80 + (Math.Clamp(amplitude, 0.0, 1.0) * 0x40));
    }
}
