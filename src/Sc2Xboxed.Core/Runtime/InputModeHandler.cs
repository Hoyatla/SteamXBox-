using System;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Translates button chords into user intent: switch output mode, launch Steam, kill Steam.
/// It deliberately says nothing about the controller's native firmware layer — that follows from
/// <see cref="ControllerOwner"/>, tracked by <see cref="SteamPresenceWatcher"/>.
/// </summary>
public sealed class InputModeHandler
{
	private readonly SteamControllerButtons _switchButtons;
	private readonly TimeSpan _debounce;
	private bool _wasSwitchPressed;
	private bool _steamHeldWithY;
	private bool _wasSteamPressed;
	private TimeSpan? _lastToggle;

	public ControllerOutputMode CurrentMode { get; private set; }
	public bool SteamLaunchRequested { get; private set; }
	public bool SteamKillRequested { get; private set; }

	public InputModeHandler(ControllerOutputMode initialMode, SteamControllerButtons switchButtons, TimeSpan debounce)
	{
		CurrentMode = initialMode;
		_switchButtons = (switchButtons == SteamControllerButtons.None) ? SteamControllerButtons.QuickAccess : switchButtons;
		_debounce = debounce;
	}

	public bool Update(SteamControllerState state)
	{
		bool switchPressed = (state.Buttons & _switchButtons) != 0;
		bool switchRising = switchPressed && !_wasSwitchPressed;
		_wasSwitchPressed = switchPressed;

		bool steamPressed = state.Buttons.HasFlag(SteamControllerButtons.Steam);
		bool steamRising = steamPressed && !_wasSteamPressed;
		_wasSteamPressed = steamPressed;

		bool yPressed = state.Buttons.HasFlag(SteamControllerButtons.Y);

		SteamLaunchRequested = false;
		SteamKillRequested = false;

		if (steamRising)
		{
			if (yPressed)
			{
				SteamKillRequested = true;
				_steamHeldWithY = true;
				return false;
			}

			if (!_steamHeldWithY)
			{
				SteamLaunchRequested = true;
				return true;
			}
		}

		if (!steamPressed)
		{
			_steamHeldWithY = false;
		}

		if (switchRising)
		{
			if (_lastToggle.HasValue && state.Timestamp - _lastToggle.Value < _debounce)
				return false;

			CurrentMode = (CurrentMode == ControllerOutputMode.Xbox360)
				? ControllerOutputMode.Profile
				: ControllerOutputMode.Xbox360;

			_lastToggle = state.Timestamp;

			return true;
		}

		return false;
	}

	/// <summary>Overrides the current output mode, used by automatic foreground-based switching.</summary>
	public void SetMode(ControllerOutputMode mode)
	{
		CurrentMode = mode;
	}

	public SteamControllerState ConsumeButton(SteamControllerState state)
	{
		SteamControllerButtons buttons = state.Buttons & ~_switchButtons;
		if (SteamLaunchRequested || SteamKillRequested)
			buttons &= ~SteamControllerButtons.Steam;
		return state with { Buttons = buttons };
	}
}
