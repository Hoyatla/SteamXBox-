namespace Sc2Xboxed.Core.Output;

public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY)
{
    public static Xbox360Report Neutral { get; } = new(
        Xbox360Buttons.None,
        0,
        0,
        0,
        0,
        0,
        0);
}
