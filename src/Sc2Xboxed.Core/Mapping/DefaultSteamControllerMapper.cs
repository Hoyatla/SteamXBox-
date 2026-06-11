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

        var report = new Xbox360Report(
            MapButtons(state.Buttons),
            ToByteTrigger(state.LeftTrigger),
            ToByteTrigger(state.RightTrigger),
            ToThumbAxis(ApplyDeadZone(state.LeftStick.X)),
            ToThumbAxis(ApplyDeadZone(state.LeftStick.Y)),
            ToThumbAxis(ApplyDeadZone(state.RightStick.X)),
            ToThumbAxis(ApplyDeadZone(state.RightStick.Y)));

        var mouse = _leftPad
            .Update(state.LeftPad)
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

    public static Xbox360Buttons MapButtons(SteamControllerButtons buttons)
    {
        var mapped = Xbox360Buttons.None;

        mapped |= buttons.HasFlag(SteamControllerButtons.A) ? Xbox360Buttons.A : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.B) ? Xbox360Buttons.B : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.X) ? Xbox360Buttons.X : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.Y) ? Xbox360Buttons.Y : Xbox360Buttons.None;

        mapped |= buttons.HasFlag(SteamControllerButtons.LeftBumper) ? Xbox360Buttons.LeftShoulder : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.RightBumper) ? Xbox360Buttons.RightShoulder : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.LeftStick) ? Xbox360Buttons.LeftThumb : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.RightStick) ? Xbox360Buttons.RightThumb : Xbox360Buttons.None;

        mapped |= buttons.HasFlag(SteamControllerButtons.Menu) ? Xbox360Buttons.Back : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.View) ? Xbox360Buttons.Start : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.Steam) ? Xbox360Buttons.Guide : Xbox360Buttons.None;

        mapped |= buttons.HasFlag(SteamControllerButtons.DPadUp) ? Xbox360Buttons.DPadUp : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.DPadDown) ? Xbox360Buttons.DPadDown : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.DPadLeft) ? Xbox360Buttons.DPadLeft : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.DPadRight) ? Xbox360Buttons.DPadRight : Xbox360Buttons.None;

        mapped |= buttons.HasFlag(SteamControllerButtons.L4) ? Xbox360Buttons.X : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.R4) ? Xbox360Buttons.Y : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.L5) ? Xbox360Buttons.A : Xbox360Buttons.None;
        mapped |= buttons.HasFlag(SteamControllerButtons.R5) ? Xbox360Buttons.B : Xbox360Buttons.None;

        return mapped;
    }

    private double ApplyDeadZone(double value)
    {
        return Math.Abs(value) < _settings.StickDeadZone ? 0.0 : value;
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
