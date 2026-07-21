using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Runtime;

public sealed class SteamButtonModeSwitcher
{
    private readonly SteamControllerButtons _switchButtons;
    private readonly TimeSpan _debounce;
    private bool _wasPressed;
    private TimeSpan? _lastToggle;

    public SteamButtonModeSwitcher(
        ControllerOutputMode initialMode,
        SteamControllerButtons switchButtons,
        TimeSpan debounce)
    {
        CurrentMode = initialMode;
        _switchButtons = switchButtons == SteamControllerButtons.None
            ? SteamControllerButtons.Steam
            : switchButtons;
        _debounce = debounce;
    }

    public ControllerOutputMode CurrentMode { get; private set; }

    public bool Update(SteamControllerState state)
    {
        var isPressed = (state.Buttons & _switchButtons) != SteamControllerButtons.None;
        var isRisingEdge = isPressed && !_wasPressed;
        _wasPressed = isPressed;

        if (!isRisingEdge ||
            (_lastToggle is { } lastToggle && state.Timestamp - lastToggle < _debounce))
        {
            return false;
        }

        CurrentMode = CurrentMode == ControllerOutputMode.Xbox360
            ? ControllerOutputMode.Native
            : ControllerOutputMode.Xbox360;

        _lastToggle = state.Timestamp;
        return true;
    }
}
