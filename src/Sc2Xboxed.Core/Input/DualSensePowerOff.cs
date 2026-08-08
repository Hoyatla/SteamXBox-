namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Builds the feature report that switches a DualSense off.
/// </summary>
/// <remarks>
/// This is the Bluetooth control feature report: id <c>0x08</c>, first payload byte <c>0x02</c>
/// ("off"). It is the same command <c>dualsensectl</c> sends and the one Steam uses for the same
/// job, and it only works over Bluetooth — a DualSense wired over USB is powered by the cable and
/// cannot be switched off from the host, so <see cref="Sc2Xboxed.Hid.DualSenseControllerSource"/>
/// refuses to send it unless the pad is on a Bluetooth transport.
///
/// <para>
/// On Windows, Bluetooth feature reports surface as one byte longer than the Linux wire form: the
/// <c>0x08</c> report is 48 bytes here (47 over the HCI link). HidD_SetFeature rejects the 47-byte
/// form with invalid-parameter, so the report is built to the Windows size.
/// </para>
///
/// <para>
/// The Bluetooth form carries a CRC-32 the controller verifies, computed over a <c>0xA3</c> prefix
/// byte (the DualSense feature-report seed; <c>0x53</c> is the DualShock 4's) followed by the 44
/// bytes before the checksum. The seed was confirmed against the real controller by validating the
/// checksum of the calibration and pairing-info feature reports it returns. A wrong seed or length
/// produces a checksum the controller silently discards — the pad simply stays on.
/// </para>
/// </remarks>
public static class DualSensePowerOff
{
    /// <summary>Feature report id of the Bluetooth control channel.</summary>
    public const byte BluetoothControlReportId = 0x08;

    /// <summary>Command value that switches the controller off.</summary>
    public const byte Off = 0x02;

    /// <summary>Length of the feature report Windows expects, checksum included.</summary>
    public const int ReportLength = 48;

    /// <summary>Prefix byte the Bluetooth checksum covers but the report body does not carry.</summary>
    private const byte FeatureCrcSeed = 0xA3;

    /// <summary>
    /// A report that switches the controller off, checksummed for Bluetooth.
    /// </summary>
    public static byte[] Build()
    {
        var report = new byte[ReportLength];
        report[0] = BluetoothControlReportId;
        report[1] = Off;

        var crc = DualSenseOutputReport.Crc32(FeatureCrcSeed, report.AsSpan(0, ReportLength - 4));
        report[ReportLength - 4] = (byte)crc;
        report[ReportLength - 3] = (byte)(crc >> 8);
        report[ReportLength - 2] = (byte)(crc >> 16);
        report[ReportLength - 1] = (byte)(crc >> 24);

        return report;
    }
}
