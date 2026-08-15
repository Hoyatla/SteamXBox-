using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Maps a controller to a DualShock 4 report, for a DualSense in gamepad mode.
/// </summary>
/// <remarks>
/// A DualSense in gamepad mode should look like what its labels promise: a PlayStation pad. The
/// Xbox-shaped report a DualSense used to emit made every game show it an Xbox controller — and the
/// button names in the GUI made the user's own rebinding read as lies. This mapper keeps the
/// profile's button map and tuning and emits a DualShock 4 report instead.
///
/// <para>
/// The button names are positional: A→Cross, B→Circle, X→Square, Y→Triangle, LB→L1, RB→R1, and so
/// on. The physical button <i>itself</i> is unchanged — the same finger, the same label — only the
/// name a game reads changes.
/// </para>
///
/// <para>
/// It shares <see cref="ControllerOutputMapper"/>'s statics so one startup path configures
/// both mappers: the button map and the tuning are the same profile values, only the emitted report
/// differs.
/// </para>
/// </remarks>
public sealed class DualSenseGamepadMapper
{
    /// <summary>
    /// The digital L2/R2 press is set when the trigger slider passes this fraction of its travel,
    /// as the firmware on a real DualShock 4 does; below it the button stays off while the analog
    /// slider still reports.
    /// </summary>
    public const double TriggerDigitalPoint = 0.2;

    /// <summary>This mapper's button mapping, shared with the Xbox mapper for the same controller.</summary>
    public XboxButtonMap ButtonMap { get; set; } = ControllerOutputMapper.DefaultButtonMap;

    /// <summary>This mapper's stick and trigger tuning, shared with the Xbox mapper.</summary>
    public XboxTuning Tuning { get; set; } = ControllerOutputMapper.DefaultTuning;

    public DS4Report Map(ControllerState state)
    {
        state = state.Normalize();

        var left = Tuning.ApplyStick(state.LeftStick.X, state.LeftStick.Y);
        var right = Tuning.ApplyStick(state.RightStick.X, state.RightStick.Y);

        var mapped = ButtonMap.Apply(state.Buttons);

        // A DualShock 4 reads up as zero on its Y axes, the opposite of ControllerState.
        var leftTrigger = ToByteTrigger(Tuning.ApplyTrigger(state.LeftTrigger));
        var rightTrigger = ToByteTrigger(Tuning.ApplyTrigger(state.RightTrigger));

        var buttons = ToDS4(mapped);
        if (leftTrigger >= (byte)(TriggerDigitalPoint * byte.MaxValue))
        {
            buttons |= DS4Buttons.TriggerLeft;
        }
        if (rightTrigger >= (byte)(TriggerDigitalPoint * byte.MaxValue))
        {
            buttons |= DS4Buttons.TriggerRight;
        }

        return new DS4Report(
            buttons,
            ToDpad(mapped),
            leftTrigger,
            rightTrigger,
            ToByteAxis(left.X),
            ToByteAxis(-left.Y),
            ToByteAxis(right.X),
            ToByteAxis(-right.Y),
            mapped.HasFlag(Xbox360Buttons.Guide));
    }

    /// <summary>The Xbox 360 buttons a report corresponds to, for the counters that span both families.</summary>
    public static Xbox360Buttons ToXbox360(DS4Report report)
    {
        var buttons = Xbox360Buttons.None;
        var b = report.Buttons;

        if (b.HasFlag(DS4Buttons.Cross)) buttons |= Xbox360Buttons.A;
        if (b.HasFlag(DS4Buttons.Circle)) buttons |= Xbox360Buttons.B;
        if (b.HasFlag(DS4Buttons.Square)) buttons |= Xbox360Buttons.X;
        if (b.HasFlag(DS4Buttons.Triangle)) buttons |= Xbox360Buttons.Y;
        if (b.HasFlag(DS4Buttons.ShoulderLeft)) buttons |= Xbox360Buttons.LeftShoulder;
        if (b.HasFlag(DS4Buttons.ShoulderRight)) buttons |= Xbox360Buttons.RightShoulder;
        if (b.HasFlag(DS4Buttons.ThumbLeft)) buttons |= Xbox360Buttons.LeftThumb;
        if (b.HasFlag(DS4Buttons.ThumbRight)) buttons |= Xbox360Buttons.RightThumb;
        if (b.HasFlag(DS4Buttons.Options)) buttons |= Xbox360Buttons.Start;
        if (b.HasFlag(DS4Buttons.Share)) buttons |= Xbox360Buttons.Back;
        if (report.PlayStation) buttons |= Xbox360Buttons.Guide;

        return buttons;
    }

    private static DS4Buttons ToDS4(Xbox360Buttons mapped)
    {
        var buttons = DS4Buttons.None;

        if (mapped.HasFlag(Xbox360Buttons.A)) buttons |= DS4Buttons.Cross;
        if (mapped.HasFlag(Xbox360Buttons.B)) buttons |= DS4Buttons.Circle;
        if (mapped.HasFlag(Xbox360Buttons.X)) buttons |= DS4Buttons.Square;
        if (mapped.HasFlag(Xbox360Buttons.Y)) buttons |= DS4Buttons.Triangle;
        if (mapped.HasFlag(Xbox360Buttons.LeftShoulder)) buttons |= DS4Buttons.ShoulderLeft;
        if (mapped.HasFlag(Xbox360Buttons.RightShoulder)) buttons |= DS4Buttons.ShoulderRight;
        if (mapped.HasFlag(Xbox360Buttons.LeftThumb)) buttons |= DS4Buttons.ThumbLeft;
        if (mapped.HasFlag(Xbox360Buttons.RightThumb)) buttons |= DS4Buttons.ThumbRight;
        if (mapped.HasFlag(Xbox360Buttons.Start)) buttons |= DS4Buttons.Options;
        if (mapped.HasFlag(Xbox360Buttons.Back)) buttons |= DS4Buttons.Share;

        return buttons;
    }

    private static DS4Dpad ToDpad(Xbox360Buttons mapped)
    {
        var up = mapped.HasFlag(Xbox360Buttons.DPadUp);
        var down = mapped.HasFlag(Xbox360Buttons.DPadDown);
        var left = mapped.HasFlag(Xbox360Buttons.DPadLeft);
        var right = mapped.HasFlag(Xbox360Buttons.DPadRight);

        if (up && right) return DS4Dpad.UpRight;
        if (down && right) return DS4Dpad.DownRight;
        if (down && left) return DS4Dpad.DownLeft;
        if (up && left) return DS4Dpad.UpLeft;
        if (up) return DS4Dpad.Up;
        if (down) return DS4Dpad.Down;
        if (left) return DS4Dpad.Left;
        if (right) return DS4Dpad.Right;

        return DS4Dpad.None;
    }

    private static byte ToByteTrigger(double normalized)
    {
        return (byte)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * byte.MaxValue);
    }

    private static byte ToByteAxis(double normalized)
    {
        return (byte)Math.Round((Math.Clamp(normalized, -1.0, 1.0) + 1.0) / 2.0 * byte.MaxValue);
    }
}
