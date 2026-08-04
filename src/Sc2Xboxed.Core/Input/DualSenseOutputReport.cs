namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Builds the output report that makes a DualSense vibrate.
/// </summary>
/// <remarks>
/// Used to answer "which pad is slot 2?" — the only answer that does not ask the user to guess is
/// the pad buzzing in their hands.
///
/// <para>
/// The two transports differ far more here than on the input side. Over USB the report is a short
/// unprotected <c>0x02</c>. Over Bluetooth it is a 78-byte <c>0x31</c> carrying a sequence tag and a
/// <b>CRC-32 the controller verifies</b>: a report with a wrong checksum is not rejected loudly, it
/// is silently discarded, and the pad simply never vibrates. That silence is why the checksum is
/// built here, where it can be tested, rather than assembled at the call site.
/// </para>
///
/// <para>
/// The CRC is computed over a <c>0xA2</c> prefix byte followed by the report — the prefix being the
/// Bluetooth HID output-report tag, which is part of the checksummed data but never sent as part of
/// the report body. Omitting it produces a plausible-looking checksum that is always wrong.
/// </para>
///
/// <para>
/// The byte offsets come from the documented DualSense protocol, not from observation of any
/// particular pad. They are stated as such because the failure mode is silence: nothing here can
/// tell a correct report from one the controller ignores.
/// </para>
/// </remarks>
public static class DualSenseOutputReport
{
    /// <summary>Output report id over USB.</summary>
    public const byte UsbReportId = 0x02;

    /// <summary>Output report id over Bluetooth.</summary>
    public const byte BluetoothReportId = 0x31;

    /// <summary>Length of the Bluetooth output report, checksum included.</summary>
    public const int BluetoothLength = 78;

    /// <summary>Length of the USB output report.</summary>
    public const int UsbLength = 48;

    /// <summary>Prefix byte the Bluetooth checksum covers but the report body does not carry.</summary>
    private const byte BluetoothCrcSeed = 0xA2;

    /// <summary>Enables the two rumble motors and nothing else.</summary>
    private const byte MotorFlags = 0x03;

    /// <summary>
    /// A report that runs both motors at the given strengths.
    /// </summary>
    /// <param name="bluetooth">True for the Bluetooth form, false for USB.</param>
    /// <param name="weak">High-frequency motor, 0 to 255.</param>
    /// <param name="strong">Low-frequency motor, 0 to 255.</param>
    /// <param name="sequence">
    /// Bluetooth sequence tag, 0 to 15. The controller uses it to order reports; a constant value
    /// works but a rotating one is what the protocol expects.
    /// </param>
    public static byte[] Rumble(bool bluetooth, byte weak, byte strong, byte sequence = 0)
    {
        if (!bluetooth)
        {
            var usb = new byte[UsbLength];
            usb[0] = UsbReportId;
            usb[1] = MotorFlags;
            usb[3] = weak;
            usb[4] = strong;
            return usb;
        }

        var report = new byte[BluetoothLength];
        report[0] = BluetoothReportId;

        // High nibble: the sequence tag. Low nibble is reserved and left clear.
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10;
        report[3] = MotorFlags;
        report[5] = weak;
        report[6] = strong;

        WriteChecksum(report);

        return report;
    }

    /// <summary>A report that stops both motors.</summary>
    /// <remarks>
    /// Needed explicitly. The motors do not stop on their own: a pad told to vibrate and never told
    /// to stop keeps going until it is unpaired, which is a worse first impression than no
    /// identification at all.
    /// </remarks>
    public static byte[] Silence(bool bluetooth, byte sequence = 0)
        => Rumble(bluetooth, 0, 0, sequence);

    /// <summary>Appends the CRC-32 the controller verifies, little-endian, over the last four bytes.</summary>
    private static void WriteChecksum(byte[] report)
    {
        var crc = Crc32(BluetoothCrcSeed, report.AsSpan(0, BluetoothLength - 4));

        report[BluetoothLength - 4] = (byte)crc;
        report[BluetoothLength - 3] = (byte)(crc >> 8);
        report[BluetoothLength - 2] = (byte)(crc >> 16);
        report[BluetoothLength - 1] = (byte)(crc >> 24);
    }

    /// <summary>CRC-32 (IEEE 802.3), computed over a prefix byte and then the body.</summary>
    public static uint Crc32(byte prefix, ReadOnlySpan<byte> body)
    {
        var crc = 0xFFFFFFFFu;

        crc = Step(crc, prefix);
        foreach (var b in body)
        {
            crc = Step(crc, b);
        }

        return ~crc;

        static uint Step(uint crc, byte value)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                // Reflected polynomial 0x04C11DB7, which is what this variant of CRC-32 uses.
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            return crc;
        }
    }
}
