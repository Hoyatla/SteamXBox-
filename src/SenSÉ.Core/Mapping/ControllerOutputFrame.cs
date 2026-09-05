using SenSÉ.Core.Output;
using SenSÉ.Core.Input;

namespace SenSÉ.Core.Mapping;

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
