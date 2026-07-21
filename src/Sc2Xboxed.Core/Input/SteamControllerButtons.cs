namespace Sc2Xboxed.Core.Input;

[Flags]
public enum SteamControllerButtons : ulong
{
    None = 0,

    A = 1UL << 0,
    B = 1UL << 1,
    X = 1UL << 2,
    Y = 1UL << 3,

    LeftBumper = 1UL << 4,
    RightBumper = 1UL << 5,
    LeftStick = 1UL << 6,
    RightStick = 1UL << 7,

    Menu = 1UL << 8,
    View = 1UL << 9,
    Steam = 1UL << 10,
    QuickAccess = 1UL << 11,

    DPadUp = 1UL << 12,
    DPadDown = 1UL << 13,
    DPadLeft = 1UL << 14,
    DPadRight = 1UL << 15,

    L4 = 1UL << 16,
    R4 = 1UL << 17,
    L5 = 1UL << 18,
    R5 = 1UL << 19
}
