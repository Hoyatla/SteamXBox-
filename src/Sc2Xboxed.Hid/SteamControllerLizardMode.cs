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

    /// <summary>
    /// Says again everything <see cref="Disable"/> said.
    /// </summary>
    /// <remarks>
    /// <b>The heartbeat is the disable, repeated — and that is the whole design.</b> The controller
    /// re-arms its own keyboard and mouse emulation when nothing tells it not to, which is why this
    /// is a beat and not a single command. That property is worth more than it looks: it means a
    /// SteamXBox that dies leaves the controller to fix itself within a second, with nothing to
    /// repair and nobody to repair it.
    ///
    /// <para>
    /// It only held for half of what was changed. <see cref="Disable"/> clears the digital mappings
    /// <b>and</b> switches both trackpads off; the beat repeated the mappings alone. So a crash left
    /// a controller whose buttons came back and whose trackpads did not — working enough to look
    /// fine, broken enough to be useless, and nothing on screen to connect that to SteamXBox.
    /// </para>
    ///
    /// <para>
    /// Sending the same thing rather than a copy of it: two lists that must agree will not, and the
    /// day they part is the day somebody adds a third setting to <see cref="Disable"/>.
    /// </para>
    /// </remarks>
    public static void Beat(HidStream stream, object streamGate) => Disable(stream, streamGate);

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
