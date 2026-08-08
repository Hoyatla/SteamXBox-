using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The Bluetooth feature report that switches a DualSense off.
/// </summary>
/// <remarks>
/// The failure mode is the same as the rumble report: a wrong checksum is silently discarded and the
/// pad simply stays on. The checksum is therefore pinned here against a value computed independently
/// (Python's zlib over the <c>0x53</c> seed and the 43 payload bytes), not by whatever
/// <see cref="DualSenseOutputReport.Crc32"/> would produce by accident.
/// </remarks>
public class DualSensePowerOffTests
{
    [Fact]
    public void TheReportIsTheBluetoothControlReport()
    {
        var report = DualSensePowerOff.Build();

        Assert.Equal(DualSensePowerOff.ReportLength, report.Length);
        Assert.Equal(DualSensePowerOff.BluetoothControlReportId, report[0]);
    }

    [Fact]
    public void TheOffCommandIsSecond()
        => Assert.Equal(DualSensePowerOff.Off, DualSensePowerOff.Build()[1]);

    // CRC-32 of 0x53 ++ [0x08, 0x02] ++ 41 zero bytes = 0x74E22FD1, computed with Python's zlib.
    [Fact]
    public void TheChecksumMatchesAnIndependentComputation()
    {
        var report = DualSensePowerOff.Build();
        var expected = 0x74E22FD1u;

        Assert.Equal((byte)expected, report[^4]);
        Assert.Equal((byte)(expected >> 8), report[^3]);
        Assert.Equal((byte)(expected >> 16), report[^2]);
        Assert.Equal((byte)(expected >> 24), report[^1]);
    }

    [Fact]
    public void TheChecksumIsLittleEndianOverTheSeedAndBody()
    {
        // Recompute the checksum from the report's own contents, the way the pad will: over the
        // 0x53 seed prefix and the 43 bytes before the checksum, stored little-endian last.
        var report = DualSensePowerOff.Build();
        var expected = DualSenseOutputReport.Crc32(0x53, report.AsSpan(0, report.Length - 4));

        Assert.Equal((byte)expected, report[^4]);
        Assert.Equal((byte)(expected >> 8), report[^3]);
        Assert.Equal((byte)(expected >> 16), report[^2]);
        Assert.Equal((byte)(expected >> 24), report[^1]);
    }
}
