using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Hid;

public sealed class TritonHapticReportBuilder
{
    private const byte ReportHapticRumble = 0x80;
    private const byte ReportHapticPulse = 0x81;
    private const byte ReportHapticCommand = 0x82;
    private const byte ReportHapticLfoTone = 0x83;

    public byte[] Build(HapticCommand command, int reportLength)
    {
        return command.Type switch
        {
            HapticType.Off => BuildCommand(command.Actuator, 0, 0, reportLength),
            HapticType.Tick => BuildPulse(command.Actuator, 200, 0, 1, -6, reportLength),
            HapticType.Click => BuildPulse(command.Actuator, 300, 100, 1, -4, reportLength),
            HapticType.Tone => BuildLfoTone(command.Actuator, -4, 200, 50, 200, 100, reportLength),
            HapticType.Rumble => BuildRumble(command.Actuator, (ushort)Math.Clamp(command.GainDb + 24, 0, 30), reportLength),
            HapticType.Noise => BuildCommand(command.Actuator, 5, (sbyte)command.GainDb, reportLength),
            _ => BuildCommand(command.Actuator, 0, 0, reportLength)
        };
    }

    public byte[] BuildStop(HapticActuator actuator, int reportLength)
    {
        return BuildCommand(actuator, 0, 0, reportLength);
    }

    private static byte[] BuildPulse(HapticActuator actuator, ushort onUs, ushort offUs, ushort repeatCount, sbyte gainDb, int reportLength)
    {
        reportLength = Math.Max(8, reportLength);
        var report = new byte[reportLength];
        report[0] = ReportHapticPulse;
        report[1] = ToTritonSide(actuator);
        report[2] = (byte)(onUs & 0xFF);
        report[3] = (byte)(onUs >> 8);
        report[4] = (byte)(offUs & 0xFF);
        report[5] = (byte)(offUs >> 8);
        report[6] = (byte)(repeatCount & 0xFF);
        report[7] = (byte)(repeatCount >> 8);
        return report;
    }

    private static byte[] BuildRumble(HapticActuator actuator, ushort intensity, int reportLength)
    {
        reportLength = Math.Max(10, reportLength);
        var report = new byte[reportLength];
        report[0] = ReportHapticRumble;
        report[1] = 0;
        report[2] = 0;
        report[3] = (byte)(intensity & 0xFF);
        report[4] = (byte)(intensity >> 8);

        byte side = actuator switch
        {
            HapticActuator.LeftRumble or HapticActuator.LeftTrackpad => 0,
            _ => 1
        };

        if (side == 0)
        {
            report[5] = (byte)(intensity & 0xFF);
            report[6] = (byte)(intensity >> 8);
            report[7] = 0;
            report[8] = 0;
        }
        else
        {
            report[5] = 0;
            report[6] = 0;
            report[7] = (byte)(intensity & 0xFF);
            report[8] = (byte)(intensity >> 8);
        }

        report[9] = 0;
        return report;
    }

    private static byte[] BuildLfoTone(HapticActuator actuator, sbyte gainDb, ushort frequency, ushort durationMs, ushort lfoFreq, byte lfoDepth, int reportLength)
    {
        reportLength = Math.Max(10, reportLength);
        var report = new byte[reportLength];
        report[0] = ReportHapticLfoTone;
        report[1] = ToTritonSide(actuator);
        report[2] = (byte)gainDb;
        report[3] = (byte)(frequency & 0xFF);
        report[4] = (byte)(frequency >> 8);
        report[5] = (byte)(durationMs & 0xFF);
        report[6] = (byte)(durationMs >> 8);
        report[7] = (byte)(lfoFreq & 0xFF);
        report[8] = (byte)(lfoFreq >> 8);
        report[9] = lfoDepth;
        return report;
    }

    private static byte[] BuildCommand(HapticActuator actuator, byte command, sbyte gainDb, int reportLength)
    {
        reportLength = Math.Max(4, reportLength);
        var report = new byte[reportLength];
        report[0] = ReportHapticCommand;
        report[1] = ToTritonSide(actuator);
        report[2] = command;
        report[3] = (byte)gainDb;
        return report;
    }

    private static byte ToTritonSide(HapticActuator actuator)
    {
        return actuator switch
        {
            HapticActuator.LeftRumble or HapticActuator.LeftTrackpad => 0x01,
            HapticActuator.RightRumble or HapticActuator.RightTrackpad => 0x02,
            _ => throw new ArgumentOutOfRangeException(nameof(actuator), actuator, null)
        };
    }
}
