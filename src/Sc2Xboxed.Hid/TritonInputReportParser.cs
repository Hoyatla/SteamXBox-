using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Hid;

public sealed class TritonInputReportParser
{
    private const byte ControllerStateBleReportId = 0x45;
    private const byte ControllerStateReportId = 0x42;
    private const byte ControllerStateTimestampReportId = 0x47;

    private const uint ButtonA = 0x00000001;
    private const uint ButtonB = 0x00000002;
    private const uint ButtonX = 0x00000004;
    private const uint ButtonY = 0x00000008;
    private const uint ButtonQuickAccess = 0x00000010;
    private const uint ButtonR3 = 0x00000020;
    private const uint ButtonView = 0x00000040;
    private const uint ButtonR4 = 0x00000080;
    private const uint ButtonR5 = 0x00000100;
    private const uint ButtonRightBumper = 0x00000200;
    private const uint ButtonDPadDown = 0x00000400;
    private const uint ButtonDPadRight = 0x00000800;
    private const uint ButtonDPadLeft = 0x00001000;
    private const uint ButtonDPadUp = 0x00002000;
    private const uint ButtonMenu = 0x00004000;
    private const uint ButtonL3 = 0x00008000;
    private const uint ButtonSteam = 0x00010000;
    private const uint ButtonL4 = 0x00020000;
    private const uint ButtonL5 = 0x00040000;
    private const uint ButtonLeftBumper = 0x00080000;
    private const uint ButtonRightTouchpadTouch = 0x00200000;
    private const uint ButtonRightTouchpadClick = 0x00400000;
    private const uint ButtonLeftTouchpadTouch = 0x02000000;
    private const uint ButtonLeftTouchpadClick = 0x04000000;

    public bool TryParse(ReadOnlySpan<byte> report, TimeSpan timestamp, out SteamControllerState state)
    {
        state = SteamControllerState.Empty(timestamp);

        if (report.Length < 30)
        {
            return false;
        }

        var reportId = report[0];
        if (reportId is not ControllerStateReportId and not ControllerStateBleReportId and not ControllerStateTimestampReportId)
        {
            return false;
        }

        var payloadOffset = 1;
        var buttons = ReadUInt32(report, payloadOffset + 1);
        var touchpadOffset = reportId == ControllerStateTimestampReportId ? payloadOffset + 19 : payloadOffset + 17;

        if (report.Length < touchpadOffset + 12)
        {
            return false;
        }

        state = new SteamControllerState(
            timestamp,
            MapButtons(buttons),
            new NormalizedStick(
                ToAxis(ReadInt16(report, payloadOffset + 9)),
                ToAxis(ReadInt16(report, payloadOffset + 11))),
            new NormalizedStick(
                ToAxis(ReadInt16(report, payloadOffset + 13)),
                ToAxis(ReadInt16(report, payloadOffset + 15))),
            ToTrigger(ReadInt16(report, payloadOffset + 5)),
            ToTrigger(ReadInt16(report, payloadOffset + 7)),
            ReadTouchpad(
                report,
                touchpadOffset,
                buttons,
                ButtonLeftTouchpadTouch,
                ButtonLeftTouchpadClick),
            ReadTouchpad(
                report,
                touchpadOffset + 6,
                buttons,
                ButtonRightTouchpadTouch,
                ButtonRightTouchpadClick));

        return true;
    }

    private static SteamControllerButtons MapButtons(uint buttons)
    {
        var mapped = SteamControllerButtons.None;

        mapped |= Has(buttons, ButtonA) ? SteamControllerButtons.A : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonB) ? SteamControllerButtons.B : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonX) ? SteamControllerButtons.X : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonY) ? SteamControllerButtons.Y : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonLeftBumper) ? SteamControllerButtons.LeftBumper : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonRightBumper) ? SteamControllerButtons.RightBumper : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonL3) ? SteamControllerButtons.LeftStick : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonR3) ? SteamControllerButtons.RightStick : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonMenu) ? SteamControllerButtons.Menu : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonView) ? SteamControllerButtons.View : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonSteam) ? SteamControllerButtons.Steam : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonQuickAccess) ? SteamControllerButtons.QuickAccess : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonDPadUp) ? SteamControllerButtons.DPadUp : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonDPadDown) ? SteamControllerButtons.DPadDown : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonDPadLeft) ? SteamControllerButtons.DPadLeft : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonDPadRight) ? SteamControllerButtons.DPadRight : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonL4) ? SteamControllerButtons.L4 : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonR4) ? SteamControllerButtons.R4 : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonL5) ? SteamControllerButtons.L5 : SteamControllerButtons.None;
        mapped |= Has(buttons, ButtonR5) ? SteamControllerButtons.R5 : SteamControllerButtons.None;

        return mapped;
    }

    private static TouchpadSample ReadTouchpad(
        ReadOnlySpan<byte> report,
        int offset,
        uint buttons,
        uint touchBit,
        uint clickBit)
    {
        var touched = Has(buttons, touchBit);
        return new TouchpadSample(
            touched,
            ToAxis(ReadInt16(report, offset)),
            -ToAxis(ReadInt16(report, offset + 2)),
            ToPressure(ReadUInt16(report, offset + 4)),
            Has(buttons, clickBit));
    }

    private static bool Has(uint value, uint bit)
    {
        return (value & bit) != 0;
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (short)(data[offset] | (data[offset + 1] << 8));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)(data[offset] |
            (data[offset + 1] << 8) |
            (data[offset + 2] << 16) |
            (data[offset + 3] << 24));
    }

    private static double ToAxis(short value)
    {
        return Math.Clamp(value / 32768.0, -1.0, 1.0);
    }

    private static double ToTrigger(short value)
    {
        return Math.Clamp(value / 32767.0, 0.0, 1.0);
    }

    private static double ToPressure(ushort value)
    {
        return Math.Clamp(value / 32768.0, 0.0, 1.0);
    }
}
