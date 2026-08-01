using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Tests;

public sealed class DefaultSteamControllerMapperTests
{
    [Fact]
    public void RearButtonsMapToRequestedXboxFaceButtons()
    {
        var buttons =
            SteamControllerButtons.L4 |
            SteamControllerButtons.R4 |
            SteamControllerButtons.L5 |
            SteamControllerButtons.R5;

        var mapped = DefaultSteamControllerMapper.MapButtons(buttons);

        Assert.True(mapped.HasFlag(Xbox360Buttons.X));
        Assert.True(mapped.HasFlag(Xbox360Buttons.Y));
        Assert.True(mapped.HasFlag(Xbox360Buttons.A));
        Assert.True(mapped.HasFlag(Xbox360Buttons.B));
    }

    [Fact]
    public void LeftTouchpadConvertsVerticalMotionToMouseWheel()
    {
        var mapper = new DefaultSteamControllerMapper(new Sc2XboxedProfileSettings
        {
            LeftPadScroll = new LeftTouchpadScrollSettings
            {
                WheelDeltaPerPadUnit = 1200.0,
                MotionDeadZone = 0.0
            }
        });

        mapper.Map(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            LeftPad = new TouchpadSample(true, 0.0, 0.0)
        });

        var output = mapper.Map(SteamControllerState.Empty(TimeSpan.FromMilliseconds(8)) with
        {
            LeftPad = new TouchpadSample(true, 0.0, 0.25)
        });

        Assert.Equal(300, output.Mouse.WheelDelta);
        Assert.False(output.Mouse.HasMouseMotion);
    }

    [Fact]
    public void RightTouchpadMovesMouseWhileTouched()
    {
        var mapper = new DefaultSteamControllerMapper(new Sc2XboxedProfileSettings
        {
            RightPadTrackball = new RightTouchpadTrackballSettings
            {
                PixelsPerPadUnit = 1000.0,
                MotionDeadZone = 0.0
            }
        });

        mapper.Map(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            RightPad = new TouchpadSample(true, 0.0, 0.0)
        });

        var output = mapper.Map(SteamControllerState.Empty(TimeSpan.FromMilliseconds(10)) with
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
        var mapper = new DefaultSteamControllerMapper(new Sc2XboxedProfileSettings
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

        mapper.Map(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            RightPad = new TouchpadSample(true, 0.0, 0.0)
        });

        mapper.Map(SteamControllerState.Empty(TimeSpan.FromMilliseconds(10)) with
        {
            RightPad = new TouchpadSample(true, 0.1, 0.0)
        });

        var output = mapper.Map(SteamControllerState.Empty(TimeSpan.FromMilliseconds(20)) with
        {
            RightPad = TouchpadSample.Released
        });

        Assert.True(output.Mouse.DeltaX > 0.0);
        Assert.Equal(0.0, output.Mouse.DeltaY, precision: 3);
    }

    [Fact]
    public void TouchpadShortTouchReleaseProducesTapEvent()
    {
        var mapper = new DefaultSteamControllerMapper();

        mapper.Map(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            LeftPad = new TouchpadSample(true, 0.25, -0.25, Pressure: 0.5)
        });

        var output = mapper.Map(SteamControllerState.Empty(TimeSpan.FromMilliseconds(90)) with
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
    /// Menu is Start and View is Back, the Xbox convention.
    /// </summary>
    /// <remarks>
    /// This asserted the opposite until 3.2, under a name claiming it matched what the controller
    /// reports. Testing on the hardware showed the two were simply inverted. If a game ever reacts to
    /// View where it should react to Menu, the fault is then in which HID bit
    /// <c>TritonInputReportParser</c> calls Menu, not here — fixing it in the mapper would only hide
    /// a mislabelled bit and break every profile that stores the mapping by name.
    /// </remarks>
    [Fact]
    public void MenuIsStartAndViewIsBack()
    {
        var menu = DefaultSteamControllerMapper.MapButtons(SteamControllerButtons.Menu);
        var view = DefaultSteamControllerMapper.MapButtons(SteamControllerButtons.View);

        Assert.True(menu.HasFlag(Xbox360Buttons.Start));
        Assert.False(menu.HasFlag(Xbox360Buttons.Back));
        Assert.True(view.HasFlag(Xbox360Buttons.Back));
        Assert.False(view.HasFlag(Xbox360Buttons.Start));
    }
}
