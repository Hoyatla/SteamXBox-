namespace Sc2Xboxed.Core.Input;

public readonly record struct ControllerState(
    TimeSpan Timestamp,
    SteamControllerButtons Buttons,
    NormalizedStick LeftStick,
    NormalizedStick RightStick,
    double LeftTrigger,
    double RightTrigger,
    TouchpadSample LeftPad,
    TouchpadSample RightPad)
{
    public static ControllerState Empty(TimeSpan timestamp)
    {
        return new ControllerState(
            timestamp,
            SteamControllerButtons.None,
            NormalizedStick.Center,
            NormalizedStick.Center,
            0.0,
            0.0,
            TouchpadSample.Released,
            TouchpadSample.Released);
    }

    public ControllerState Normalize()
    {
        return this with
        {
            LeftStick = LeftStick.Clamp(),
            RightStick = RightStick.Clamp(),
            LeftTrigger = Math.Clamp(LeftTrigger, 0.0, 1.0),
            RightTrigger = Math.Clamp(RightTrigger, 0.0, 1.0),
            LeftPad = LeftPad.Clamp(),
            RightPad = RightPad.Clamp()
        };
    }

    /// <summary>Whether anything on the pad is pressed or pushed enough to count as input.</summary>
    /// <remarks>
    /// Shared by the identity correlators, which only need to know <i>that</i> a pad spoke, not what
    /// it said. Small enough to ignore the resting jitter of an analog stick, large enough to catch a
    /// finger resting on a trigger.
    /// </remarks>
    public bool HasInput(double epsilon = 0.05)
    {
        return Buttons != SteamControllerButtons.None
            || Math.Abs(LeftStick.X) > epsilon
            || Math.Abs(LeftStick.Y) > epsilon
            || Math.Abs(RightStick.X) > epsilon
            || Math.Abs(RightStick.Y) > epsilon
            || LeftTrigger > epsilon
            || RightTrigger > epsilon;
    }
}
