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
}
