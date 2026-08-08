using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>How far the pointer and the wheel moved in one frame.</summary>
/// <param name="PixelsX">Horizontal pointer travel, in whole pixels.</param>
/// <param name="PixelsY">Vertical pointer travel, in whole pixels.</param>
/// <param name="WheelNotches">Wheel detents, positive upwards.</param>
public readonly record struct StickPointerOutput(int PixelsX, int PixelsY, int WheelNotches);

/// <summary>
/// Feel of the two sticks. They do different jobs and are tuned apart.
/// </summary>
/// <remarks>
/// One dead zone and one curve used to serve both, which cannot be right: the right stick points and
/// the left stick scrolls. Pointing wants a wide, fine range — a direction the pointer follows, with
/// resolution near the centre for aiming and speed at the edge for crossing the screen. Scrolling
/// wants a firm threshold and something close to linear, because a thumb resting on a stick should
/// produce no scroll at all and a deliberate push should produce a predictable rate. Tuning one to
/// feel right made the other feel wrong, every time, and the setting that fixed it did not exist.
/// </remarks>
/// <param name="DeadZone">Below this magnitude the pointing stick is treated as centred.</param>
/// <param name="PixelsPerSecond">Pointer speed at full deflection.</param>
/// <param name="Curve">Exponent applied to the pointing magnitude; 1 is linear, higher is finer near centre.</param>
/// <param name="NotchesPerSecond">Wheel detents per second at full deflection.</param>
/// <param name="WheelDeadZone">Below this magnitude the scrolling stick is treated as centred.</param>
/// <param name="WheelCurve">Exponent applied to the scrolling magnitude.</param>
public sealed record StickPointerSettings(
    double DeadZone = 0.15,
    double PixelsPerSecond = 1400,
    double Curve = 2.0,
    double NotchesPerSecond = 12,
    double WheelDeadZone = 0.25,
    double WheelCurve = 1.2);

/// <summary>
/// Drives the pointer and the wheel from two sticks, for controllers that have no trackpads.
/// </summary>
/// <remarks>
/// An Xbox or DualSense pad has no touchpad, so the mapping a Steam Controller uses — right pad
/// moves the pointer, left pad scrolls — has nothing to attach to. The sticks take those roles:
/// right stick moves the pointer, left stick scrolls.
///
/// The difference that matters is not which input is used but what it means. A trackpad reports a
/// <em>position</em>, so a finger that stops moving stops the pointer. A stick reports a
/// <em>displacement</em> that persists while held, so it drives a velocity: held right, the pointer
/// keeps travelling. Everything below follows from that.
/// </remarks>
public static class StickPointerMapper
{
    /// <summary>
    /// Converts one frame of stick deflection into pointer and wheel movement.
    /// </summary>
    /// <param name="state">Frame to read the sticks from.</param>
    /// <param name="elapsed">Time since the previous frame.</param>
    /// <param name="settings">Feel of the mapping.</param>
    /// <param name="rightStickPointer">
    /// Whether the right stick drives the pointer. A profile that switched its right stick off must
    /// not move the cursor; a stick-only family cannot fall back to a pad that is not there.
    /// </param>
    /// <param name="leftStickWheel">
    /// Whether the left stick scrolls. Kept separate so "Aucun" on the left stick stops both its
    /// roles instead of leaving the wheel alive while the combobox says it does nothing.
    /// </param>
    /// <param name="carry">
    /// Sub-pixel and sub-notch remainder carried between frames, updated in place. Without it a slow
    /// stick would round to zero on every frame and the pointer would never move at all — the
    /// classic reason a low sensitivity feels broken rather than slow.
    /// </param>
    public static StickPointerOutput Map(
        ControllerState state,
        TimeSpan elapsed,
        StickPointerSettings settings,
        bool rightStickPointer,
        bool leftStickWheel,
        ref StickPointerCarry carry)
    {
        var seconds = elapsed.TotalSeconds;
        if (seconds <= 0 || seconds > 0.25)
        {
            // A frame gap that large means the loop stalled or the controller reconnected. Moving
            // the pointer by a quarter second of accumulated deflection would look like a jump.
            return new StickPointerOutput(0, 0, 0);
        }

        if (rightStickPointer)
        {
            var (px, py) = Velocity(state.RightStick, settings.DeadZone, settings.Curve);
            carry.X += px * settings.PixelsPerSecond * seconds;
            carry.Y += py * settings.PixelsPerSecond * seconds;
        }
        else
        {
            // The stick is off: drop any remainder, or a disabled stick would fire one last blip
            // the next time it is re-enabled.
            carry.X = 0;
            carry.Y = 0;
        }

        if (leftStickWheel)
        {
            // Only the vertical axis of the left stick scrolls. Horizontal wheel exists, but binding
            // it here would make a diagonal push scroll sideways by accident on every vertical flick.
            var (_, wy) = Velocity(state.LeftStick, settings.WheelDeadZone, settings.WheelCurve);
            carry.Wheel += -wy * settings.NotchesPerSecond * seconds;
        }
        else
        {
            carry.Wheel = 0;
        }

        var outX = (int)carry.X;
        var outY = (int)carry.Y;
        var outWheel = (int)carry.Wheel;

        carry.X -= outX;
        carry.Y -= outY;
        carry.Wheel -= outWheel;

        return new StickPointerOutput(outX, outY, outWheel);
    }

    /// <summary>
    /// Turns a stick position into a direction and a speed between 0 and 1.
    /// </summary>
    /// <remarks>
    /// The dead zone is radial, not per axis. Applied per axis it would carve a square hole out of a
    /// round stick: pushing straight up would work while pushing up-left within the same distance
    /// would not, and diagonals would start moving before the cardinals do.
    ///
    /// The magnitude is also rescaled after the dead zone is removed, so the stick still reaches
    /// full speed at its physical limit rather than losing the first fifteen percent of its travel.
    /// </remarks>
    private static (double X, double Y) Velocity(NormalizedStick stick, double deadZone, double curve)
    {
        var magnitude = Math.Sqrt(stick.X * stick.X + stick.Y * stick.Y);
        if (magnitude <= deadZone)
        {
            return (0, 0);
        }

        var scaled = Math.Min(1.0, (magnitude - deadZone) / (1.0 - deadZone));
        var speed = Math.Pow(scaled, curve);

        // Y is inverted once, here: the sticks report up as positive, screens count down as
        // positive. Doing it at the call site is how one of the two axes ends up inverted.
        return (stick.X / magnitude * speed, -stick.Y / magnitude * speed);
    }
}

/// <summary>Fractional pointer and wheel movement carried between frames.</summary>
public struct StickPointerCarry
{
    public double X;
    public double Y;
    public double Wheel;
}
