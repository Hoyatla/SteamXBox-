using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Decoding a DualSense input report.
/// </summary>
/// <remarks>
/// A PlayStation 5 pad is not an XInput device, so none of the Xbox path applies to it. The failure
/// mode worth guarding here is silence: a wrong transport offset reads the Y axis as X and a d-pad
/// treated as a bitfield walks in one direction forever. Neither throws.
/// </remarks>
public class DualSenseReportParserTests
{
    private const byte Centre = 128;

    /// <summary>A USB report with everything at rest, ready to be poked at.</summary>
    /// <remarks>
    /// Full length on purpose. Report <c>0x01</c> is used by both transports and only its length
    /// tells them apart, so a short fixture here would be decoded as the compact Bluetooth report
    /// and would test the wrong layout.
    /// </remarks>
    private static byte[] Usb()
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[1] = Centre;  // LX
        report[2] = Centre;  // LY
        report[3] = Centre;  // RX
        report[4] = Centre;  // RY
        report[5] = 0x08;    // d-pad centred, no face buttons
        return report;
    }

    /// <summary>The same report over Bluetooth: one sequence byte pushes everything along.</summary>
    private static byte[] Bluetooth()
    {
        var report = new byte[12];
        report[0] = 0x31;
        report[2] = Centre;
        report[3] = Centre;
        report[4] = Centre;
        report[5] = Centre;
        report[9] = 0x08;
        return report;
    }

    [Fact]
    public void ARestingPadReportsNothing()
    {
        var state = DualSenseReportParser.Parse(Usb(), TimeSpan.Zero);

        Assert.NotNull(state);
        Assert.Equal(0, state!.Value.LeftStick.X);
        Assert.Equal(0, state.Value.LeftStick.Y);
        Assert.Equal(SteamControllerButtons.None, state.Value.Buttons);
    }

    // A pad sitting untouched does not report exactly 128. Left raw, that is a permanent slow drift
    // — mistaken for a mapping bug three times on this project already.
    [Fact]
    public void SmallOffsetsAtRestAreFlattened()
    {
        var report = Usb();
        report[1] = 130;
        report[2] = 126;

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.Equal(0, state.LeftStick.X);
        Assert.Equal(0, state.LeftStick.Y);
    }

    [Fact]
    public void PushingTheLeftStickRightIsPositiveX()
    {
        var report = Usb();
        report[1] = 255;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.LeftStick.X > 0.9);
    }

    // Screen coordinates run downwards and sticks run upwards; the sign is flipped once, here.
    [Fact]
    public void PushingTheLeftStickUpIsPositiveY()
    {
        var report = Usb();
        report[2] = 0;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.LeftStick.Y > 0.9);
    }

    [Fact]
    public void TheRightStickIsReadFromItsOwnAxes()
    {
        var report = Usb();
        report[3] = 255;

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.True(state.RightStick.X > 0.9);
        Assert.Equal(0, state.LeftStick.X);
    }

    // The one difference between the transports, and the one that fails silently if guessed.
    [Fact]
    public void TheBluetoothReportPutsTheAxesOneByteLater()
    {
        var report = Bluetooth();
        report[2] = 255;

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.True(state.LeftStick.X > 0.9);
    }

    [Fact]
    public void TheBluetoothButtonsAreOffsetTheSameWay()
    {
        var report = Bluetooth();
        report[9] = 0x08 | 0x20;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value
            .Buttons.HasFlag(SteamControllerButtons.A));
    }

    [Theory]
    [InlineData(0x20, SteamControllerButtons.A)]   // cross, where A is
    [InlineData(0x40, SteamControllerButtons.B)]   // circle
    [InlineData(0x10, SteamControllerButtons.X)]   // square
    [InlineData(0x80, SteamControllerButtons.Y)]   // triangle
    public void EachFaceButtonMapsToItsPosition(int bit, SteamControllerButtons expected)
    {
        var report = Usb();
        report[5] = (byte)(0x08 | bit);

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.Buttons.HasFlag(expected));
    }

    // The shape invites treating it as a bitfield, which makes "up" (0) indistinguishable from
    // "centred" (8) and leaves the pad walking upwards forever.
    [Fact]
    public void ACentredDpadIsNotUp()
    {
        var report = Usb();
        report[5] = 0x08;

        Assert.False(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value
            .Buttons.HasFlag(SteamControllerButtons.DPadUp));
    }

    [Fact]
    public void TheDpadHatDecodesToDirections()
    {
        var report = Usb();
        report[5] = 0;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value
            .Buttons.HasFlag(SteamControllerButtons.DPadUp));
    }

    [Fact]
    public void ADiagonalHatGivesBothDirections()
    {
        var report = Usb();
        report[5] = 1;

        var buttons = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.Buttons;

        Assert.True(buttons.HasFlag(SteamControllerButtons.DPadUp));
        Assert.True(buttons.HasFlag(SteamControllerButtons.DPadRight));
    }

    [Fact]
    public void TheTriggersAreAnalogue()
    {
        var report = Usb();
        report[8] = 255;
        report[9] = 128;

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.Equal(1.0, state.LeftTrigger, 2);
        Assert.Equal(0.5, state.RightTrigger, 1);
    }

    [Theory]
    [InlineData(0x01, SteamControllerButtons.LeftBumper)]
    [InlineData(0x02, SteamControllerButtons.RightBumper)]
    [InlineData(0x10, SteamControllerButtons.View)]        // create
    [InlineData(0x20, SteamControllerButtons.Menu)]        // options
    [InlineData(0x40, SteamControllerButtons.LeftStick)]
    [InlineData(0x80, SteamControllerButtons.RightStick)]
    public void TheShoulderRowMapsToItsXboxEquivalent(int bit, SteamControllerButtons expected)
    {
        var report = Usb();
        report[6] = (byte)bit;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.Buttons.HasFlag(expected));
    }

    // Le bouton PS lance Steam, comme le bouton Steam d'une manette Steam. Ce n'est PAS le bouton de
    // changement de mode : le switch est la tenue L3+R3, dans InputModeHandler. Le mapper sur
    // QuickAccess pour donner un bouton de switch a la manette a ete fait le 14 aout et annule le
    // jour meme — cela retirait le lanceur pour resoudre un probleme qui n'est pas ici.
    [Fact]
    public void ThePsButtonLaunchesSteamAndIsNotTheSwitch()
    {
        var report = Bluetooth();
        report[11] = 0x01;

        var buttons = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.Buttons;

        Assert.True(buttons.HasFlag(SteamControllerButtons.Steam));
        Assert.False(buttons.HasFlag(SteamControllerButtons.QuickAccess));
    }

    // Rien sur cette manette ne produit QuickAccess, et c'est voulu. Le mappage mute a ete retire le
    // 14 aout : cet octet est un compteur dans le rapport Bluetooth de compatibilite, et le lire
    // comme un bouton fait basculer le mode deux fois par seconde.
    [Fact]
    public void NoBitProducesQuickAccess()
    {
        var report = Bluetooth();
        report[9] = 0xFF;
        report[10] = 0xFF;
        report[11] = 0xFF;

        Assert.False(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value
            .Buttons.HasFlag(SteamControllerButtons.QuickAccess));
    }

    // A DualSense also emits feature and audio reports on the same pipe. They are normal traffic.
    [Fact]
    public void AnUnknownReportIsIgnoredRatherThanThrown()
        => Assert.Null(DualSenseReportParser.Parse([0x05, 1, 2, 3], TimeSpan.Zero));

    [Fact]
    public void ATruncatedReportIsIgnored()
        => Assert.Null(DualSenseReportParser.Parse([0x01, 128, 128], TimeSpan.Zero));

    [Fact]
    public void AnEmptyReportIsIgnored()
        => Assert.Null(DualSenseReportParser.Parse([], TimeSpan.Zero));

    [Fact]
    public void TheTimestampIsCarriedThrough()
        => Assert.Equal(
            TimeSpan.FromSeconds(3),
            DualSenseReportParser.Parse(Usb(), TimeSpan.FromSeconds(3))!.Value.Timestamp);

    // Nothing on a DualSense stands in for a trackpad, and inventing one would mean guessing.
    [Fact]
    public void ThePadsAreReportedReleased()
    {
        var state = DualSenseReportParser.Parse(Usb(), TimeSpan.Zero)!.Value;

        Assert.Equal(TouchpadSample.Released, state.LeftPad);
        Assert.Equal(TouchpadSample.Released, state.RightPad);
    }

    // Report 0x01 means two different things depending on the transport. The axes sit in the same
    // place in both, which is what makes the mistake so misleading: the pointer moves correctly and
    // then every button does something else, because a trigger byte read as a bitfield is a fistful
    // of buttons pressed at once. This is the shape a DualSense actually sends over Bluetooth until
    // something asks it for the full report.
    private static byte[] BluetoothCompact()
    {
        var report = new byte[10];
        report[0] = 0x01;
        report[1] = Centre;
        report[2] = Centre;
        report[3] = Centre;
        report[4] = Centre;
        report[5] = 0x08;   // buttons here, not at byte 8
        return report;
    }

    // The real Bluetooth compatibility report is not the ten-byte fixture above but the full
    // 78-byte frame. Bytes captured from a DualSense connected over Bluetooth, right stick mid-way:
    //   [01 80 7E F6 7D 08 00 24 00 00 00 00]
    // Byte 7 runs 0x24, 0x28, 0x2C, … 0x3C, 0x00, 0x04, … — a per-frame counter, not the PS/mute
    // byte. Read as the system byte it held the mute button (Quick Access, the mode-switch chord)
    // on and off by itself, and the controller switched mode roughly twice a second. This is the
    // regression that test pins.
    private static byte[] BluetoothCompactFull()
    {
        var report = new byte[78];
        report[0] = 0x01;
        report[1] = Centre;
        report[2] = 0x7E;
        report[3] = 0xF6;
        report[4] = 0x7D;
        report[5] = 0x08;
        report[7] = 0x24;   // the counter, in the USB report's system-byte place
        return report;
    }

    [Fact]
    public void TheBluetoothCompatibilityCounterIsNeverAButton()
    {
        var state = DualSenseReportParser.Parse(BluetoothCompactFull(), TimeSpan.Zero)!.Value;

        Assert.Equal(SteamControllerButtons.None, state.Buttons);
        Assert.Equal(0, state.LeftStick.X);
        Assert.True(state.RightStick.X > 0.8);
    }

    [Theory]
    [InlineData(0x04)]   // the counter values whose bit 0x04 fired the mute button
    [InlineData(0x0C)]
    [InlineData(0x14)]
    [InlineData(0x1C)]
    [InlineData(0x24)]
    [InlineData(0x2C)]
    [InlineData(0x34)]
    [InlineData(0x3C)]
    public void NoCounterValueTurnsIntoQuickAccess(int counter)
    {
        var report = BluetoothCompactFull();
        report[7] = (byte)counter;

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.Equal(SteamControllerButtons.None, state.Buttons);
    }

    [Fact]
    public void ATriggerIsNeverReadAsARowOfButtons()
    {
        var report = BluetoothCompact();
        report[8] = 255;    // a trigger; read as a button byte it is a fistful of buttons at once

        var state = DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value;

        Assert.Equal(1.0, state.LeftTrigger, 2);
        Assert.Equal(SteamControllerButtons.None, state.Buttons);
    }

    [Fact]
    public void TheCompactBluetoothReportStillDecodesItsButtons()
    {
        var report = BluetoothCompact();
        report[5] = 0x08 | 0x20;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value
            .Buttons.HasFlag(SteamControllerButtons.A));
    }

    [Fact]
    public void TheCompactBluetoothReportStillDecodesItsAxes()
    {
        var report = BluetoothCompact();
        report[1] = 255;

        Assert.True(DualSenseReportParser.Parse(report, TimeSpan.Zero)!.Value.LeftStick.X > 0.9);
    }
}