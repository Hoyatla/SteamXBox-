using SenSÉ.Core.Input;
using SenSÉ.Core.Haptics;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Tests;

public sealed class ControllerOutputMapperTests
{
    [Fact]
    public void RearButtonsMapToRequestedXboxFaceButtons()
    {
        var buttons =
            SteamControllerButtons.L4 |
            SteamControllerButtons.R4 |
            SteamControllerButtons.L5 |
            SteamControllerButtons.R5;

        var mapped = ControllerOutputMapper.MapButtons(buttons);

        Assert.True(mapped.HasFlag(Xbox360Buttons.X));
        Assert.True(mapped.HasFlag(Xbox360Buttons.Y));
        Assert.True(mapped.HasFlag(Xbox360Buttons.A));
        Assert.True(mapped.HasFlag(Xbox360Buttons.B));
    }

    [Fact]
    public void LeftTouchpadConvertsVerticalMotionToMouseWheel()
    {
        var mapper = new ControllerOutputMapper(new SenSÉProfileSettings
        {
            LeftPadScroll = new LeftTouchpadScrollSettings
            {
                WheelDeltaPerPadUnit = 1200.0,
                MotionDeadZone = 0.0
            }
        });

        mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftPad = new TouchpadSample(true, 0.0, 0.0)
        });

        var output = mapper.Map(ControllerState.Empty(TimeSpan.FromMilliseconds(8)) with
        {
            LeftPad = new TouchpadSample(true, 0.0, 0.25)
        });

        Assert.Equal(300, output.Mouse.WheelDelta);
        Assert.False(output.Mouse.HasMouseMotion);
    }

    [Fact]
    public void RightTouchpadMovesMouseWhileTouched()
    {
        var mapper = new ControllerOutputMapper(new SenSÉProfileSettings
        {
            RightPadTrackball = new RightTouchpadTrackballSettings
            {
                PixelsPerPadUnit = 1000.0,
                MotionDeadZone = 0.0
            }
        });

        mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            RightPad = new TouchpadSample(true, 0.0, 0.0)
        });

        var output = mapper.Map(ControllerState.Empty(TimeSpan.FromMilliseconds(10)) with
        {
            RightPad = new TouchpadSample(true, 0.2, -0.1)
        });

        Assert.Equal(200.0, output.Mouse.DeltaX, precision: 3);
        Assert.Equal(100.0, output.Mouse.DeltaY, precision: 3);
        Assert.Equal(0, output.Mouse.WheelDelta);
    }

    [Fact]
    public void RightTouchpadKeepsTrackballInertiaAfterRelease()
    {
        var mapper = new ControllerOutputMapper(new SenSÉProfileSettings
        {
            RightPadTrackball = new RightTouchpadTrackballSettings
            {
                PixelsPerPadUnit = 1000.0,
                MotionDeadZone = 0.0,
                InertiaDecayPerSecond = 4.0,
                StopSpeedPixelsPerSecond = 0.1,

                // This checks that inertia is wired through the mapper at all, so the gating that
                // normally suppresses a throw this short is switched off here on purpose.
                MinThrowTravelPixels = 0.0,
                TouchActivationTravel = 0.0,
                FinePrecisionTravel = 0.0,
            }
        });

        mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            RightPad = new TouchpadSample(true, 0.0, 0.0)
        });

        mapper.Map(ControllerState.Empty(TimeSpan.FromMilliseconds(10)) with
        {
            RightPad = new TouchpadSample(true, 0.1, 0.0)
        });

        var output = mapper.Map(ControllerState.Empty(TimeSpan.FromMilliseconds(20)) with
        {
            RightPad = TouchpadSample.Released
        });

        Assert.True(output.Mouse.DeltaX > 0.0);
        Assert.Equal(0.0, output.Mouse.DeltaY, precision: 3);
    }

    [Fact]
    public void TouchpadShortTouchReleaseProducesTapEvent()
    {
        var mapper = new ControllerOutputMapper();

        mapper.Map(ControllerState.Empty(TimeSpan.Zero) with
        {
            LeftPad = new TouchpadSample(true, 0.25, -0.25, Pressure: 0.5)
        });

        var output = mapper.Map(ControllerState.Empty(TimeSpan.FromMilliseconds(90)) with
        {
            LeftPad = TouchpadSample.Released
        });

        Assert.True(output.LeftPadTap.WasTapped);
        Assert.Equal(0.25, output.LeftPadTap.X, precision: 3);
        Assert.Equal(-0.25, output.LeftPadTap.Y, precision: 3);
    }

    [Fact]
    public void XboxRumbleMapsToIndependentSteamHapticCommands()
    {
        var mapper = new XboxRumbleToSteamHapticsMapper();

        var frame = mapper.Map(new XboxRumbleFrame(0.25, 0.75));

        Assert.Collection(
            frame.Commands,
            left =>
            {
                Assert.Equal(HapticActuator.LeftRumble, left.Actuator);
                Assert.Equal(HapticType.Rumble, left.Type);
            },
            right =>
            {
                Assert.Equal(HapticActuator.RightRumble, right.Actuator);
                Assert.Equal(HapticType.Rumble, right.Type);
            });
    }

    /// <summary>
    /// Menu is Back and View is Start, which is what the hardware does.
    /// </summary>
    /// <remarks>
    /// Briefly swapped during 4.0 on the assumption that it was a bug, then put back: the naming
    /// looks inverted against the Xbox convention, but this is what the controller actually produces.
    /// If a game ever reacts to View where it should react to Menu, the fault is in which HID bit
    /// <c>TritonInputReportParser</c> calls Menu, not here — correcting it in the mapper would only
    /// hide a mislabelled bit and break every profile that stores the mapping by name.
    /// </remarks>
    [Fact]
    public void MenuIsBackAndViewIsStart()
    {
        var menu = ControllerOutputMapper.MapButtons(SteamControllerButtons.Menu);
        var view = ControllerOutputMapper.MapButtons(SteamControllerButtons.View);

        Assert.True(menu.HasFlag(Xbox360Buttons.Back));
        Assert.False(menu.HasFlag(Xbox360Buttons.Start));
        Assert.True(view.HasFlag(Xbox360Buttons.Start));
        Assert.False(view.HasFlag(Xbox360Buttons.Back));
    }
}
