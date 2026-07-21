using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Hid;

namespace Sc2Xboxed.Core.Tests;

public sealed class TritonInputReportParserTests
{
    [Fact]
    public void ParsesSteamController2026BleStateReport()
    {
        var report = new byte[54];
        report[0] = 0x45;
        report[1] = 0x10;

        WriteUInt32(report, 2,
            0x00020000 | // L4
            0x00000080 | // R4
            0x02000000 | // left pad touch
            0x04000000); // left pad click

        WriteInt16(report, 6, 16384);
        WriteInt16(report, 8, 32767);
        WriteInt16(report, 10, 32767);
        WriteInt16(report, 12, -32768);
        WriteInt16(report, 14, -16384);
        WriteInt16(report, 16, 0);
        WriteInt16(report, 18, 16384);
        WriteInt16(report, 20, -16384);
        WriteUInt16(report, 22, 8192);

        var parser = new TritonInputReportParser();

        var parsed = parser.TryParse(report, TimeSpan.FromSeconds(1), out var state);

        Assert.True(parsed);
        Assert.True(state.Buttons.HasFlag(SteamControllerButtons.L4));
        Assert.True(state.Buttons.HasFlag(SteamControllerButtons.R4));
        Assert.Equal(0.5, state.LeftTrigger, precision: 3);
        Assert.Equal(1.0, state.RightTrigger, precision: 3);
        Assert.Equal(1.0, state.LeftStick.X, precision: 3);
        Assert.Equal(-1.0, state.LeftStick.Y, precision: 3);
        Assert.True(state.LeftPad.IsTouched);
        Assert.True(state.LeftPad.IsPressed);
        Assert.Equal(0.5, state.LeftPad.X, precision: 3);
        Assert.Equal(0.5, state.LeftPad.Y, precision: 3);
        Assert.Equal(0.25, state.LeftPad.Pressure, precision: 3);
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)(value >> 24);
    }
}
