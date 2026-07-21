using HidSharp;

namespace Sc2Xboxed.Hid;

public static class SteamControllerLizardMode
{
    private const byte FeatureReportCommand = 0x01;
    private const byte CommandClearDigitalMappings = 0x81;
    private const byte CommandSetDefaultMappings = 0x85;
    private const byte CommandSetSettings = 0x87;
    private const byte SettingRightTrackpadMode = 0x07;
    private const byte SettingLeftTrackpadMode = 0x08;
    private const byte TrackpadNone = 0x00;
    private const int FeatureReportLength = 64;

    public static void Disable(HidStream stream, object streamGate)
    {
        lock (streamGate)
        {
            stream.SetFeature(BuildCommand(CommandClearDigitalMappings));

            stream.SetFeature(BuildCommand(
                CommandSetSettings,
                new byte[]
                {
                    SettingLeftTrackpadMode, TrackpadNone, 0x00,
                    SettingRightTrackpadMode, TrackpadNone, 0x00
                }));
        }
    }

    public static void Enable(HidStream stream, object streamGate)
    {
        lock (streamGate)
        {
            stream.SetFeature(BuildCommand(CommandSetDefaultMappings));
        }
    }

    public static byte[] BuildHeartbeatCommand()
    {
        return BuildCommand(CommandClearDigitalMappings);
    }

    private static byte[] BuildCommand(byte command, IReadOnlyList<byte>? payload = null)
    {
        var report = new byte[FeatureReportLength];
        report[0] = FeatureReportCommand;
        report[1] = command;

        if (payload is { Count: > 0 })
        {
            report[2] = checked((byte)payload.Count);
            for (var index = 0; index < payload.Count; index++)
            {
                report[index + 3] = payload[index];
            }
        }

        return report;
    }
}
