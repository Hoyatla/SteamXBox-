using HidSharp;

namespace Sc2Xboxed.Hid;

/// <summary>
/// Turns the controller off.
/// </summary>
/// <remarks>
/// This used to write a raw output report — report ID 0x9F, byte 0x01 — through
/// <c>stream.Write</c>, and it did nothing. Every command this project has that demonstrably works,
/// the native-layer enable and disable in <see cref="SteamControllerLizardMode"/>, goes through
/// <c>SetFeature</c> in a 64-byte envelope: report ID 0x01, then the command byte, then the payload
/// length, then the payload. Power-off is sent the same way here.
///
/// The payload is still unverified. On the original Steam Controller the shutdown command carries
/// the four bytes "off!", which is the default below; <c>power-off --probe</c> tries the plausible
/// variants one at a time so the working one can be identified on the hardware rather than guessed.
/// </remarks>
public static class SteamControllerPowerOff
{
    private const byte FeatureReportId = 0x01;
    private const byte CommandPowerOff = 0x9F;
    private const int FeatureReportLength = 64;

    /// <summary>ASCII "off!", the payload the original Steam Controller expects.</summary>
    private static readonly byte[] OffMagic = [0x6F, 0x66, 0x66, 0x21];

    /// <summary>The variants worth trying, in decreasing order of likelihood.</summary>
    public static IReadOnlyList<(string Name, byte[] Report)> Variants { get; } =
    [
        ("feature 0x9F payload \"off!\"", Build(CommandPowerOff, OffMagic)),
        ("feature 0x9F no payload", Build(CommandPowerOff, null)),
        ("feature 0x9F payload 0x01", Build(CommandPowerOff, [0x01])),
        ("feature 0x9E payload \"off!\"", Build(0x9E, OffMagic)),
    ];

    /// <summary>Sends the default variant. Throws if the device refuses the report.</summary>
    public static void Send(HidStream stream, object streamGate)
    {
        lock (streamGate)
        {
            stream.SetFeature(Variants[0].Report);
        }
    }

    /// <summary>Sends one variant by index, for the probe.</summary>
    public static void SendVariant(HidStream stream, object streamGate, int index)
    {
        lock (streamGate)
        {
            stream.SetFeature(Variants[index].Report);
        }
    }

    private static byte[] Build(byte command, IReadOnlyList<byte>? payload)
    {
        var report = new byte[FeatureReportLength];
        report[0] = FeatureReportId;
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
