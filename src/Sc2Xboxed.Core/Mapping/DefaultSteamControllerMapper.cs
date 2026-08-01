using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

public sealed class DefaultSteamControllerMapper
{
    private readonly Sc2XboxedProfileSettings _settings;
    private readonly LeftTouchpadScrollMapper _leftPad;
    private readonly RightTouchpadTrackballMapper _rightPad;
    private readonly TouchpadTapDetector _leftTap;
    private readonly TouchpadTapDetector _rightTap;

    public DefaultSteamControllerMapper()
        : this(Sc2XboxedProfileSettings.Default)
    {
    }

    public DefaultSteamControllerMapper(Sc2XboxedProfileSettings settings)
    {
        _settings = settings;
        _leftPad = new LeftTouchpadScrollMapper(settings.LeftPadScroll);
        _rightPad = new RightTouchpadTrackballMapper(settings.RightPadTrackball);
        _leftTap = new TouchpadTapDetector(settings.TouchpadTap);
        _rightTap = new TouchpadTapDetector(settings.TouchpadTap);
    }

    public ControllerOutputFrame Map(SteamControllerState state)
    {
        state = state.Normalize();

        // The tuning owns the dead zone now: it is radial rather than per-axis, and carries the
        // curve and sensitivity with it. The profile's own stick dead zone remains the fallback.
        var left = Tuning.ApplyStick(state.LeftStick.X, state.LeftStick.Y);
        var right = Tuning.ApplyStick(state.RightStick.X, state.RightStick.Y);

        var report = new Xbox360Report(
            MapButtons(state.Buttons),
            ToByteTrigger(Tuning.ApplyTrigger(state.LeftTrigger)),
            ToByteTrigger(Tuning.ApplyTrigger(state.RightTrigger)),
            ToThumbAxis(left.X),
            ToThumbAxis(left.Y),
            ToThumbAxis(right.X),
            ToThumbAxis(right.Y));

        var mouse = _leftPad
            .Update(state.Timestamp, state.LeftPad)
            .Add(_rightPad.Update(state.Timestamp, state.RightPad));

        return new ControllerOutputFrame(
            report,
            mouse,
            _leftTap.Update(state.Timestamp, state.LeftPad),
            _rightTap.Update(state.Timestamp, state.RightPad));
    }

    public void ResetTransientState()
    {
        _leftPad.Reset();
        _rightPad.Reset();
        _leftTap.Reset();
        _rightTap.Reset();
    }

    /// <summary>
    /// The active Xbox360-mode button mapping. Defaults to the built-in one, so nothing changes
    /// until a profile is loaded over it.
    /// </summary>
    public static XboxButtonMap ButtonMap { get; set; } = XboxButtonMap.Default;

    /// <summary>Active stick, trigger and vibration tuning for Xbox360 mode.</summary>
    public static XboxTuning Tuning { get; set; } = new();

    public static Xbox360Buttons MapButtons(SteamControllerButtons buttons) => ButtonMap.Apply(buttons);

    private double ApplyDeadZone(double value)
    {
        return Math.Abs(value) < _settings.GamepadStickDeadZone ? 0.0 : value;
    }

    private static byte ToByteTrigger(double normalized)
    {
        return (byte)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * byte.MaxValue);
    }

    private static short ToThumbAxis(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        return normalized < 0.0
            ? (short)Math.Round(normalized * 32768.0)
            : (short)Math.Round(normalized * 32767.0);
    }
}
