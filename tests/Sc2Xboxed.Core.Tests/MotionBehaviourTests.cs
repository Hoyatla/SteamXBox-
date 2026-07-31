using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Tests;

public sealed class MotionBehaviourTests
{
    private static TouchpadSample Touch(double x, double y) => new(true, x, y, 0.0, false);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static RightTouchpadTrackballSettings Trackball => RightTouchpadTrackballSettings.Default with { InvertY = true };

    /// <summary>
    /// Trackball with the involuntary-contact gate disabled. Tests that measure the response to a
    /// deliberate small movement have to opt out of it, or the gate absorbs the whole gesture.
    /// </summary>
    private static RightTouchpadTrackballSettings Responsive => Trackball with { TouchActivationTravel = 0.0, FinePrecisionTravel = 0.0 };

    [Fact]
    public void AccelerationIsExactlyNeutralAtExponentOne()
    {
        // The default must not change existing feel: an untouched profile has to behave identically.
        var linear = new RightTouchpadTrackballMapper(Responsive with { AccelerationExponent = 1.0 });

        linear.Update(At(0), Touch(0.0, 0.0));
        var slow = linear.Update(At(8), Touch(0.01, 0.0));

        linear.Reset();
        linear.Update(At(0), Touch(0.0, 0.0));
        var fast = linear.Update(At(8), Touch(0.10, 0.0));

        // Ten times the movement must give exactly ten times the pixels when acceleration is off.
        Assert.Equal(slow.DeltaX * 10.0, fast.DeltaX, 3);
    }

    [Fact]
    public void AccelerationFavoursFastGesturesOverSlowOnes()
    {
        var accelerated = Responsive with { AccelerationExponent = 1.6 };

        var slowMapper = new RightTouchpadTrackballMapper(accelerated);
        slowMapper.Update(At(0), Touch(0.0, 0.0));
        var slow = slowMapper.Update(At(8), Touch(0.01, 0.0));

        var fastMapper = new RightTouchpadTrackballMapper(accelerated);
        fastMapper.Update(At(0), Touch(0.0, 0.0));
        var fast = fastMapper.Update(At(8), Touch(0.10, 0.0));

        // Same 10x movement, but now the fast gesture must gain more than 10x.
        Assert.True(fast.DeltaX > slow.DeltaX * 10.0,
            $"expected super-linear gain, got {fast.DeltaX:0.##} vs {slow.DeltaX * 10.0:0.##}");
    }

    [Fact]
    public void SlowGesturesGetTwiceThePrecision()
    {
        // Replaces the precision modifier button: the curve floor is what gives fine control, with no
        // chord to hold. A very slow gesture must bottom out at half the linear travel.
        var settings = Responsive with { AccelerationExponent = 2.0, MinAccelerationGain = 0.5 };

        var mapper = new RightTouchpadTrackballMapper(settings);
        mapper.Update(At(0), Touch(0.0, 0.0));
        var crawl = mapper.Update(At(8), Touch(0.002, 0.0));

        var linear = 0.002 * settings.PixelsPerPadUnit;
        Assert.Equal(linear * 0.5, crawl.DeltaX, 3);
    }

    [Fact]
    public void PressingThePadFreezesTheCursor()
    {
        var mapper = new RightTouchpadTrackballMapper(Trackball);

        mapper.Update(At(0), Touch(0.0, 0.0));

        // Pressing always shifts the finger a little; that shift must not drag the pointer off target.
        var pressed = new TouchpadSample(true, 0.05, 0.03, 0.0, true);
        var frame = mapper.Update(At(8), pressed);

        Assert.False(frame.HasMouseMotion);
    }

    [Fact]
    public void ReleasingAClickDoesNotJumpTheCursor()
    {
        var mapper = new RightTouchpadTrackballMapper(Trackball);

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), new TouchpadSample(true, 0.05, 0.03, 0.0, true));
        mapper.Update(At(16), new TouchpadSample(true, 0.07, 0.04, 0.0, true));

        // Position kept tracking while frozen, so lifting the click resumes from where the finger is
        // rather than replaying everything that happened during the press.
        var afterRelease = mapper.Update(At(24), Touch(0.07, 0.04));

        Assert.False(afterRelease.HasMouseMotion);
    }

    [Fact]
    public void HoldingThePadAndMovingStillDrags()
    {
        // Resizing a window or selecting text needs the button held down while the pointer moves.
        // Freezing the whole press, or firing a complete click on release, made both impossible.
        var mapper = new RightTouchpadTrackballMapper(
            Trackball with { TouchActivationTravel = 0.0, ClickSettleMilliseconds = 90.0 });

        mapper.Update(At(0), Touch(0.0, 0.0));

        // Press, then keep holding well past the settle window while the finger travels.
        mapper.Update(At(8), new TouchpadSample(true, 0.01, 0.0, 0.0, true));
        mapper.Update(At(120), new TouchpadSample(true, 0.05, 0.0, 0.0, true));
        var dragging = mapper.Update(At(140), new TouchpadSample(true, 0.12, 0.0, 0.0, true));

        Assert.True(dragging.HasMouseMotion, "holding past the settle window must still move the pointer");
    }

    [Fact]
    public void ABrushDoesNotMoveThePointer()
    {
        var mapper = new RightTouchpadTrackballMapper(Trackball with { TouchActivationTravel = 0.012 });

        // A glancing contact: real enough to register as a touch, far too small to be intent.
        mapper.Update(At(0), Touch(0.20, 0.20));
        var brush1 = mapper.Update(At(8), Touch(0.203, 0.201));
        var brush2 = mapper.Update(At(16), Touch(0.205, 0.202));

        Assert.False(brush1.HasMouseMotion);
        Assert.False(brush2.HasMouseMotion);
    }

    [Fact]
    public void ADeliberateGesturePassesTheBrushFilter()
    {
        var mapper = new RightTouchpadTrackballMapper(Trackball with { TouchActivationTravel = 0.012 });

        mapper.Update(At(0), Touch(0.20, 0.20));
        mapper.Update(At(8), Touch(0.22, 0.20));   // clears the activation distance
        var moving = mapper.Update(At(16), Touch(0.26, 0.20));

        Assert.True(moving.HasMouseMotion, "past the activation distance the pad must track normally");
    }

    [Fact]
    public void ASmallAdjustmentDoesNotThrowTheCursor()
    {
        // The complaint this guards: a short, brisk correction ended in a long glide, because velocity
        // is distance over time and a two-pixel nudge in one frame still reads as fast.
        var mapper = new RightTouchpadTrackballMapper(
            Trackball with { TouchActivationTravel = 0.0, MinThrowTravelPixels = 70.0 });

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Touch(0.01, 0.0));
        mapper.Update(At(16), Touch(0.02, 0.0));

        var released = mapper.Update(At(24), TouchpadSample.Released);

        Assert.False(released.HasMouseMotion, "a short gesture must place the cursor, not launch it");
    }

    [Fact]
    public void ALongGestureStillThrows()
    {
        var mapper = new RightTouchpadTrackballMapper(
            Trackball with { TouchActivationTravel = 0.0, MinThrowTravelPixels = 70.0 });

        mapper.Update(At(0), Touch(0.0, 0.0));
        for (int i = 1; i <= 6; i++)
        {
            mapper.Update(At(i * 8), Touch(i * 0.06, 0.0));
        }

        var released = mapper.Update(At(56), TouchpadSample.Released);

        Assert.True(released.HasMouseMotion, "a real swipe must still coast");
    }

    [Fact]
    public void EdgeContinuationMovesWhileTheFingerRestsAtTheEdge()
    {
        var mapper = new RightTouchpadTrackballMapper(
            Trackball with { EdgeSpeedPixelsPerSecond = 800.0, EdgeThreshold = 0.85 });

        // Finger parked at the right edge, perfectly still: the delta path contributes nothing.
        mapper.Update(At(0), Touch(0.98, 0.0));
        var resting = mapper.Update(At(8), Touch(0.98, 0.0));

        Assert.True(resting.DeltaX > 0.0, "resting at the right edge must keep moving the cursor right");
    }

    [Fact]
    public void EdgeContinuationIsOffByDefault()
    {
        var mapper = new RightTouchpadTrackballMapper(Trackball);

        mapper.Update(At(0), Touch(0.98, 0.0));
        var resting = mapper.Update(At(8), Touch(0.98, 0.0));

        Assert.False(resting.HasMouseMotion);
    }

    [Fact]
    public void HorizontalScrollIsOffUnlessEnabled()
    {
        var settings = LeftTouchpadScrollSettings.Default with
        {
            WheelDeltaPerPadUnit = 10.0,
            InvertVertical = false,
        };

        var off = new LeftTouchpadScrollMapper(settings);
        off.Update(At(0), Touch(0.0, 0.0));
        var offFrame = off.Update(At(8), Touch(0.5, 0.0));

        var on = new LeftTouchpadScrollMapper(settings with { HorizontalEnabled = true });
        on.Update(At(0), Touch(0.0, 0.0));
        var onFrame = on.Update(At(8), Touch(0.5, 0.0));

        Assert.Equal(0, offFrame.HorizontalWheelDelta);
        Assert.True(onFrame.HorizontalWheelDelta > 0, "a rightward swipe must scroll right when enabled");
    }

    [Fact]
    public void ProfileDefaultsKeepTheHistoricalRouting()
    {
        var defaults = Sc2XboxedProfileSettings.Default;

        // These were hardcoded before the motions section was honoured; the defaults must reproduce
        // exactly the old behaviour so an existing profile is unaffected.
        Assert.Equal(PadMotionMode.Trackball, defaults.RightPadMode);
        Assert.Equal(PadMotionMode.Scroll, defaults.LeftPadMode);
        Assert.Equal(StickMotionMode.ArrowKeys, defaults.LeftStickMode);
    }

    [Fact]
    public void LeftPadTrackballHasItsOwnSettings()
    {
        var settings = Sc2XboxedProfileSettings.Default with
        {
            RightPadTrackball = Trackball with { PixelsPerPadUnit = 900.0 },
            LeftPadTrackball = Trackball with { PixelsPerPadUnit = 300.0 },
        };

        // Sharing one settings object made the left pad silently adopt the right pad's sensitivity.
        Assert.NotEqual(
            settings.RightPadTrackball.PixelsPerPadUnit,
            settings.LeftPadTrackball.PixelsPerPadUnit);
    }
}
