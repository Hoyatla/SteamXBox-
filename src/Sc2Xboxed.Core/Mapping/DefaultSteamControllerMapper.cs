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

    public ControllerOutputFrame Map(ControllerState state)
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
    /// This mapper's Xbox360-mode button mapping.
    /// </summary>
    /// <remarks>
    /// Per instance, so each controller can send a game its own layout — which is the whole of what
    /// "un profil Xbox par manette" means. It was static, and a static here meant one layout for the
    /// entire process: the assignment could be recorded, displayed and persisted, and every pad went
    /// on emitting whatever profile the bridge had been launched with.
    ///
    /// It defaults to <see cref="DefaultButtonMap"/>, which the startup path still sets once. A
    /// mapper built before any profile is chosen therefore behaves exactly as before, and only a
    /// mapper deliberately given its own map departs from it.
    /// </remarks>
    public XboxButtonMap ButtonMap { get; set; } = DefaultButtonMap;

    /// <summary>This mapper's stick, trigger and vibration tuning for Xbox360 mode.</summary>
    /// <inheritdoc cref="ButtonMap"/>
    public XboxTuning Tuning { get; set; } = DefaultTuning;

    /// <summary>
    /// What a mapper starts with when nothing has been chosen for it.
    /// </summary>
    /// <remarks>
    /// Still process-wide, and legitimately so: it is the profile the bridge was launched with, the
    /// answer for every controller that has none of its own. What was wrong was not having a shared
    /// default — it was having <i>only</i> a shared value.
    /// </remarks>
    public static XboxButtonMap DefaultButtonMap { get; set; } = XboxButtonMap.Default;

    /// <inheritdoc cref="DefaultButtonMap"/>
    public static XboxTuning DefaultTuning { get; set; } = new();

    public static Xbox360Buttons MapButtons(SteamControllerButtons buttons) => DefaultButtonMap.Apply(buttons);

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
