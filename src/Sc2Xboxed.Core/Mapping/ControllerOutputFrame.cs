using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Mapping;

public readonly record struct ControllerOutputFrame(
    Xbox360Report Gamepad,
    MouseOutputFrame Mouse,
    TouchpadTap LeftPadTap,
    TouchpadTap RightPadTap)
{
    public static ControllerOutputFrame Empty { get; } = new(
        Xbox360Report.Neutral,
        MouseOutputFrame.Empty,
        TouchpadTap.None,
        TouchpadTap.None);
}
