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
	private bool _steamUsedAsModifier;
	private bool _wasSteamPressed;
	private bool _wasChordHeld;
	private bool _chordConsumed;
	private TimeSpan? _lastToggle;

	public ControllerOutputMode CurrentMode { get; private set; }
	public bool SteamLaunchRequested { get; private set; }
	public bool SteamKillRequested { get; private set; }

	/// <summary>
	/// The user asked for the SteamXBox environment: Steam pressed while X is held.
	/// </summary>
	/// <remarks>
	/// Steam is already the system modifier here — alone it launches Steam, with Y it kills it — so
	/// the environment joins that vocabulary rather than inventing a second one. It cannot have the
	/// Steam button to itself: that one is taken.
	///
	/// Only ever set in <see cref="ControllerOutputMode.Profile"/>. In Xbox mode the controller is a
	/// gamepad and nothing else: a chord that opened a window mid-game would be a bug, not a
	/// feature. That is what makes switching to Xbox "unbind the tools" without anything having to
	/// be undone — the request is simply never raised.
	/// </remarks>
	public bool DesktopRequested { get; private set; }

	public InputModeHandler(ControllerOutputMode initialMode, SteamControllerButtons switchButtons, TimeSpan debounce)
	{
		CurrentMode = initialMode;
		_switchButtons = (switchButtons == SteamControllerButtons.None) ? SteamControllerButtons.QuickAccess : switchButtons;
		_debounce = debounce;
	}

	public bool Update(ControllerState state)
	{
		bool switchPressed = (state.Buttons & _switchButtons) != 0;
		bool switchRising = switchPressed && !_wasSwitchPressed;
		_wasSwitchPressed = switchPressed;

		bool steamPressed = state.Buttons.HasFlag(SteamControllerButtons.Steam);
		bool steamRising = steamPressed && !_wasSteamPressed;
		_wasSteamPressed = steamPressed;

		bool yPressed = state.Buttons.HasFlag(SteamControllerButtons.Y);
		bool xPressed = state.Buttons.HasFlag(SteamControllerButtons.X);

		SteamLaunchRequested = false;
		SteamKillRequested = false;
		DesktopRequested = false;

		if (steamRising)
		{
			if (yPressed)
			{
				SteamKillRequested = true;
				_steamUsedAsModifier = true;
				return false;
			}

			// Profile mode only. In Xbox mode the pad is a gamepad and X is a game button: raising
			// this there would open a window in the middle of play.
			if (xPressed && CurrentMode == ControllerOutputMode.Profile)
			{
				DesktopRequested = true;
				_steamUsedAsModifier = true;
				return false;
			}

			// The flag keeps a chord from also launching Steam when the modifier is released after
			// the Steam button.
			if (!_steamUsedAsModifier)
			{
				SteamLaunchRequested = true;
				return true;
			}
		}

		if (!steamPressed)
		{
			_steamUsedAsModifier = false;
		}

		// Both stick clicks held together also switch mode. It works in either direction and in
		// either mode, which is the point: a controller stuck in Xbox mode with no way back would
		// need the keyboard to recover.
		//
		// The cost is real and worth naming: L3 and R3 are game buttons, so a title that binds both
		// — sprint and crouch, say — will switch mode when they are pressed at the same instant.
		// Rare enough to accept, common enough to keep the quick-access button as the everyday way.
		var chord = SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick;
		bool chordHeld = (state.Buttons & chord) == chord;
		bool chordRising = chordHeld && !_wasChordHeld;
		_wasChordHeld = chordHeld;

		// The latch clears only once both clicks are released. Withholding on the switching frame
		// alone would leak L3 and R3 to the game for every frame the user keeps holding them, which
		// is most of them — a chord is held for a moment, not for eight milliseconds.
		if ((state.Buttons & chord) == 0)
			_chordConsumed = false;

		if (switchRising || chordRising)
		{
			if (_lastToggle.HasValue && state.Timestamp - _lastToggle.Value < _debounce)
				return false;

			if (chordRising)
				_chordConsumed = true;

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

	/// <summary>Strips the buttons that were consumed as commands, before the mappers see them.</summary>
	/// <remarks>
	/// X is withheld along with Steam when the environment was requested. Without it the chord would
	/// also fire whatever the profile binds X to — a click, a key — at the same moment the
	/// environment opens.
	/// </remarks>
	public ControllerState ConsumeButton(ControllerState state)
	{
		SteamControllerButtons buttons = state.Buttons & ~_switchButtons;

		if (SteamLaunchRequested || SteamKillRequested || DesktopRequested)
			buttons &= ~SteamControllerButtons.Steam;

		if (DesktopRequested)
			buttons &= ~SteamControllerButtons.X;

		// Held from the moment the chord switches mode until both clicks are released. Otherwise the
		// switch also fires whatever the profile or the game binds to L3 and R3, at the exact instant
		// the mode changes.
		if (_chordConsumed)
			buttons &= ~(SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick);

		return state with { Buttons = buttons };
	}
}
