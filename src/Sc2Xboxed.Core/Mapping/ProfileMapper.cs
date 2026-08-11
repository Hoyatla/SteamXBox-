using System;
using System.Text.Json;
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

	private readonly Sc2XboxedProfileSettings _settings = Sc2XboxedProfileSettings.Default;
	private readonly RightTouchpadTrackballMapper _rightTrackball;
	private readonly LeftTouchpadScrollMapper _leftScroll;
	private readonly RightTouchpadTrackballMapper _leftTrackball;
	private readonly LeftTouchpadScrollMapper _rightScroll;
	private readonly SmoothedTouchpadInput _rightPadSmooth = new();
	private readonly double _stickDeadZone;

	private bool _leftPadWasOskMode;
	private bool _firstFrame = true;
	private double _mouseRemainderX;
	private double _mouseRemainderY;
	private int _wheelAccum;
	private int _horizontalWheelAccum;

	public bool CursorMoved { get; private set; }
	public bool Scrolled { get; private set; }
	public bool PadClicked { get; private set; }

	/// <summary>
	/// Whole wheel notches emitted this frame. Drives the scroll detent haptic, which needs a count
	/// rather than a boolean so a fast flick does not feel identical to a single notch.
	/// </summary>
	public int WheelNotches { get; private set; }

	/// <summary>Signed vertical wheel notches produced this frame, for pointer arbitration.</summary>
	public int SignedWheelNotches { get; private set; }

	/// <summary>Signed horizontal wheel notches produced this frame, for pointer arbitration.</summary>
	public int HorizontalWheelNotches { get; private set; }

	/// <summary>Whole pixels actually sent to the OS this frame, for diagnostics.</summary>
	public int EmittedPixelsX { get; private set; }

	/// <summary>Whole pixels actually sent to the OS this frame, for diagnostics.</summary>
	public int EmittedPixelsY { get; private set; }

	public bool OskToggleRequested { get; private set; }
	public bool OskActive { get; set; }

	/// <summary>
	/// True when the overlay is in daisywheel mode, where ABXY select characters instead of running
	/// their desktop bindings. Set by the host when it launches the overlay.
	/// </summary>
	public bool DaisywheelActive { get; set; }

	/// <summary>Resolved settings this mapper is running with, for the haptic layer to consult.</summary>
	public Sc2XboxedProfileSettings Settings => _settings;

	public ProfileMapper() : this(Sc2XboxedProfileSettings.Default) { }

	public ProfileMapper(Sc2XboxedProfileSettings settings)
	{
		_settings = settings;
		_stickDeadZone = settings.StickDeadZone;
		_rightTrackball = new RightTouchpadTrackballMapper(settings.RightPadTrackball);
		_leftScroll = new LeftTouchpadScrollMapper(settings.LeftPadScroll);

		// Its own settings, not the right pad's: sharing them meant the left pad silently inherited
		// the right pad's sensitivity and invert flags.
		_leftTrackball = new RightTouchpadTrackballMapper(settings.LeftPadTrackball);
		_rightScroll = new LeftTouchpadScrollMapper(settings.LeftPadScroll);
	}

	public static Sc2XboxedProfileSettings LoadFromProfilesDirectory(string profileName)
	{
		return LoadDetailed(profileName).Settings;
	}

	/// <summary>
	/// Resolves a profile and records where every value came from, so a diagnostic dump can show a
	/// key that was absent and silently defaulted.
	/// </summary>
	public static ProfileLoadResult LoadDetailed(string profileName)
	{
		var profilesDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"SteamXBox", "profiles");
		var filePath = Path.Combine(profilesDir, $"{profileName}.json");
		var defaults = Sc2XboxedProfileSettings.Default;

		if (!File.Exists(filePath))
		{
			return new ProfileLoadResult(defaults, filePath, FileFound: false, Error: null, Values: []);
		}

		var origins = new List<ProfileValueOrigin>();

		try
		{
			var json = File.ReadAllText(filePath);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			double sens = ReadDouble(root, "rightPadSensitivity", 900.0, origins);
			bool invertY = ReadBool(root, "rightPadInvertY", true, origins);
			bool invertX = ReadBool(root, "rightPadInvertX", false, origins);
			double deadzone = ReadDouble(root, "stickDeadZone", 0.5, origins);
			double gamepadDeadzone = ReadDouble(root, "xboxStickDeadZone", 0.08, origins);
			bool hasTrackpads = ReadHasTrackpads(root, origins);
			bool oskFloating = ReadBool(root, "oskFloating", defaults.OskFloating, origins);
			double stickPointerSpeed = ReadDouble(root, "stickPointerSpeed", defaults.StickPointerSpeed, origins);
			double stickPointerCurve = ReadDouble(root, "stickPointerCurve", defaults.StickPointerCurve, origins);
			bool leftInvert = ReadBool(root, "leftPadInvertVertical", true, origins);
			double leftSens = ReadDouble(root, "leftPadSensitivity", defaults.LeftPadScroll.WheelDeltaPerPadUnit, origins);

			// A full pad swipe spans 2.0 units, so this caps a gesture at ~120 notches. The GUI used to
			// default this field to 600, which produced over 1200 notches per second and made scrolling
			// behave like an on/off switch. Clamped rather than obeyed, and recorded so it is visible.
			const double MaxSaneWheelDeltaPerPadUnit = 60.0;
			if (leftSens > MaxSaneWheelDeltaPerPadUnit)
			{
				origins.Add(new ProfileValueOrigin(
					"leftPadSensitivity(clamped)",
					$"{leftSens:0.##} -> {MaxSaneWheelDeltaPerPadUnit:0.##} (unusable above this)",
					FromFile: false));
				leftSens = MaxSaneWheelDeltaPerPadUnit;
			}
			double rightPadDeadZone = ReadDouble(root, "rightPadDeadZone", defaults.RightPadTrackball.MotionDeadZone, origins);
			double leftPadDeadZone = ReadDouble(root, "leftPadDeadZone", defaults.LeftPadScroll.MotionDeadZone, origins);

			// The "motions" section was written by the editor and read by nobody, so all three
			// dropdowns were decorative and the behaviour was hardcoded.
			var rightPadMode = ReadPadMode(root, "RightPad", defaults.RightPadMode, origins);
			var leftPadMode = ReadPadMode(root, "LeftPad", defaults.LeftPadMode, origins);
			var leftStickMode = ReadStickMode(root, "LeftStick", defaults.LeftStickMode, origins);
			var rightStickMode = ReadStickMode(root, "RightStick", defaults.RightStickMode, origins);

			double rightAccel = ReadDouble(root, "rightPadAcceleration", defaults.RightPadTrackball.AccelerationExponent, origins);
			double leftAccel = ReadDouble(root, "leftPadAcceleration", defaults.LeftPadScroll.AccelerationExponent, origins);
			double edgeSpeed = ReadDouble(root, "rightPadEdgeSpeed", defaults.RightPadTrackball.EdgeSpeedPixelsPerSecond, origins);
			double finePrecision = ReadDouble(root, "finePrecision", defaults.RightPadTrackball.MinAccelerationGain, origins);
			double minThrowTravel = ReadDouble(root, "minThrowTravel", defaults.RightPadTrackball.MinThrowTravelPixels, origins);
			double finePrecisionTravel = ReadDouble(root, "finePrecisionTravel", defaults.RightPadTrackball.FinePrecisionTravel, origins);
			double touchActivation = ReadDouble(root, "touchActivation", defaults.RightPadTrackball.TouchActivationTravel, origins);
			double rightInertia = ReadDouble(root, "rightPadInertia", defaults.RightPadTrackball.InertiaDecayPerSecond, origins);
			double leftInertia = ReadDouble(root, "leftPadInertia", defaults.LeftPadScroll.InertiaDecayPerSecond, origins);
			bool horizontalScroll = ReadBool(root, "leftPadHorizontalScroll", defaults.LeftPadScroll.HorizontalEnabled, origins);

			double leftHapticForce = ReadDouble(root, "leftPadHapticForce", defaults.LeftPadHaptics.Force, origins);
			double leftHapticFreq = ReadDouble(root, "leftPadHapticFrequency", defaults.LeftPadHaptics.Frequency, origins);
			double rightHapticForce = ReadDouble(root, "rightPadHapticForce", defaults.RightPadHaptics.Force, origins);
			double rightHapticFreq = ReadDouble(root, "rightPadHapticFrequency", defaults.RightPadHaptics.Frequency, origins);

			// Xbox360-mode layout, merged into the profile so one file carries the whole pad. A
			// profile written before the merge has no section: fall back to the stored default Xbox
			// profile so the tuning the user made in the Xbox tab survives the move.
			var xboxButtons = ReadXboxButtons(root, origins);
			var xboxTuning = ReadXboxTuning(root, origins);

			var settings = defaults with
			{
				StickDeadZone = deadzone,
				GamepadStickDeadZone = gamepadDeadzone,
				HasTrackpads = hasTrackpads,
				OskFloating = oskFloating,
				StickPointerSpeed = stickPointerSpeed,
				StickPointerCurve = stickPointerCurve,
				XboxButtons = xboxButtons,
				XboxTuning = xboxTuning,
				RightPadMode = rightPadMode,
				LeftPadMode = leftPadMode,
				LeftStickMode = leftStickMode,
				RightStickMode = rightStickMode,
				LeftPadHaptics = new PadHapticSettings { Force = leftHapticForce, Frequency = leftHapticFreq },
				RightPadHaptics = new PadHapticSettings { Force = rightHapticForce, Frequency = rightHapticFreq },
				RightPadTrackball = defaults.RightPadTrackball with
				{
					PixelsPerPadUnit = sens,
					MotionDeadZone = rightPadDeadZone,
					InvertY = invertY,
					InvertX = invertX,
					AccelerationExponent = rightAccel,
					EdgeSpeedPixelsPerSecond = edgeSpeed,
					InertiaDecayPerSecond = rightInertia,
				},
				LeftPadTrackball = defaults.LeftPadTrackball with
				{
					PixelsPerPadUnit = leftSens * 20.0, // scroll units are far smaller than pixels
					MotionDeadZone = leftPadDeadZone,
					InvertY = leftInvert,
					AccelerationExponent = rightAccel,
					MinAccelerationGain = finePrecision,
					FinePrecisionTravel = finePrecisionTravel,
					MinThrowTravelPixels = minThrowTravel,
					TouchActivationTravel = touchActivation,
					InertiaDecayPerSecond = rightInertia,
				},
				// Built from the profile defaults, not LeftTouchpadScrollSettings.Default: the latter
				// carries a 600 wheel-units-per-pad-unit value that is 60x the tuned default, so
				// loading any profile used to make scrolling wildly fast.
				LeftPadScroll = defaults.LeftPadScroll with
				{
					WheelDeltaPerPadUnit = leftSens,
					MotionDeadZone = leftPadDeadZone,
					InvertVertical = leftInvert,
					AccelerationExponent = leftAccel,
					HorizontalEnabled = horizontalScroll,
					InertiaDecayPerSecond = leftInertia,
				},
			};

			return new ProfileLoadResult(settings, filePath, FileFound: true, Error: null, Values: origins);
		}
		catch (Exception exception)
		{
			return new ProfileLoadResult(
				defaults,
				filePath,
				FileFound: true,
				Error: $"{exception.GetType().Name}: {exception.Message}",
				Values: origins);
		}
	}

	/// <summary>
	/// Reads one entry of the profile's "motions" object. The editor writes display strings, so the
	/// mapping is explicit rather than an Enum.Parse on user-facing text.
	/// </summary>
	private static PadMotionMode ReadPadMode(JsonElement root, string key, PadMotionMode fallback, List<ProfileValueOrigin> origins)
	{
		var raw = ReadMotionString(root, key);
		var parsed = raw?.Trim().ToLowerInvariant() switch
		{
			"trackball" => PadMotionMode.Trackball,
			"scroll" => PadMotionMode.Scroll,
			"none" or "aucun" => PadMotionMode.None,
			_ => (PadMotionMode?)null,
		};

		origins.Add(new ProfileValueOrigin($"motions.{key}", (parsed ?? fallback).ToString(), parsed is not null));
		return parsed ?? fallback;
	}

	private static StickMotionMode ReadStickMode(JsonElement root, string key, StickMotionMode fallback, List<ProfileValueOrigin> origins)
	{
		var raw = ReadMotionString(root, key);
		var parsed = raw?.Trim().ToLowerInvariant() switch
		{
			"arrowkeys" => StickMotionMode.ArrowKeys,
			"pointer" or "souris" or "mouse" => StickMotionMode.Pointer,
			"none" or "aucun" => StickMotionMode.None,
			_ => (StickMotionMode?)null,
		};

		origins.Add(new ProfileValueOrigin($"motions.{key}", (parsed ?? fallback).ToString(), parsed is not null));
		return parsed ?? fallback;
	}

	private static string? ReadMotionString(JsonElement root, string key)
	{
		if (root.TryGetProperty("motions", out var motions) &&
			motions.ValueKind == JsonValueKind.Object &&
			motions.TryGetProperty(key, out var value) &&
			value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}

		return null;
	}

	/// <summary>
	/// Reads the merged "xboxButtons" section. Absent on profiles written before the merge: the old
	/// default Xbox profile is loaded instead, so nothing the user had tuned is silently lost.
	/// </summary>
	private static Dictionary<string, string> ReadXboxButtons(JsonElement root, List<ProfileValueOrigin> origins)
	{
		if (root.TryGetProperty("xboxButtons", out var section) && section.ValueKind == JsonValueKind.Object)
		{
			var stored = new Dictionary<string, string>();
			foreach (var property in section.EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.String)
				{
					stored[property.Name] = property.Value.GetString()!;
				}
			}

			origins.Add(new ProfileValueOrigin("xboxButtons", $"{stored.Count} binding(s)", FromFile: true));
			return stored;
		}

		origins.Add(new ProfileValueOrigin("xboxButtons", "legacy default", FromFile: false));
		return XboxProfile.Load(XboxProfile.DefaultName).Buttons;
	}

	/// <summary>
	/// Reads the Xbox tuning keys. The merged "xboxButtons" section is the marker of a post-merge
	/// profile; without it the stored default Xbox profile's tuning is inherited, like the buttons.
	/// </summary>
	private static XboxTuning ReadXboxTuning(JsonElement root, List<ProfileValueOrigin> origins)
	{
		if (!root.TryGetProperty("xboxButtons", out var section) || section.ValueKind != JsonValueKind.Object)
		{
			origins.Add(new ProfileValueOrigin("xboxTuning", "legacy default", FromFile: false));
			return XboxProfile.Load(XboxProfile.DefaultName).Tuning;
		}

		return new XboxTuning
		{
			StickDeadZone = ReadDouble(root, "xboxStickDeadZone", 0.018, origins),
			StickCurve = ReadDouble(root, "xboxStickCurve", 1.0, origins),
			StickSensitivity = ReadDouble(root, "xboxStickSensitivity", 1.0, origins),
			TriggerThreshold = ReadDouble(root, "xboxTriggerThreshold", 0.0, origins),
			TriggerFullPoint = ReadDouble(root, "xboxTriggerFullPoint", 1.0, origins),
			VibrationEnabled = ReadBool(root, "xboxVibrationEnabled", true, origins),
			VibrationIntensity = ReadDouble(root, "xboxVibrationIntensity", 1.0, origins),
			HapticForwarding = ReadBool(root, "xboxHapticForwarding", false, origins),
			TriggerHapticsEnabled = ReadBool(root, "xboxTriggerHapticsEnabled", false, origins),
			TriggerHapticStrength = ReadDouble(root, "xboxTriggerHapticStrength", 0.6, origins),
			TriggerActuatorIndex = ReadInt(root, "xboxTriggerActuatorIndex", 2, origins),
		};
	}

	private static int ReadInt(JsonElement root, string key, int fallback, List<ProfileValueOrigin> origins)
	{
		if (root.TryGetProperty(key, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetInt32(out var value))
		{
			origins.Add(new ProfileValueOrigin(key, value.ToString(), FromFile: true));
			return value;
		}

		origins.Add(new ProfileValueOrigin(key, fallback.ToString(), FromFile: false));
		return fallback;
	}

	private static double ReadDouble(JsonElement root, string key, double fallback, List<ProfileValueOrigin> origins)
	{
		if (root.TryGetProperty(key, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetDouble(out var value))
		{
			origins.Add(new ProfileValueOrigin(key, value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), FromFile: true));
			return value;
		}

		origins.Add(new ProfileValueOrigin(key, fallback.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), FromFile: false));
		return fallback;
	}

	private static bool ReadBool(JsonElement root, string key, bool fallback, List<ProfileValueOrigin> origins)
	{
		if (root.TryGetProperty(key, out var element) &&
			(element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
		{
			var value = element.GetBoolean();
			origins.Add(new ProfileValueOrigin(key, value.ToString(), FromFile: true));
			return value;
		}

		origins.Add(new ProfileValueOrigin(key, fallback.ToString(), FromFile: false));
		return fallback;
	}

	/// <summary>
	/// Whether the profile's family has trackpads at all.
	/// </summary>
	/// <remarks>
	/// True when the family is unknown, so a profile written before this key existed keeps behaving
	/// as it did. A DualSense's touchpad is not read by this project and both stick families report
	/// their pads permanently released, so for them the whole pad path is work that can never
	/// produce anything.
	/// </remarks>
	private static bool ReadHasTrackpads(JsonElement root, List<ProfileValueOrigin> origins)
	{
		var family = root.TryGetProperty("family", out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString() ?? ""
			: "";

		var has = !family.Equals("DualSense", StringComparison.OrdinalIgnoreCase)
			&& !family.Equals("XInput", StringComparison.OrdinalIgnoreCase);

		origins.Add(new ProfileValueOrigin("family", family.Length == 0 ? "(absent)" : family, family.Length > 0));
		origins.Add(new ProfileValueOrigin("hasTrackpads", has.ToString(), FromFile: false));

		return has;
	}

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
		_mouseRemainderX = 0.0;
		_mouseRemainderY = 0.0;
		_wheelAccum = 0;
		_horizontalWheelAccum = 0;
	}

	public void Map(ControllerState state)
	{
		state = state.Normalize();

		CursorMoved = false;
		Scrolled = false;
		PadClicked = false;
		WheelNotches = 0;
		SignedWheelNotches = 0;
		HorizontalWheelNotches = 0;
		EmittedPixelsX = 0;
		EmittedPixelsY = 0;
		OskToggleRequested = false;
		_wheelAccum = 0;
		_horizontalWheelAccum = 0;

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

		// While the overlay is open the controller is an OSK, not a profile: the triggers and the
		// sticks belong to the keyboard (a trigger commits a key, a stick aims at one), and a
		// shortcut firing under the overlay is a ghost key in the middle of a word. The edges are
		// still tracked so nothing fires on release once the overlay closes.
		bool live = !OskActive;

		bool rightTriggerDown = state.RightTrigger > 0.5;
		bool leftTriggerDown = state.LeftTrigger > 0.5;

		HandleEdge(ref _prevRightTriggerDown, rightTriggerDown, live, () => InputHelper.MouseLeftDown(), () => InputHelper.MouseLeftUp());
		HandleEdge(ref _prevLeftTriggerDown, leftTriggerDown, live, () => InputHelper.MouseRightDown(), () => InputHelper.MouseRightUp());

		// A controller with no trackpads never enters the pad path at all. A DualSense and an Xbox
		// pad report both pads permanently released, so the smoothing, the trackball, its inertia
		// and the click edges all ran every frame on input that can never change — and the profile
		// carried sensitivity, dead zone and inertia settings describing a surface that is not
		// there. Two families were being tuned as though they were a third.
		if (!OskActive && _settings.HasTrackpads)
		{
			var rightSmooth = _rightPadSmooth.Update(state.RightPad);
			var rightFrame = MapPad(_settings.RightPadMode, state.Timestamp, rightSmooth, _rightTrackball, _rightScroll);
			ApplyMouseFrame(rightFrame);
			CursorMoved = rightSmooth.IsTouched && rightFrame.HasMouseMotion && (Math.Abs(rightFrame.DeltaX) > 2.0 || Math.Abs(rightFrame.DeltaY) > 2.0);
			// Button down on press and up on release, so holding drags: resizing a window or selecting
			// text needs the button to stay down while the finger moves. Emitting a complete click on
			// release instead made both impossible.
			HandleEdge(ref _prevRightPadClick, rightSmooth.IsPressed, true,
				() => { InputHelper.MouseLeftDown(); PadClicked = true; },
				() => InputHelper.MouseLeftUp());

			var leftFrame = MapPad(_settings.LeftPadMode, state.Timestamp, state.LeftPad, _leftTrackball, _leftScroll);
			ApplyMouseFrame(leftFrame);
			Scrolled = leftFrame.HasWheel;
			HandleEdge(ref _prevLeftPadClick, state.LeftPad.IsPressed, true, () => InputHelper.MouseMiddleDown(), () => InputHelper.MouseMiddleUp());

			// Wheel and motion are accumulated, not sent: the host routes them through the shared
			// pointer arbiter so several controllers can push the one desktop pointer. The counts
			// still describe what this controller produced, for haptics and the per-second line.
			SignedWheelNotches = _wheelAccum;
			HorizontalWheelNotches = _horizontalWheelAccum;
			WheelNotches = Math.Abs(_wheelAccum) + Math.Abs(_horizontalWheelAccum);
		}
		else
		{
			HandleEdge(ref _prevRightPadClick, state.RightPad.IsPressed, true, () => { }, () => { });
			HandleEdge(ref _prevLeftPadClick, state.LeftPad.IsPressed, true, () => { }, () => { });
			_leftPadWasOskMode = InputHelper.IsOskRunning();
		}

		// The overlay is one input or the other: while it is open every button except the ones that
		// close it (B, A, Menu in the daisywheel) is suspended. A shortcut fired under the overlay
		// would be typed into whatever the user is writing — Alt-Tab while aiming at a letter, a
		// volume notch while committing one. Edges are still tracked, so nothing fires on release.
		HandleEdge(ref _prevLB, state.Buttons.HasFlag(SteamControllerButtons.LeftBumper), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_TAB }),
			() => { });
		HandleEdge(ref _prevRB, state.Buttons.HasFlag(SteamControllerButtons.RightBumper), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, InputHelper.VK_TAB }),
			() => { });

		HandleEdge(ref _prevDPadUp, state.Buttons.HasFlag(SteamControllerButtons.DPadUp), live,
			() => InputHelper.KeyDown(0xAF), () => InputHelper.KeyUp(0xAF));
		HandleEdge(ref _prevDPadDown, state.Buttons.HasFlag(SteamControllerButtons.DPadDown), live,
			() => InputHelper.KeyDown(0xAE), () => InputHelper.KeyUp(0xAE));
		HandleEdge(ref _prevDPadLeft, state.Buttons.HasFlag(SteamControllerButtons.DPadLeft), live,
			() => InputHelper.KeyTap(0xB1), () => { });
		HandleEdge(ref _prevDPadRight, state.Buttons.HasFlag(SteamControllerButtons.DPadRight), live,
			() => InputHelper.KeyTap(0xB0), () => { });

		HandleEdge(ref _prevL4, state.Buttons.HasFlag(SteamControllerButtons.L4), live,
			() => InputHelper.KeyTap(InputHelper.VK_SNAPSHOT), () => { });
		HandleEdge(ref _prevR4, state.Buttons.HasFlag(SteamControllerButtons.R4), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, 0x47 }),
			() => { });

		HandleEdge(ref _prevL5, state.Buttons.HasFlag(SteamControllerButtons.L5), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, InputHelper.VK_MENU, 0x52 }),
			() => { });
		HandleEdge(ref _prevR5, state.Buttons.HasFlag(SteamControllerButtons.R5), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_F4 }),
			() => { });

		// While the daisywheel is up, ABXY pick characters in the overlay and must not fire their
		// desktop bindings. Edges are still tracked so a button held across the transition does not
		// trigger on release.
		bool daisywheelTyping = OskActive && DaisywheelActive;

		HandleEdge(ref _prevX, state.Buttons.HasFlag(SteamControllerButtons.X), live,
			() =>
			{
				if (!daisywheelTyping)
					InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_LEFT });
			}, () => { });
		HandleEdge(ref _prevY, state.Buttons.HasFlag(SteamControllerButtons.Y), live,
			() =>
			{
				if (!daisywheelTyping)
					InputHelper.KeyCombination(new ushort[] { InputHelper.VK_MENU, InputHelper.VK_RIGHT });
			}, () => { });

		// A opens the search launcher, and does nothing else. It used to also close the overlay,
		// which made it two buttons in one: the same press meant "find something" or "put the
		// keyboard away" depending on a state the user cannot see. B closes the overlay, and that is
		// enough — one button, one job.
		//
		// Suspended while the overlay is up, like every other desktop binding: a launcher opening
		// under the keyboard would take the keystrokes meant for whatever is being typed.
		HandleEdge(ref _prevA, state.Buttons.HasFlag(SteamControllerButtons.A), live,
			() =>
			{
				if (!daisywheelTyping)
				{
					Runtime.DesktopSignal.Raise(Runtime.DesktopSignal.Search);
				}
			}, () => { });
		HandleEdge(ref _prevB, state.Buttons.HasFlag(SteamControllerButtons.B), true,
			() =>
			{
				if (daisywheelTyping)
				{
					return;
				}

				OskToggleRequested = true;
				System.Diagnostics.Debug.WriteLine($"[ProfileMapper] B pressed → OskToggleRequested=true, OskActive={OskActive}");
			}, () => { });

		HandleEdge(ref _prevMenu, state.Buttons.HasFlag(SteamControllerButtons.Menu), live || daisywheelTyping,
			() =>
			{
				// Menu is the way out of the daisywheel, since B is a character there.
				if (daisywheelTyping)
					OskToggleRequested = true;
				else
					// Clears the screen, and puts the windows back on the next press. This used to
					// tap the Windows key; the environment's own launcher is on A now, which is the
					// same job done by something built for a controller.
					Runtime.DesktopSignal.Raise(Runtime.DesktopSignal.ClearScreen);
			}, () => { });
		HandleEdge(ref _prevView, state.Buttons.HasFlag(SteamControllerButtons.View), live,
			() => InputHelper.KeyCombination(new ushort[] { InputHelper.VK_LWIN, 0x44 }),
			() => { });

		HandleEdge(ref _prevL3, state.Buttons.HasFlag(SteamControllerButtons.LeftStick), live,
			() => InputHelper.KeyTap(0x0D), () => { });
		HandleEdge(ref _prevR3, state.Buttons.HasFlag(SteamControllerButtons.RightStick), live,
			() => { }, () => { });

		if (live && _settings.LeftStickMode == StickMotionMode.ArrowKeys)
		{
			MapLeftStickArrows(state.LeftStick);
		}
	}

	/// <summary>Routes a pad to whatever its profile says it drives.</summary>
	/// <remarks>
	/// This replaces a hardcoded assignment plus a mode switch keyed on
	/// <c>InputHelper.IsOskRunning()</c>, which matched a process named "osk" — Windows' own
	/// on-screen keyboard, never this project's overlay. The left pad therefore kept scrolling while
	/// the overlay was open, and flipped to trackball if the user happened to open the Windows one.
	/// </remarks>
	private static MouseOutputFrame MapPad(
		PadMotionMode mode,
		TimeSpan timestamp,
		TouchpadSample pad,
		RightTouchpadTrackballMapper trackball,
		LeftTouchpadScrollMapper scroll)
	{
		switch (mode)
		{
			case PadMotionMode.Trackball:
				return trackball.Update(timestamp, pad);

			case PadMotionMode.Scroll:
				return scroll.Update(timestamp, pad);

			default:
				// Keep both mappers fed so a mid-session mode change does not start from a stale
				// position and jump the cursor.
				trackball.Reset();
				scroll.Reset();
				return MouseOutputFrame.Empty;
		}
	}

	private static SteamControllerButtons PrecisionButtonFlag(PrecisionButton button) => button switch
	{
		PrecisionButton.L4 => SteamControllerButtons.L4,
		PrecisionButton.R4 => SteamControllerButtons.R4,
		PrecisionButton.L5 => SteamControllerButtons.L5,
		PrecisionButton.R5 => SteamControllerButtons.R5,
		PrecisionButton.LeftBumper => SteamControllerButtons.LeftBumper,
		PrecisionButton.RightBumper => SteamControllerButtons.RightBumper,
		_ => SteamControllerButtons.None,
	};

	/// <summary>
	/// Accumulates a mouse frame into the frame's outputs, carrying the sub-pixel remainder across
	/// frames. The host sends the pointer, through the shared arbiter, so nothing is written to the
	/// OS here.
	/// </summary>
	/// <remarks>
	/// SendInput only takes whole pixels. Truncating each frame independently discarded the
	/// fraction every time, so at HID report rate a slow, deliberate drag produced deltas below one
	/// pixel and the cursor did not move at all — fine pointing was impossible, and faster movement
	/// was biased short. Keeping the remainder makes the emitted motion match the finger.
	/// </remarks>
	private void ApplyMouseFrame(MouseOutputFrame frame)
	{
		if (frame.HasMouseMotion)
		{
			_mouseRemainderX += frame.DeltaX;
			_mouseRemainderY += frame.DeltaY;

			// Truncation toward zero keeps the remainder's sign, so this is direction-neutral.
			var stepX = (int)_mouseRemainderX;
			var stepY = (int)_mouseRemainderY;
			_mouseRemainderX -= stepX;
			_mouseRemainderY -= stepY;

			if (stepX != 0 || stepY != 0)
			{
				EmittedPixelsX += stepX;
				EmittedPixelsY += stepY;
			}
		}

		if (frame.HasWheel)
			_wheelAccum += frame.WheelDelta;

		if (frame.HasHorizontalWheel)
			_horizontalWheelAccum += frame.HorizontalWheelDelta;
	}

	private void MapLeftStickArrows(NormalizedStick stick)
	{
		if (stick.Y > _stickDeadZone)
			InputHelper.KeyDown(InputHelper.VK_UP);
		else
			InputHelper.KeyUp(InputHelper.VK_UP);

		if (stick.Y < -_stickDeadZone)
			InputHelper.KeyDown(InputHelper.VK_DOWN);
		else
			InputHelper.KeyUp(InputHelper.VK_DOWN);

		if (stick.X < -_stickDeadZone)
			InputHelper.KeyDown(InputHelper.VK_LEFT);
		else
			InputHelper.KeyUp(InputHelper.VK_LEFT);

		if (stick.X > _stickDeadZone)
			InputHelper.KeyDown(InputHelper.VK_RIGHT);
		else
			InputHelper.KeyUp(InputHelper.VK_RIGHT);
	}

	private static void HandleEdge(ref bool prev, bool current, bool enabled, Action onDown, Action onUp)
	{
		if (enabled && current && !prev)
			onDown();
		else if (enabled && !current && prev)
			onUp();

		// Tracked regardless of enabled: the flags must follow the physical state or a button
		// released under the overlay would fire its shortcut the moment the overlay closes.
		prev = current;
	}
}
