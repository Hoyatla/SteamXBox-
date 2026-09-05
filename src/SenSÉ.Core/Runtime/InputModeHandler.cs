using System;
using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;

namespace SenSÉ.Core.Runtime;

/// <summary>
/// Translates button chords into user intent: switch output mode, launch Steam software.
/// It deliberately says nothing about the controller's native firmware layer — that follows from
/// <see cref="ControllerOwner"/>, tracked by <see cref="SteamPresenceWatcher"/>.
/// </summary>
public sealed class InputModeHandler
{
	private readonly SteamControllerButtons _switchButtons;
	private readonly TimeSpan _debounce;
	private bool _wasSwitchPressed;
	private bool _wasSteamPressed;
	private bool _chordConsumed;
	private TimeSpan? _chordHeldSince;
	private TimeSpan? _lastToggle;

	/// <summary>How long both stick clicks must be held together before the mode changes.</summary>
	/// <remarks>
	/// Two seconds because L3 and R3 are game buttons. Anything shorter and an ordinary
	/// sprint-and-crouch switches the controller mid-play; this is the constant that separates a
	/// deliberate chord from a collision.
	/// </remarks>
	private static readonly TimeSpan ChordHold = TimeSpan.FromSeconds(2);

	public ControllerOutputMode CurrentMode { get; private set; }

	/// <summary>
	/// The user pressed the Steam (PS) button alone on a Steam Controller, with the right
	/// trackpad not being used as a mouse: launch Steam software.
	/// </summary>
	/// <remarks>
	/// Steam software has its own close button, so there is no kill chord from SenSÉ's side. When
	/// Steam exits, SteamPresenceWatcher detects it and the controller reclaims on its own.
	///
	/// <para>
	/// Two filters gate the launch (both 2026-09-05, after the user reported that any pad use was
	/// firing the launch):
	/// </para>
	///
	/// <list type="bullet">
	///   <item>
	///     The frame must come from a Steam Controller. The XInput mapper folds the Xbox Guide
	///     into <see cref="SteamControllerButtons.Steam"/> (XInputStateMapper.cs:114), and the
	///     DualSense parser folds the PS button in too (DualSenseReportParser.cs:220). The launch
	///     is a Steam-Controller-only feature; a Guide / PS press on a different pad must not
	///     fire it.
	///   </item>
	///   <item>
	///     The right trackpad must not be touched or pressed. The Steam Controller firmware
	///     reports the Steam bit on right trackpad activity (mouse-move / click). The thumb
	///     sits on the trackpad only when using it as a mouse, not when pressing the Steam
	///     button, so the gate filters the false launches while a real press goes through.
	///   </item>
	/// </list>
	/// </remarks>
	public bool SteamLaunchRequested { get; private set; }

	public InputModeHandler(ControllerOutputMode initialMode, SteamControllerButtons switchButtons, TimeSpan debounce)
	{
		CurrentMode = initialMode;
		_switchButtons = (switchButtons == SteamControllerButtons.None) ? SteamControllerButtons.QuickAccess : switchButtons;
		_debounce = debounce;
	}

	public bool Update(ControllerState state, ControllerKind source = ControllerKind.SteamController)
	{
		bool switchPressed = (state.Buttons & _switchButtons) != 0;
		bool switchRising = switchPressed && !_wasSwitchPressed;
		_wasSwitchPressed = switchPressed;

		bool steamPressed = state.Buttons.HasFlag(SteamControllerButtons.Steam);

		// Steam-launch gates (2026-09-05): only the Steam Controller's physical Steam button
		// fires the launch. See SteamLaunchRequested remarks for the two reasons.
		bool fromSteamController = source == ControllerKind.SteamController;
		bool trackpadActive = state.RightPad.IsTouched || state.RightPad.IsPressed;
		bool steamForLaunch = steamPressed && fromSteamController && !trackpadActive;
		bool steamRisingForLaunch = steamForLaunch && !_wasSteamPressed;
		_wasSteamPressed = steamPressed;

		SteamLaunchRequested = false;

		if (steamRisingForLaunch)
		{
			// Steam software has its own close button — no kill chord. Reclaim happens when
			// Steam exits (SteamPresenceWatcher at Program.cs:1871-1877).
			SteamLaunchRequested = true;
			return false;
		}

		// Both stick clicks held together also switch mode. It works in either direction and in
		// either mode, which is the point: a controller stuck in Xbox mode with no way back would
		// need the keyboard to recover. On a pad with no quick-access button — a DualSense, an Xbox
		// controller — it is the only way, so it has to be reliable in both directions.
		//
		// HELD, not pressed. L3 and R3 are ordinary game buttons and hitting both at the same instant
		// is something a player does by accident all the time. Measured on 14 August: two switches at
		// 22:51:51.831 and 22:51:52.675, a second apart, with nothing but the two sticks being pushed
		// — the pad changed mode twice while the user thought he was aiming. The hold is what tells a
		// deliberate chord from a collision, and two seconds is long enough that no game input reaches
		// it by chance.
		//
		// The cost, named: for those two seconds both clicks still reach the game or the profile. That
		// is the price of a hold and it is the right way round — a short simultaneous press has to
		// stay a short simultaneous press.
		var chord = SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick;
		bool chordHeld = (state.Buttons & chord) == chord;

		if (!chordHeld)
			_chordHeldSince = null;
		else
			_chordHeldSince ??= state.Timestamp;

		// The latch clears only once both clicks are released. Withholding on the switching frame
		// alone would leak L3 and R3 to the game for every frame the user keeps holding them, which
		// is most of them — a chord is held for a moment, not for eight milliseconds.
		if ((state.Buttons & chord) == 0)
			_chordConsumed = false;

		// Once per hold: _chordConsumed stays set until both clicks come back up, so keeping them
		// down does not switch again every frame after the second second.
		bool chordCompleted =
			!_chordConsumed &&
			_chordHeldSince is { } heldSince &&
			state.Timestamp - heldSince >= ChordHold;

		if (switchRising || chordCompleted)
		{
			if (_lastToggle.HasValue && state.Timestamp - _lastToggle.Value < _debounce)
				return false;

			if (chordCompleted)
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
	public void SetMode(ControllerOutputMode mode	)
	{
		CurrentMode = mode;
	}

	/// <summary>Strips the buttons that were consumed as commands, before the mappers see them.</summary>
	/// <remarks>
	/// The Steam button is withheld along with the command that used it, so the same press does not
	/// also reach the profile mapper as a plain button.
	/// </remarks>
	public ControllerState ConsumeButton(ControllerState state)
	{
		SteamControllerButtons buttons = state.Buttons & ~_switchButtons;

		if (SteamLaunchRequested)
			buttons &= ~SteamControllerButtons.Steam;

		// Held from the moment the chord switches mode until both clicks are released. Otherwise the
		// switch also fires whatever the profile or the game binds to L3 and R3, at the exact instant
		// the mode changes.
		if (_chordConsumed)
			buttons &= ~(SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick);

		return state with { Buttons = buttons };
	}
}
