namespace Sc2Xboxed.Core.Output;

[Flags]
public enum Xbox360Buttons : ushort
{
    None = 0,

    DPadUp = 1 << 0,
    DPadDown = 1 << 1,
    DPadLeft = 1 << 2,
    DPadRight = 1 << 3,
    Start = 1 << 4,
    Back = 1 << 5,
    LeftThumb = 1 << 6,
    RightThumb = 1 << 7,
    LeftShoulder = 1 << 8,
    RightShoulder = 1 << 9,
    Guide = 1 << 10,

    A = 1 << 12,
    B = 1 << 13,
    X = 1 << 14,
    Y = 1 << 15
}
