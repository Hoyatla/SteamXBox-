using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Tests;

public sealed class ScrollReleaseTests
{
    private static TouchpadSample Touch(double x, double y) => new(true, x, y, 0.0, false);

    private static TouchpadSample Press(double x, double y) => new(true, x, y, 0.0, true);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static LeftTouchpadScrollSettings Scroll => LeftTouchpadScrollSettings.Default with
    {
        WheelDeltaPerPadUnit = 10.0,
        InvertVertical = false,
    };

    [Fact]
    public void AGestureThatDoublesBackDoesNotCoastBackwards()
    {
        // Lifting a finger drags it back a little. Those reversed frames used to outweigh the gesture
        // and send the coast the wrong way, which reads as the page scrolling back on its own.
        var mapper = new LeftTouchpadScrollMapper(Scroll);

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Touch(0.0, 0.20));
        mapper.Update(At(16), Touch(0.0, 0.40));
        // The finger rolls backwards as it leaves the surface.
        mapper.Update(At(24), Touch(0.0, 0.22));
        mapper.Update(At(32), Touch(0.0, 0.05));

        var coasted = 0;
        var time = 32;
        for (int i = 0; i < 60; i++)
        {
            time += 8;
            coasted += mapper.Update(At(time), TouchpadSample.Released).WheelDelta;
        }

        Assert.Equal(0, coasted);
    }

    [Fact]
    public void AOneWayFlickStillCoasts()
    {
        var mapper = new LeftTouchpadScrollMapper(Scroll);

        mapper.Update(At(0), Touch(0.0, 0.0));
        for (int i = 1; i <= 5; i++)
        {
            mapper.Update(At(i * 8), Touch(0.0, i * 0.15));
        }

        var coasted = 0;
        var time = 40;
        for (int i = 0; i < 60; i++)
        {
            time += 8;
            coasted += mapper.Update(At(time), TouchpadSample.Released).WheelDelta;
        }

        Assert.True(coasted > 0, "a clean one-way flick must still coast");
    }

    [Fact]
    public void ATinyMovementDoesNotLaunchAScroll()
    {
        var mapper = new LeftTouchpadScrollMapper(Scroll);

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Touch(0.0, 0.02));

        var coasted = 0;
        var time = 8;
        for (int i = 0; i < 40; i++)
        {
            time += 8;
            coasted += mapper.Update(At(time), TouchpadSample.Released).WheelDelta;
        }

        Assert.Equal(0, coasted);
    }

    [Fact]
    public void PressingTheLeftPadFreezesScrollingBriefly()
    {
        var mapper = new LeftTouchpadScrollMapper(Scroll with { ClickSettleMilliseconds = 90.0 });

        mapper.Update(At(0), Touch(0.0, 0.0));

        // The press itself always shifts the finger; that must not scroll the page.
        var pressed = mapper.Update(At(8), Press(0.0, 0.06));

        Assert.Equal(0, pressed.WheelDelta);
    }

    [Fact]
    public void HoldingTheLeftPadAndMovingStillScrolls()
    {
        var mapper = new LeftTouchpadScrollMapper(Scroll with { ClickSettleMilliseconds = 90.0 });

        mapper.Update(At(0), Touch(0.0, 0.0));
        mapper.Update(At(8), Press(0.0, 0.02));

        // Past the settle window, a held press behaves like a normal drag.
        mapper.Update(At(120), Press(0.0, 0.10));
        var dragging = mapper.Update(At(140), Press(0.0, 0.40));

        Assert.NotEqual(0, dragging.WheelDelta);
    }
}
