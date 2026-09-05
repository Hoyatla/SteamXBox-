namespace SenSÉ.Core.Output;

/// <summary>
/// One report to a virtual DualShock 4 pad.
/// </summary>
/// <remarks>
/// Axes are bytes, as a DualShock 4 reports them: 128 is centre, 0 and 255 are the extremes. The
/// stick Y axis is inverted relative to this product's conventions — up on a DualShock 4 is byte
/// zero, where <see cref="Input.ControllerState"/> treats up as +1 — so the mapper flips the sign
/// before packing.
/// </remarks>
public readonly record struct DS4Report(
    DS4Buttons Buttons,
    DS4Dpad Dpad,
    byte LeftTrigger,
    byte RightTrigger,
    byte LeftThumbX,
    byte LeftThumbY,
    byte RightThumbX,
    byte RightThumbY,
    bool PlayStation)
{
    public static DS4Report Neutral { get; } = new(
        DS4Buttons.None,
        DS4Dpad.None,
        0,
        0,
        128,
        128,
        128,
        128,
        false);
}
