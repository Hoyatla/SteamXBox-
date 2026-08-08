using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Pointer and wheel driven by the sticks, for controllers with no trackpads.
/// </summary>
public class StickPointerMapperTests
{
    private static readonly StickPointerSettings Settings = new();
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    private static ControllerState With(NormalizedStick right, NormalizedStick? left = null)
        => ControllerState.Empty(TimeSpan.Zero) with
        {
            RightStick = right,
            LeftStick = left ?? NormalizedStick.Center,
        };

    private static StickPointerOutput Map(ControllerState state, TimeSpan? elapsed = null)
    {
        var carry = new StickPointerCarry();
        return StickPointerMapper.Map(state, elapsed ?? Frame, Settings, rightStickPointer: true, leftStickWheel: true, ref carry);
    }

    [Fact]
    public void ACentredStickMovesNothing()
    {
        var output = Map(With(NormalizedStick.Center));

        Assert.Equal(0, output.PixelsX);
        Assert.Equal(0, output.PixelsY);
        Assert.Equal(0, output.WheelNotches);
    }

    [Fact]
    public void TheRightStickMovesThePointer()
    {
        var output = Map(With(new NormalizedStick(1, 0)));

        Assert.True(output.PixelsX > 0);
        Assert.Equal(0, output.PixelsY);
    }

    // Sticks report up as positive; screens count down as positive. Inverting once, inside the
    // mapper, is what stops one of the two axes from being wrong.
    [Fact]
    public void PushingUpMovesThePointerUp()
        => Assert.True(Map(With(new NormalizedStick(0, 1))).PixelsY < 0);

    [Fact]
    public void TheLeftStickScrollsAndDoesNotMoveThePointer()
    {
        var output = Map(With(NormalizedStick.Center, new NormalizedStick(0, 1)));

        Assert.True(output.WheelNotches > 0);
        Assert.Equal(0, output.PixelsX);
        Assert.Equal(0, output.PixelsY);
    }

    // Binding the horizontal axis too would make every vertical flick scroll sideways by accident.
    [Fact]
    public void PushingTheLeftStickSidewaysDoesNotScroll()
        => Assert.Equal(0, Map(With(NormalizedStick.Center, new NormalizedStick(1, 0))).WheelNotches);

    [Fact]
    public void TheRightStickDoesNotScroll()
        => Assert.Equal(0, Map(With(new NormalizedStick(0, 1))).WheelNotches);

    // A per-axis dead zone would carve a square hole out of a round stick: straight up would work
    // while up-left at the same distance would not.
    [Fact]
    public void TheDeadZoneIsRadialNotPerAxis()
    {
        // Each axis stays under the dead zone while their combination clears it: a per-axis filter
        // would refuse this push even though it is further from centre than a cardinal one it
        // accepts.
        var perAxis = Settings.DeadZone * 0.8;
        var magnitude = Math.Sqrt(2 * perAxis * perAxis);

        Assert.True(perAxis < Settings.DeadZone);
        Assert.True(magnitude > Settings.DeadZone);

        // Accumulated rather than read from one frame. Just past the dead zone the curve makes the
        // movement sub-pixel per frame, so a single frame would report zero for a reason that has
        // nothing to do with the dead zone being radial — which is what this test is about.
        var carry = new StickPointerCarry();
        var diagonal = With(new NormalizedStick(perAxis, perAxis));
        var travelled = 0;

        for (var i = 0; i < 200; i++)
        {
            travelled += StickPointerMapper.Map(diagonal, Frame, Settings, rightStickPointer: true, leftStickWheel: true, ref carry).PixelsX;
        }

        Assert.True(travelled > 0);
    }

    [Fact]
    public void JustInsideTheDeadZoneMovesNothing()
    {
        var output = Map(With(new NormalizedStick(Settings.DeadZone * 0.9, 0)));
        Assert.Equal(0, output.PixelsX);
    }

    // Held longer means travelled further: a stick is a velocity, unlike a trackpad which reports a
    // position and stops when the finger stops.
    [Fact]
    public void MovementScalesWithTime()
    {
        var stick = With(new NormalizedStick(1, 0));
        var shortFrame = Map(stick, TimeSpan.FromMilliseconds(50)).PixelsX;
        var longFrame = Map(stick, TimeSpan.FromMilliseconds(200)).PixelsX;

        Assert.True(longFrame > shortFrame * 3);
    }

    // Without the carry a slow stick rounds to zero every frame and the pointer never moves at all —
    // which reads as broken rather than as slow.
    [Fact]
    public void SubPixelMovementAccumulatesInsteadOfBeingLost()
    {
        var carry = new StickPointerCarry();
        var barely = With(new NormalizedStick(Settings.DeadZone + 0.02, 0));
        var tick = TimeSpan.FromMilliseconds(8);

        var single = StickPointerMapper.Map(barely, tick, Settings, rightStickPointer: true, leftStickWheel: true, ref carry);
        Assert.Equal(0, single.PixelsX);

        var total = 0;
        for (var i = 0; i < 400; i++)
        {
            total += StickPointerMapper.Map(barely, tick, Settings, rightStickPointer: true, leftStickWheel: true, ref carry).PixelsX;
        }

        Assert.True(total > 0);
    }

    // A gap that long means the loop stalled or the controller reconnected. Applying it would fling
    // the pointer across the screen.
    [Fact]
    public void AnImplausibleFrameGapIsIgnored()
    {
        var output = Map(With(new NormalizedStick(1, 0)), TimeSpan.FromSeconds(3));
        Assert.Equal(0, output.PixelsX);
    }

    [Fact]
    public void ZeroElapsedTimeMovesNothing()
        => Assert.Equal(0, Map(With(new NormalizedStick(1, 0)), TimeSpan.Zero).PixelsX);

    // The curve must not cost the stick its top speed: full deflection still means full speed.
    [Fact]
    public void FullDeflectionReachesTheConfiguredSpeed()
    {
        // Une seconde entiere serait rejetee par le garde-fou d'ecart de trame, et c'est lui qui a
        // raison : une trame d'un dixieme de seconde est deja tres lente pour une boucle a 133 Hz.
        var carry = new StickPointerCarry();
        var frame = TimeSpan.FromMilliseconds(200);
        var output = StickPointerMapper.Map(With(new NormalizedStick(1, 0)), frame, Settings, rightStickPointer: true, leftStickWheel: true, ref carry);

        Assert.Equal((int)(Settings.PixelsPerSecond * frame.TotalSeconds), output.PixelsX);
    }

    [Fact]
    public void HalfDeflectionIsSlowerThanLinearBecauseOfTheCurve()
    {
        var carry = new StickPointerCarry();
        var frame = TimeSpan.FromMilliseconds(200);
        var output = StickPointerMapper.Map(With(new NormalizedStick(0.5, 0)), frame, Settings, rightStickPointer: true, leftStickWheel: true, ref carry);
        var linear = Settings.PixelsPerSecond * frame.TotalSeconds / 2;

        Assert.True(output.PixelsX < linear);
        Assert.True(output.PixelsX > 0);
    }

    // The "Stick droite" dropdown: a stick-only family (PS5/Xbox) has no pad to fall back on, so
    // switching the right stick off must actually stop the pointer — and must not leave a carry
    // behind that fires one last blip when it is switched back on.
    [Fact]
    public void ADisabledRightStickDoesNotMoveThePointer()
    {
        var carry = new StickPointerCarry();
        var frame = TimeSpan.FromMilliseconds(200);
        var output = StickPointerMapper.Map(
            With(new NormalizedStick(1, 0)), frame, Settings, rightStickPointer: false, leftStickWheel: true, ref carry);

        Assert.Equal(0, output.PixelsX);
        Assert.Equal(0, output.PixelsY);
    }

    [Fact]
    public void ADisabledRightStickDropsItsCarrySoNothingFiresOnReenable()
    {
        // Move the pointer first so a sub-pixel remainder accumulates.
        var carry = new StickPointerCarry();
        var frame = TimeSpan.FromMilliseconds(200);
        StickPointerMapper.Map(With(new NormalizedStick(1, 0)), TimeSpan.FromMilliseconds(5), Settings, rightStickPointer: true, leftStickWheel: true, ref carry);

        // Switch it off: the remainder must be zeroed, or re-enabling the stick would emit a blip.
        StickPointerMapper.Map(With(NormalizedStick.Center), frame, Settings, rightStickPointer: false, leftStickWheel: true, ref carry);
        var output = StickPointerMapper.Map(With(new NormalizedStick(1, 0)), frame, Settings, rightStickPointer: true, leftStickWheel: true, ref carry);

        Assert.Equal((int)(Settings.PixelsPerSecond * frame.TotalSeconds), output.PixelsX);
    }

    [Fact]
    public void ADisabledLeftStickDoesNotScroll()
    {
        var carry = new StickPointerCarry();
        var frame = TimeSpan.FromMilliseconds(200);
        var output = StickPointerMapper.Map(
            With(NormalizedStick.Center, new NormalizedStick(0, 1)), frame, Settings, rightStickPointer: true, leftStickWheel: false, ref carry);

        Assert.Equal(0, output.WheelNotches);
    }
}
