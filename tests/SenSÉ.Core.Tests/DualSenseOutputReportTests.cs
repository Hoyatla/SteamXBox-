using System.Text;
using SenSÉ.Core.Input;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The output report that makes a DualSense vibrate.
/// </summary>
/// <remarks>
/// The failure mode is silence. A Bluetooth report with a wrong checksum is not rejected loudly — it
/// is discarded, and the pad simply never buzzes. Nothing at runtime can tell that apart from a
/// broken motor, so the checksum is pinned here against a known CRC-32 value.
/// </remarks>
public class DualSenseOutputReportTests
{
    // CRC-32 (IEEE) of "123456789" is 0xCBF43926 — the standard check value for this variant.
    // Pinning it proves the polynomial and reflection are right before anything relies on them.
    [Fact]
    public void TheChecksumMatchesTheStandardCheckValue()
    {
        var data = Encoding.ASCII.GetBytes("23456789");

        Assert.Equal(0xCBF43926u, DualSenseOutputReport.Crc32((byte)'1', data));
    }

    [Fact]
    public void TheChecksumChangesWithTheData()
        => Assert.NotEqual(
            DualSenseOutputReport.Crc32(0xA2, [1, 2, 3]),
            DualSenseOutputReport.Crc32(0xA2, [1, 2, 4]));

    // The prefix is part of the checksummed data but never part of the report body. Omitting it
    // produces a plausible-looking checksum that is always wrong.
    [Fact]
    public void ThePrefixIsPartOfTheChecksum()
        => Assert.NotEqual(
            DualSenseOutputReport.Crc32(0xA2, [1, 2, 3]),
            DualSenseOutputReport.Crc32(0x00, [1, 2, 3]));

    [Fact]
    public void TheUsbReportHasItsOwnIdAndLength()
    {
        var report = DualSenseOutputReport.Rumble(bluetooth: false, weak: 100, strong: 200);

        Assert.Equal(48, report.Length);
        Assert.Equal(DualSenseOutputReport.UsbReportId, report[0]);
    }

    [Fact]
    public void TheBluetoothReportHasItsOwnIdAndLength()
    {
        var report = DualSenseOutputReport.Rumble(bluetooth: true, weak: 100, strong: 200);

        Assert.Equal(78, report.Length);
        Assert.Equal(DualSenseOutputReport.BluetoothReportId, report[0]);
    }

    [Theory]
    [InlineData(false, 3, 4)]
    [InlineData(true, 5, 6)]
    public void BothMotorStrengthsReachTheReport(bool bluetooth, int weakAt, int strongAt)
    {
        var report = DualSenseOutputReport.Rumble(bluetooth, weak: 111, strong: 222);

        Assert.Equal(111, report[weakAt]);
        Assert.Equal(222, report[strongAt]);
    }

    [Fact]
    public void TheMotorsAreEnabledOrNothingHappens()
    {
        Assert.NotEqual(0, DualSenseOutputReport.Rumble(bluetooth: false, 50, 50)[1]);
        Assert.NotEqual(0, DualSenseOutputReport.Rumble(bluetooth: true, 50, 50)[3]);
    }

    [Fact]
    public void TheBluetoothReportCarriesItsChecksumInTheLastFourBytes()
    {
        var report = DualSenseOutputReport.Rumble(bluetooth: true, weak: 90, strong: 90);
        var expected = DualSenseOutputReport.Crc32(0xA2, report.AsSpan(0, 74));

        Assert.Equal((byte)expected, report[74]);
        Assert.Equal((byte)(expected >> 24), report[77]);
    }

    [Fact]
    public void TwoDifferentStrengthsGiveTwoDifferentChecksums()
    {
        var soft = DualSenseOutputReport.Rumble(bluetooth: true, 10, 10);
        var hard = DualSenseOutputReport.Rumble(bluetooth: true, 200, 200);

        Assert.NotEqual(soft[^4..], hard[^4..]);
    }

    [Fact]
    public void TheSequenceTagSitsInTheHighNibble()
    {
        Assert.Equal(0x50, DualSenseOutputReport.Rumble(bluetooth: true, 0, 0, sequence: 5)[1]);
        Assert.Equal(0xF0, DualSenseOutputReport.Rumble(bluetooth: true, 0, 0, sequence: 0x1F)[1]);
    }

    // A pad told to vibrate and never told to stop keeps going until it is unpaired.
    [Theory]
    [InlineData(false, 3, 4)]
    [InlineData(true, 5, 6)]
    public void SilenceStopsBothMotors(bool bluetooth, int weakAt, int strongAt)
    {
        var report = DualSenseOutputReport.Silence(bluetooth);

        Assert.Equal(0, report[weakAt]);
        Assert.Equal(0, report[strongAt]);
    }

    [Fact]
    public void SilenceIsStillAValidChecksummedReport()
    {
        var report = DualSenseOutputReport.Silence(bluetooth: true);
        var expected = DualSenseOutputReport.Crc32(0xA2, report.AsSpan(0, 74));

        Assert.Equal((byte)expected, report[74]);
    }
}
