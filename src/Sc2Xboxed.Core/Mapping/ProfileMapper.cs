using System;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

public sealed class ProfileMapper
{
	private bool _prevRightTriggerDown;
	private bool _prevLeftTriggerDown;
	private bool _prevRightPadClick;
	private bool _prevLeftPadClick;
	private bool _prevL4;
	private bool _prevR4;
	private bool _prevL5;
	private bool _prevR5;
	private bool _prevDPadUp;
	private bool _prevDPadDown;
	private bool _prevDPadLeft;
	private bool _prevDPadRight;
	private bool _prevLB;
	private bool _prevRB;
	private bool _prevX;
	private bool _prevY;
	private bool _prevA;
	private bool _prevB;
	private bool _prevL3;
	private bool _prevR3;
	private bool _prevMenu;
	private bool _prevView;

	private readonly RightTouchpadTrackballMapper _rightTrackball = new(RightTouchpadTrackballSettings.Default with { InvertY = true });
	private readonly LeftTouchpadScrollMapper _leftScroll = new(LeftTouchpadScrollSettings.Default with { InvertVertical = true, WheelDeltaPerPadUnit = 10.0 });
	private readonly RightTouchpadTrackballMapper _leftTrackball = new(RightTouchpadTrackballSettings.Default with { InvertY = true });
	private readonly SmoothedTouchpadInput _rightPadSmooth = new();

	private bool _leftPadWasOskMode;
	private bool _firstFrame = true;

	public bool CursorMoved { get; private set; }
	public bool Scrolled { get; private set; }
	public bool PadClicked { get; private set; }
	public bool OskToggleRequested { get; private set; }
	public bool OskActive { get; set; }

	public void Reset()
	{
		_prevRightTriggerDown = false;
		_prevLeftTriggerDown = false;
		_prevRightPadClick = false;
		_prevLeftPadClick = false;
		_prevL4 = false;
		_prevR4 = false;
		_prevL5 = false;
		_prevR5 = false;
		_prevDPadUp = false;
		_prevDPadDown = false;
		_prevDPadLeft = false;
		_prevDPadRight = false;
		_prevLB = false;
		_prevRB = false;
		_prevX = false;
		_prevY = false;
		_prevA = false;
		_prevB = false;
		_prevL3 = false;
		_prevR3 = false;
		_prevMenu = false;
		_prevView = false;

		_rightTrackball.Reset();
		_leftScroll.Reset();
		_leftTrackball.Reset();
		_rightPadSmooth.Reset();
		_leftPadWasOskMode = false;
	}

	public void Map(SteamControllerState state)
	{
		state = state.Normalize();

		CursorMoved = false;
		Scrolled = false;
		PadClicked = false;
		OskToggleRequested = false;

		if (_firstFrame)
		{
			_prevRightTriggerDown = state.RightTrigger > 0.5;
			_prevLeftTriggerDown = state.LeftTrigger > 0.5;
			_prevRightPadClick = state.RightPad.IsPressed;
			_prevLeftPadClick = state.LeftPad.IsPressed;
			_prevL4 = state.Buttons.HasFlag(SteamControllerButtons.L4);
			_prevR4 = state.Buttons.HasFlag(SteamControllerButtons.R4);
			_prevL5 = state.Buttons.HasFlag(SteamControllerButtons.L5);
			_prevR5 = state.Buttons.HasFlag(SteamControllerButtons.R5);
			_prevDPadUp = state.Buttons.HasFlag(SteamControllerButtons.DPadUp);
			_prevDPadDown = state.Buttons.HasFlag(SteamControllerButtons.DPadDown);
			_prevDPadLeft = state.Buttons.HasFlag(SteamControllerButtons.DPadLeft);
			_prevDPadRight = state.Buttons.HasFlag(SteamControllerButtons.DPadRight);
			_prevLB = state.Buttons.HasFlag(SteamControllerButtons.LeftBumper);
			_prevRB = state.Buttons.HasFlag(SteamControllerButtons.RightBumper);
			_prevX = state.Buttons.HasFlag(SteamControllerButtons.X);
			_prevY = state.Buttons.HasFlag(SteamControllerButtons.Y);
			_prevA = state.Buttons.HasFlag(SteamControllerButtons.A);
			_prevB = state.Buttons.HasFlag(SteamControllerButtons.B);
			_prevL3 = state.Buttons.HasFlag(SteamControllerButtons.LeftStick);
			_prevR3 = state.Buttons.HasFlag(SteamControllerButtons.RightStick);
			_prevMenu = state.Buttons.HasFlag(SteamControllerButtons.Menu);
			_prevView = state.Buttons.HasFlag(SteamControllerButtons.View);
			_firstFrame = false;
			return;
		}

		bool rightTriggerDown = state.RightTrigger > 0.5;
		bool leftTriggerDown = state.LeftTrigger > 0.5;

		HandleEdge(ref _prevRightTriggerDown, rightTriggerDown, () => InputHelper.MouseLeftDown(), () => InputHelper.MouseLeftUp());
		HandleEdge(ref _prevLeftTriggerDown, leftTriggerDown, () => InputHelper.MouseRightDown(), () => InputHelper.MouseRightUp());

		if (!OskActive)
		{
			var rightSmooth = _rightPadSmooth.Update(state.RightPad);
			var rightFrame = _rightTrackball.Update(state.Timestamp, rightSmooth);
			ApplyMouseFrame(rightFrame);
			CursorMoved = rightSmooth.IsTouched && rightFrame.HasMouseMotion && (Math.Abs(rightFrame.DeltaX) > 2.0 || Math.Abs(rightFrame.DeltaY) > 2.0);
			HandleEdge(ref _prevRightPadClick, rightSmooth.IsPressed, () => { InputHelper.MouseLeftDown(); PadClicked = true; }, () => InputHelper.MouseLeftUp());

			var leftFrame = MapLeftPad(state.Timestamp, state.LeftPad);
			ApplyMouseFrame(leftFrame);
			Scrolled = leftFrame.HasWheel;
			HandleEdge(ref _prevLeftPadClick, state.LeftPad.IsPressed, () => InputHelper.MouseMiddleDown(), () => InputHelper.MouseMiddleUp());
		}
		else
		{
			HandleEdge(ref _prevRightPadClick, state.RightPad.IsPressed, () => { }, () => { });
			HandleEdge(ref _prevLeftPadClick, state.LeftPad.IsPressed, () => { }, () => { });
			_leftPadWasOskMode = InputHelper.IsOskRunning();
		}

		HandleEdge(ref _prevLB, state.Buttons.HasFlag(SteamControllerButtons.LeftBumper),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_TAB }),
			() => { });
		HandleEdge(ref _prevRB, state.Buttons.HasFlag(SteamControllerButtons.RightBumper),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, InputHelper.VK_TAB }),
			() => { });

		HandleEdge(ref _prevDPadUp, state.Buttons.HasFlag(SteamControllerButtons.DPadUp),
			() => InputHelper.KeyDown(0xAF), () => InputHelper.KeyUp(0xAF));
		HandleEdge(ref _prevDPadDown, state.Buttons.HasFlag(SteamControllerButtons.DPadDown),
			() => InputHelper.KeyDown(0xAE), () => InputHelper.KeyUp(0xAE));
		HandleEdge(ref _prevDPadLeft, state.Buttons.HasFlag(SteamControllerButtons.DPadLeft),
			() => InputHelper.KeyTap(0xB1), () => { });
		HandleEdge(ref _prevDPadRight, state.Buttons.HasFlag(SteamControllerButtons.DPadRight),
			() => InputHelper.KeyTap(0xB0), () => { });

		HandleEdge(ref _prevL4, state.Buttons.HasFlag(SteamControllerButtons.L4),
			() => InputHelper.KeyTap(InputHelper.VK_SNAPSHOT), () => { });
		HandleEdge(ref _prevR4, state.Buttons.HasFlag(SteamControllerButtons.R4),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, 0x47 }),
			() => { });

		HandleEdge(ref _prevL5, state.Buttons.HasFlag(SteamControllerButtons.L5),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, InputHelper.VK_MENU, 0x52 }),
			() => { });
		HandleEdge(ref _prevR5, state.Buttons.HasFlag(SteamControllerButtons.R5),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_F4 }),
			() => { });

		HandleEdge(ref _prevX, state.Buttons.HasFlag(SteamControllerButtons.X),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_LEFT }),
			() => { });
		HandleEdge(ref _prevY, state.Buttons.HasFlag(SteamControllerButtons.Y),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_RIGHT }),
			() => { });

		HandleEdge(ref _prevA, state.Buttons.HasFlag(SteamControllerButtons.A),
			() =>
			{
				if (OskActive)
				{
					OskToggleRequested = true;
				}
			}, () => { });
		HandleEdge(ref _prevB, state.Buttons.HasFlag(SteamControllerButtons.B),
			() =>
			{
				OskToggleRequested = true;
			}, () => { });

		HandleEdge(ref _prevMenu, state.Buttons.HasFlag(SteamControllerButtons.Menu),
			() => InputHelper.KeyTap(0x5B), () => { });
		HandleEdge(ref _prevView, state.Buttons.HasFlag(SteamControllerButtons.View),
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, 0x44 }),
			() => { });

		HandleEdge(ref _prevL3, state.Buttons.HasFlag(SteamControllerButtons.LeftStick),
			() => InputHelper.KeyTap(0x0D), () => { });
		HandleEdge(ref _prevR3, state.Buttons.HasFlag(SteamControllerButtons.RightStick),
			() => { }, () => { });

		MapLeftStickArrows(state.LeftStick);
	}

	private MouseOutputFrame MapLeftPad(TimeSpan timestamp, TouchpadSample pad)
	{
		bool oskNow = InputHelper.IsOskRunning();

		if (oskNow && !_leftPadWasOskMode)
		{
			_leftScroll.Reset();
			_leftTrackball.Reset();
			_leftPadWasOskMode = true;
		}
		else if (!oskNow && _leftPadWasOskMode)
		{
			_leftTrackball.Reset();
			_leftScroll.Reset();
			_leftPadWasOskMode = false;
		}

		if (oskNow)
			return _leftTrackball.Update(timestamp, pad);

		return _leftScroll.Update(pad);
	}

	private static void ApplyMouseFrame(MouseOutputFrame frame)
	{
		if (frame.HasMouseMotion)
			InputHelper.MouseMoveRelative((int)frame.DeltaX, (int)frame.DeltaY);

		if (frame.HasWheel)
			InputHelper.MouseWheel(frame.WheelDelta);
	}

	private void MapLeftStickArrows(NormalizedStick stick)
	{
		double deadzone = 0.5;

		if (stick.Y > deadzone)
			InputHelper.KeyDown(InputHelper.VK_UP);
		else
			InputHelper.KeyUp(InputHelper.VK_UP);

		if (stick.Y < -deadzone)
			InputHelper.KeyDown(InputHelper.VK_DOWN);
		else
			InputHelper.KeyUp(InputHelper.VK_DOWN);

		if (stick.X < -deadzone)
			InputHelper.KeyDown(InputHelper.VK_LEFT);
		else
			InputHelper.KeyUp(InputHelper.VK_LEFT);

		if (stick.X > deadzone)
			InputHelper.KeyDown(InputHelper.VK_RIGHT);
		else
			InputHelper.KeyUp(InputHelper.VK_RIGHT);
	}

	private static void HandleEdge(ref bool prev, bool current, Action onDown, Action onUp)
	{
		if (current && !prev)
			onDown();
		else if (!current && prev)
			onUp();
		prev = current;
	}
}
