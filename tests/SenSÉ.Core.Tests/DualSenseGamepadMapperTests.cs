using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The DualShock 4 report a DualSense produces in gamepad mode.
/// </summary>
/// <remarks>
/// The names are positional: the same physical button, the same label, only the name a game reads
/// changes (A→Cross and so on). The Y axes are inverted because a DualShock 4 reads up as zero.
/// </remarks>
public sealed class DualSenseGamepadMapperTests
{
    private static DualSenseGamepadMapper Mapper()
        => new() { ButtonMap = XboxButtonMap.DefaultFor(ControllerKind.DualSense) };

    [Fact]
    public void FaceButtonsMapPositionally()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.A | SteamControllerButtons.B
                     | SteamControllerButtons.X | SteamControllerButtons.Y,
        });

        Assert.True(report.Buttons.HasFlag(DS4Buttons.Cross));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.Circle));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.Square));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.Triangle));
    }

    [Fact]
    public void BumpersAndSticksMapPositionally()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.LeftBumper | SteamControllerButtons.RightBumper
                     | SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick,
        });

        Assert.True(report.Buttons.HasFlag(DS4Buttons.ShoulderLeft));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.ShoulderRight));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.ThumbLeft));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.ThumbRight));
    }

    [Fact]
    public void OptionsAndShareMapFromMenuAndView()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.Menu | SteamControllerButtons.View,
        });

        Assert.True(report.Buttons.HasFlag(DS4Buttons.Options));
        Assert.True(report.Buttons.HasFlag(DS4Buttons.Share));
    }

    [Fact]
    public void PlayStationButtonSetsTheSpecialByte()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.Steam,
        });

        Assert.True(report.PlayStation);
    }

    [Fact]
    public void DPadReportsAsAHat()
    {
        var mapper = Mapper();

        var up = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.DPadUp,
        });
        Assert.Equal(DS4Dpad.Up, up.Dpad);

        var diagonal = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.DPadDown | SteamControllerButtons.DPadRight,
        });
        Assert.Equal(DS4Dpad.DownRight, diagonal.Dpad);
    }

    [Fact]
    public void UpOnAYAxisIsByteZero()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftStick = new NormalizedStick(0, 1),
            RightStick = new NormalizedStick(0, 1),
        });

        Assert.Equal(0, report.LeftThumbY);
        Assert.Equal(0, report.RightThumbY);
        Assert.Equal(128, report.LeftThumbX);
    }

    [Fact]
    public void DownOnAYAxisIsByteTwoFiftyFive()
    {
        var report = Mapper().Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftStick = new NormalizedStick(0, -1),
        });

        Assert.Equal(255, report.LeftThumbY);
    }

    [Fact]
    public void XAxisRunsLeftToRightAcrossTheByteRange()
    {
        var mapper = Mapper();

        var left = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftStick = new NormalizedStick(-1, 0),
        });
        Assert.Equal(0, left.LeftThumbX);
        Assert.Equal(128, left.LeftThumbY);

        var right = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftStick = new NormalizedStick(1, 0),
        });
        Assert.Equal(255, right.LeftThumbX);
    }

    [Fact]
    public void TriggersMapToSliderAndDigitalBits()
    {
        var mapper = Mapper();

        var full = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftTrigger = 1.0,
            RightTrigger = 1.0,
        });
        Assert.Equal(255, full.LeftTrigger);
        Assert.Equal(255, full.RightTrigger);
        Assert.True(full.Buttons.HasFlag(DS4Buttons.TriggerLeft));
        Assert.True(full.Buttons.HasFlag(DS4Buttons.TriggerRight));

        var light = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftTrigger = 0.1,
        });
        Assert.False(light.Buttons.HasFlag(DS4Buttons.TriggerLeft));
        Assert.True(light.LeftTrigger > 0);
    }

    [Fact]
    public void ProfileTuningIsRespected()
    {
        var mapper = Mapper();
        mapper.Tuning = new XboxTuning { StickDeadZone = 0.5 };

        var report = mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftStick = new NormalizedStick(0.4, 0),
        });

        Assert.Equal(128, report.LeftThumbX);
    }

    [Fact]
    public void ProfileButtonMapIsRespected()
    {
        // Swapped face buttons are still emitted positionally: the physical button B, told to
        // become A, comes out as Cross, not Circle.
        var map = XboxButtonMap.DefaultFor(ControllerKind.DualSense);
        map[SteamControllerButtons.B] = Xbox360Buttons.A;

        var report = new DualSenseGamepadMapper { ButtonMap = map }
            .Map(ControllerState.Empty(TimeSpan.Zero) with
            {
                Buttons = SteamControllerButtons.B,
            });

        Assert.True(report.Buttons.HasFlag(DS4Buttons.Cross));
        Assert.False(report.Buttons.HasFlag(DS4Buttons.Circle));
    }

    [Fact]
    public void ToXbox360UndoesThePositionalMap()
    {
        var report = new DS4Report(
            DS4Buttons.Cross | DS4Buttons.Options | DS4Buttons.ShoulderRight,
            DS4Dpad.None,
            0,
            0,
            128,
            128,
            128,
            128,
            PlayStation: true);

        var mapped = DualSenseGamepadMapper.ToXbox360(report);

        Assert.True(mapped.HasFlag(Xbox360Buttons.A));
        Assert.True(mapped.HasFlag(Xbox360Buttons.Start));
        Assert.True(mapped.HasFlag(Xbox360Buttons.RightShoulder));
        Assert.True(mapped.HasFlag(Xbox360Buttons.Guide));
    }
}
