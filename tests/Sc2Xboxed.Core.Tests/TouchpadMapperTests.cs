using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Direction conventions are locked in here on purpose: pad Y grows downwards, Windows mouse
/// DeltaY grows downwards, and the trackball negates Y once on top of the InvertY flag. Any change
/// that flips a sign should fail these tests rather than surprise the user.
/// </summary>
public sealed class TouchpadMapperTests
{
    private static TouchpadSample Touch(double x, double y) => new(true, x, y, 0.0, false);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Live default: the shipped profile enables InvertY on the right pad.</summary>
    private static RightTouchpadTrackballSettings LiveTrackball =>
        RightTouchpadTrackballSettings.Default with { InvertY = true, TouchActivationTravel = 0.0, MinThrowTravelPixels = 0.0, FinePrecisionTravel = 0.0 };

    [Fact]
    public void FingerRightMovesCursorRight()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        mapper.Update(At(0), Touch(0.0, 0.0));
        var frame = mapper.Update(At(8), Touch(0.1, 0.0));

        Assert.True(frame.DeltaX > 0.0, "moving the finger right must move the cursor right");
        Assert.Equal(0.0, frame.DeltaY, 6);
    }

    [Fact]
    public void FingerUpMovesCursorUp()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        // Pad Y grows downwards, so moving the finger up decreases Y.
        mapper.Update(At(0), Touch(0.0, 0.0));
        var frame = mapper.Update(At(8), Touch(0.0, -0.1));

        // Windows mouse DeltaY grows downwards, so up is negative.
        Assert.True(frame.DeltaY < 0.0, "moving the finger up must move the cursor up");
        Assert.Equal(0.0, frame.DeltaX, 6);
    }

    [Fact]
    public void SlowDiagonalDragKeepsBothAxes()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        // Each axis delta sits below MotionDeadZone (0.0015) while the 2D magnitude clears it.
        // The old per-axis dead zone zeroed both components here, producing stair-step motion.
        mapper.Update(At(0), Touch(0.0, 0.0));
        var frame = mapper.Update(At(8), Touch(0.0012, 0.0012));

        Assert.NotEqual(0.0, frame.DeltaX);
        Assert.NotEqual(0.0, frame.DeltaY);
    }

    [Fact]
    public void TrackballThrowContinuesAfterReleaseThenStops()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        // A steady rightward drag builds up velocity.
        for (int i = 0; i <= 6; i++)
        {
            mapper.Update(At(i * 8), Touch(i * 0.03, 0.0));
        }

        var firstCoast = mapper.Update(At(56), TouchpadSample.Released);
        Assert.True(firstCoast.DeltaX > 0.0, "the throw must continue in the drag direction");

        // Inertia decays; give it a generous window to settle.
        var time = 56;
        var settled = false;
        for (int i = 0; i < 400 && !settled; i++)
        {
            time += 8;
            settled = !mapper.Update(At(time), TouchpadSample.Released).HasMouseMotion;
        }

        Assert.True(settled, "the throw must decay to a stop");
    }

    [Fact]
    public void RestingTheFingerCancelsTheTrackballThrow()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        for (int i = 0; i <= 6; i++)
        {
            mapper.Update(At(i * 8), Touch(i * 0.03, 0.0));
        }

        // Holding still past QuietFramesToCancelThrow must kill the velocity, so lifting off does not
        // throw. Fewer frames than that count as a pause and are covered by
        // ABriefPauseMidGestureDoesNotCancelTheThrow.
        var quietFrames = LiveTrackball.QuietFramesToCancelThrow;
        var time = 56;
        for (int i = 0; i < quietFrames; i++, time += 8)
        {
            mapper.Update(At(time), Touch(0.18, 0.0));
        }

        var released = mapper.Update(At(time), TouchpadSample.Released);

        Assert.False(released.HasMouseMotion);
    }

    [Fact]
    public void ScrollSignFollowsTheInvertFlag()
    {
        var plain = new LeftTouchpadScrollMapper(
            LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 10.0, InvertVertical = false });
        var inverted = new LeftTouchpadScrollMapper(
            LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 10.0, InvertVertical = true });

        plain.Update(At(0), Touch(0.0, 0.0));
        inverted.Update(At(0), Touch(0.0, 0.0));

        // Finger moves down the pad.
        var plainFrame = plain.Update(At(8), Touch(0.0, 0.5));
        var invertedFrame = inverted.Update(At(8), Touch(0.0, 0.5));

        Assert.True(plainFrame.WheelDelta > 0);
        Assert.True(invertedFrame.WheelDelta < 0);
    }

    [Fact]
    public void SlowScrollAccumulatesIntoAWholeNotch()
    {
        var mapper = new LeftTouchpadScrollMapper(
            LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 10.0, InvertVertical = false });

        mapper.Update(At(0), Touch(0.0, 0.0));

        // 0.05 pad units is half a notch at this sensitivity: the first frame emits nothing but must
        // keep the fraction, so the second frame completes a notch.
        var first = mapper.Update(At(8), Touch(0.0, 0.05));
        var second = mapper.Update(At(16), Touch(0.0, 0.10));

        Assert.Equal(0, first.WheelDelta);
        Assert.Equal(1, second.WheelDelta);
    }

    [Fact]
    public void AShortFlickStillThrowsAtTheGestureSpeed()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        // Three frames at 0.03 pad units per 8 ms: 27 px per frame at 900 px/unit, so ~3375 px/s.
        // An exponential average only reaches ~73% of that in three frames, which is why short flicks
        // used to feel like they had no inertia at all.
        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Touch(0.03, 0.0));
        mapper.Update(At(16), Touch(0.06, 0.0));
        mapper.Update(At(24), Touch(0.09, 0.0));

        var firstCoast = mapper.Update(At(32), TouchpadSample.Released);

        // One frame of coasting should carry roughly one frame of travel, not a fraction of it.
        Assert.True(firstCoast.DeltaX > 20.0,
            $"expected a throw close to the 27px/frame gesture, got {firstCoast.DeltaX:0.#}px");
    }

    [Fact]
    public void ABriefPauseMidGestureDoesNotCancelTheThrow()
    {
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Touch(0.03, 0.0));
        mapper.Update(At(16), Touch(0.06, 0.0));
        mapper.Update(At(24), Touch(0.09, 0.0));

        // One still frame: a pause, not a finger at rest.
        mapper.Update(At(32), Touch(0.09, 0.0));

        var released = mapper.Update(At(40), TouchpadSample.Released);

        Assert.True(released.HasMouseMotion, "a single quiet frame must not cancel a pending throw");
    }

    [Fact]
    public void InertiaSurvivesTheSmoothingLayersReleaseTail()
    {
        // The production path feeds the trackball through SmoothedTouchpadInput, which keeps reporting
        // "touched" with an unchanged position for three frames after the finger lifts. Those frames
        // look like zero motion, so a low quiet-frame threshold cancelled the throw right before the
        // release landed. Testing the mapper in isolation hid this entirely.
        var smoother = new SmoothedTouchpadInput();
        var mapper = new RightTouchpadTrackballMapper(LiveTrackball);

        var time = 0;
        void Feed(TouchpadSample raw)
        {
            mapper.Update(At(time), smoother.Update(raw));
            time += 8;
        }

        // A steady rightward drag.
        for (int i = 0; i <= 8; i++)
        {
            Feed(Touch(i * 0.03, 0.0));
        }

        // Finger lifts: the smoother stretches the release over several frames, so coasting can only
        // start once its tail has drained.
        var coasted = false;
        for (int i = 0; i < 12; i++)
        {
            if (mapper.Update(At(time), smoother.Update(TouchpadSample.Released)).HasMouseMotion)
            {
                coasted = true;
            }
            time += 8;
        }

        Assert.True(coasted, "the throw must survive the smoothing layer's release tail");
    }

    [Fact]
    public void ScrollCoastIsCappedRegardlessOfSensitivity()
    {
        // 600 units per pad unit is the value the profile editor writes; without a hard cap the coast
        // length scales with it and a single flick becomes hundreds of notches of overshoot.
        var settings = LeftTouchpadScrollSettings.Default with
        {
            WheelDeltaPerPadUnit = 600.0,
            InvertVertical = false,
        };
        var mapper = new LeftTouchpadScrollMapper(settings);

        mapper.Update(At(0), Touch(0.0, 0.0));
        for (int i = 1; i <= 6; i++)
        {
            mapper.Update(At(i * 8), Touch(0.0, i * 0.15));
        }

        var coasted = 0;
        var time = 48;
        for (int i = 0; i < 500; i++)
        {
            time += 8;
            coasted += Math.Abs(mapper.Update(At(time), TouchpadSample.Released).WheelDelta);
        }

        Assert.True(coasted > 0, "the flick must still coast");
        Assert.True(coasted <= settings.MaxCoastNotches,
            $"coast must respect the {settings.MaxCoastNotches} notch cap, emitted {coasted}");
    }

    [Fact]
    public void ScrollKeepsCoastingAfterRelease()
    {
        var mapper = new LeftTouchpadScrollMapper(
            LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 10.0, InvertVertical = false });

        // A brisk downward flick.
        for (int i = 0; i <= 6; i++)
        {
            mapper.Update(At(i * 8), Touch(0.0, i * 0.12));
        }

        var coasted = 0;
        var time = 48;
        for (int i = 0; i < 100; i++)
        {
            time += 8;
            coasted += mapper.Update(At(time), TouchpadSample.Released).WheelDelta;
        }

        Assert.True(coasted > 0, "a flick must keep scrolling in the same direction after release");
    }
}
