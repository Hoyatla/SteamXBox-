using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Osk;
using SteamXBox.Gui.Services;

using SteamXBox.Shell.Localization;

namespace SteamXBox.Gui.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private bool _syncingSelection;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private ProfileData? _activeEdit;
    [ObservableProperty] private bool _isDefaultEdit;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private ProfileData? _selectedProfileItem;

    /// <summary>Whether the controller selected at the top can receive a profile.</summary>
    [ObservableProperty] private bool _hasTarget;

    /// <summary>The controller the current edit will be saved under.</summary>
    [ObservableProperty] private string _targetName = "";

    /// <summary>What saving would do to the selected controller's profile.</summary>
    [ObservableProperty] private string _targetContext = "";

    partial void OnSelectedProfileItemChanged(ProfileData? value)
    {
        if (_syncingSelection || value is null) return;
        StartEditing(value);
    }

    public ObservableCollection<ProfileData> Profiles => _service.Profiles;

    /// <summary>The profiles of the tab's family: Default always, then the family's own.</summary>
    /// <remarks>
    /// Built from the shared collection, never a second source of truth. The family comes from the
    /// tab the user opened — Steam, PS5 or Xbox — and each profile belongs to the family it was
    /// written for, so a controller only ever sees the settings that make sense on it.
    /// </remarks>
    public ObservableCollection<ProfileData> VisibleProfiles { get; } = [];

    private static string FamilyOf(ControllerKind kind) => kind switch
    {
        ControllerKind.SteamController => "Steam",
        ControllerKind.DualSense => "DualSense",
        ControllerKind.XInput => "XInput",
        _ => "",
    };

    private void RebuildVisibleProfiles()
    {
        var family = Controllers.Family is { } kind ? FamilyOf(kind) : null;

        VisibleProfiles.Clear();
        foreach (var profile in _service.Profiles)
        {
            if (family is null || profile.Name == "Default" || profile.Family == family)
                VisibleProfiles.Add(profile);
        }

        if (SelectedProfileItem is { } selected && !VisibleProfiles.Contains(selected))
            SelectedProfileItem = null;
    }

    private void RefreshTarget()
    {
        var selected = Controllers.Selected;
        HasTarget = selected is not null;
        if (selected is null)
        {
            TargetName = "";
            TargetContext = Strings.Current["Sélectionnez une manette en haut pour l'associer à ce profil."];
            return;
        }

        TargetName = selected.DisplayName;
        var hasProfile = _service.Profiles.Any(p => p.Name == selected.DisplayName && p.Name != "Default");
        TargetContext = hasProfile
            ? Strings.Current.Format("Profil « {0} » — il sera écrasé à la sauvegarde.", selected.DisplayName)
            : Strings.Current["Aucun profil — il sera créé à la sauvegarde."];
    }

    /// <summary>The connected controllers, shared with the other tab.</summary>
    public ControllerStripViewModel Controllers => ControllerStripViewModel.Shared;

    /// <summary>
    /// Whether the tab is editing a pad with sticks but no trackpads (DualSense, XInput). Those
    /// families only get the stick rows of the Mouvements card; everything pad-specific is hidden.
    /// </summary>
    public bool IsStickFamily =>
        Controllers.Family is ControllerKind.DualSense or ControllerKind.XInput;

    /// <summary>The complement: Steam Controller (or no family selected), where the full card shows.</summary>
    public bool IsPadFamily => !IsStickFamily;

    // ---- Wrapper properties for dictionary bindings ----
    // Buttons
    public string ButtonL4     { get => GetButton("L4");     set => SetButton("L4", value); }
    public string ButtonR4     { get => GetButton("R4");     set => SetButton("R4", value); }
    public string ButtonL5     { get => GetButton("L5");     set => SetButton("L5", value); }
    public string ButtonR5     { get => GetButton("R5");     set => SetButton("R5", value); }
    public string ButtonLBumper { get => GetButton("LBumper"); set => SetButton("LBumper", value); }
    public string ButtonRBumper { get => GetButton("RBumper"); set => SetButton("RBumper", value); }
    public string ButtonA      { get => GetButton("A");      set => SetButton("A", value); }
    public string ButtonB      { get => GetButton("B");      set => SetButton("B", value); }
    public string ButtonX      { get => GetButton("X");      set => SetButton("X", value); }
    public string ButtonY      { get => GetButton("Y");      set => SetButton("Y", value); }
    public string ButtonL3     { get => GetButton("L3");     set => SetButton("L3", value); }
    public string ButtonR3     { get => GetButton("R3");     set => SetButton("R3", value); }
    public string ButtonMenu   { get => GetButton("Menu");   set => SetButton("Menu", value); }
    public string ButtonView   { get => GetButton("View");   set => SetButton("View", value); }
    public string ButtonDPadUp   { get => GetButton("DPadUp");   set => SetButton("DPadUp", value); }
    public string ButtonDPadDown { get => GetButton("DPadDown"); set => SetButton("DPadDown", value); }
    public string ButtonDPadLeft { get => GetButton("DPadLeft"); set => SetButton("DPadLeft", value); }
    public string ButtonDPadRight { get => GetButton("DPadRight"); set => SetButton("DPadRight", value); }
    // Motions
    public string MotionRightPad  { get => GetMotion("RightPad");  set => SetMotion("RightPad", value); }
    public string MotionLeftPad   { get => GetMotion("LeftPad");   set => SetMotion("LeftPad", value); }
    public string MotionLeftStick { get => GetMotion("LeftStick"); set => SetMotion("LeftStick", value); }
    public string MotionRightStick { get => GetMotion("RightStick"); set => SetMotion("RightStick", value); }

    private string GetButton(string key) =>
        ActiveEdit is null ? "" : ActiveEdit.Buttons.GetValueOrDefault(key) ?? DefaultButtons.GetValueOrDefault(key) ?? "";

    /// <summary>
    /// The factory shortcut assignments, so a profile written without them (or with keys dropped by
    /// an older build) still reads its default shortcuts instead of empty combos.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DefaultButtons = new ProfileData().Buttons;
    private void SetButton(string key, string value)
    {
        if (ActiveEdit == null) return;
        ActiveEdit.Buttons[key] = value;
        OnPropertyChanged(ButtonPropName(key));
    }
    private string GetMotion(string key) =>
        ActiveEdit?.Motions.GetValueOrDefault(key) ?? DefaultMotions.GetValueOrDefault(key) ?? "";

    /// <summary>
    /// The factory motion assignments, so a profile written without a key (like "RightStick" before
    /// the stick had a role to bind) still reads its default instead of an empty combo.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DefaultMotions = new ProfileData().Motions;
    private void SetMotion(string key, string value)
    {
        if (ActiveEdit == null) return;
        ActiveEdit.Motions[key] = value;
        OnPropertyChanged(MotionPropName(key));
    }
    private static string ButtonPropName(string key) => $"Button{key}";
    private static string MotionPropName(string key) => $"Motion{key}";
    private void NotifyAllWrappers()
    {
        var btnKeys = new[] { "L4","R4","L5","R5","LBumper","RBumper","A","B","X","Y","L3","R3","Menu","View","DPadUp","DPadDown","DPadLeft","DPadRight" };
        foreach (var k in btnKeys) OnPropertyChanged(ButtonPropName(k));
        OnPropertyChanged(MotionPropName("RightPad"));
        OnPropertyChanged(MotionPropName("LeftPad"));
        OnPropertyChanged(MotionPropName("LeftStick"));
        OnPropertyChanged(MotionPropName("RightStick"));

        foreach (var name in PercentPropertyNames) OnPropertyChanged(name);
    }

    // ---- Numeric settings as percentages ----
    // The stored values are engineering units with no meaning to a user: 900 pixels per pad unit,
    // 0.002 pad units of dead zone. Each is exposed as 0-100% over a usable range instead, so the
    // editor shows a slider rather than a box expecting a magic number.

    // Movement settings keep absolute ranges with a meaningful zero: 0% dead zone means no dead zone.
    // Centring these on a tuned value made "50%" arbitrary and left no way to actually turn one off.
    private const double RightPadSensMin = 200.0, RightPadSensMax = 2000.0;
    private const double LeftPadSensMin = 1.0, LeftPadSensMax = 20.0;
    private const double StickDeadZoneMin = 0.0, StickDeadZoneMax = 1.0;
    private const double RightPadDeadZoneMin = 0.0, RightPadDeadZoneMax = 0.005;
    private const double LeftPadDeadZoneMin = 0.0, LeftPadDeadZoneMax = 0.005;

    // Behaviour curves, absolute like the movement ranges. These were briefly centred on the tuned
    // values, which served to establish the defaults but made every slider read 50% and left no way
    // to express "off". Each range now starts at a meaningful zero.
    private const double RightAccelMin = 1.0, RightAccelMax = 3.0;   // 0% = linear, i.e. no acceleration
    private const double LeftAccelMin = 1.0, LeftAccelMax = 3.0;
    private const double EdgeSpeedMin = 0.0, EdgeSpeedMax = 1500.0;  // 0% = off
    // Inverted ranges: a higher percentage means more of the effect, but a lower stored number.
    private const double InertiaDecayShort = 12.0, InertiaDecayLong = 0.5; // 0% = almost no glide
    private const double FinePrecisionMin = 1.0, FinePrecisionMax = 0.05;  // 0% = no fine precision
    private const double ThrowTravelMin = 0.0, ThrowTravelMax = 200.0;     // 0% = no threshold

    // Fine-precision reach and brush rejection are deliberately not exposed: they are calibration
    // constants of the pad itself, not preferences, and a wrong value makes the pad feel broken.

    private static readonly string[] PercentPropertyNames =
    [
        nameof(RightPadSensitivityPercent), nameof(RightPadSensitivityDisplay),
        nameof(LeftPadSensitivityPercent), nameof(LeftPadSensitivityDisplay),
        nameof(StickDeadZonePercent), nameof(StickDeadZoneDisplay),
        nameof(XboxStickDeadZonePercent), nameof(XboxStickDeadZoneDisplay),
        nameof(XboxStickCurvePercent), nameof(XboxStickCurveDisplay),
        nameof(XboxStickSensitivityPercent), nameof(XboxStickSensitivityDisplay),
        nameof(RightPadDeadZonePercent), nameof(RightPadDeadZoneDisplay),
        nameof(LeftPadDeadZonePercent), nameof(LeftPadDeadZoneDisplay),
        nameof(RightPadAccelerationPercent), nameof(RightPadAccelerationDisplay),
        nameof(LeftPadAccelerationPercent), nameof(LeftPadAccelerationDisplay),
        nameof(RightPadEdgeSpeedPercent), nameof(RightPadEdgeSpeedDisplay),
        nameof(FinePrecisionPercent), nameof(FinePrecisionDisplay),
        nameof(MinThrowTravelPercent), nameof(MinThrowTravelDisplay),
        nameof(LeftPadHapticForcePercent), nameof(LeftPadHapticForceDisplay),
        nameof(LeftPadHapticFrequencyPercent), nameof(LeftPadHapticFrequencyDisplay),
        nameof(RightPadHapticForcePercent), nameof(RightPadHapticForceDisplay),
        nameof(RightPadHapticFrequencyPercent), nameof(RightPadHapticFrequencyDisplay),
        nameof(RightPadInertiaPercent), nameof(RightPadInertiaDisplay),
        nameof(LeftPadInertiaPercent), nameof(LeftPadInertiaDisplay),
        nameof(LeftPadHorizontalScroll),
    ];

    public bool LeftPadHorizontalScroll
    {
        get => ActiveEdit?.LeftPadHorizontalScroll ?? false;
        set
        {
            if (ActiveEdit is null || ActiveEdit.LeftPadHorizontalScroll == value) return;
            ActiveEdit.LeftPadHorizontalScroll = value;
            OnPropertyChanged();
        }
    }

    public double RightPadAccelerationPercent
    {
        get => GetPercent(p => p.RightPadAcceleration, RightAccelMin, RightAccelMax);
        set => SetPercent((p, v) => p.RightPadAcceleration = Math.Round(v, 3), value, RightAccelMin, RightAccelMax,
            nameof(RightPadAccelerationPercent), nameof(RightPadAccelerationDisplay));
    }
    public string RightPadAccelerationDisplay =>
        RightPadAccelerationPercent < 1 ? "Off" : $"{RightPadAccelerationPercent:0} %";

    public double LeftPadAccelerationPercent
    {
        get => GetPercent(p => p.LeftPadAcceleration, LeftAccelMin, LeftAccelMax);
        set => SetPercent((p, v) => p.LeftPadAcceleration = Math.Round(v, 3), value, LeftAccelMin, LeftAccelMax,
            nameof(LeftPadAccelerationPercent), nameof(LeftPadAccelerationDisplay));
    }
    public string LeftPadAccelerationDisplay =>
        LeftPadAccelerationPercent < 1 ? "Off" : $"{LeftPadAccelerationPercent:0} %";

    public double RightPadEdgeSpeedPercent
    {
        get => GetPercent(p => p.RightPadEdgeSpeed, EdgeSpeedMin, EdgeSpeedMax);
        set => SetPercent((p, v) => p.RightPadEdgeSpeed = Math.Round(v), value, EdgeSpeedMin, EdgeSpeedMax,
            nameof(RightPadEdgeSpeedPercent), nameof(RightPadEdgeSpeedDisplay));
    }
    public string RightPadEdgeSpeedDisplay =>
        RightPadEdgeSpeedPercent < 1 ? "Off" : $"{RightPadEdgeSpeedPercent:0} %";

    // Inverted: more percent means a lower gain floor, i.e. finer control on slow gestures.
    public double FinePrecisionPercent
    {
        get => GetPercent(p => p.FinePrecision, FinePrecisionMin, FinePrecisionMax);
        set => SetPercent((p, v) => p.FinePrecision = Math.Round(v, 3), value, FinePrecisionMin, FinePrecisionMax,
            nameof(FinePrecisionPercent), nameof(FinePrecisionDisplay));
    }
    public string FinePrecisionDisplay => $"{FinePrecisionPercent:0} %";

    public double MinThrowTravelPercent
    {
        get => GetPercent(p => p.MinThrowTravel, ThrowTravelMin, ThrowTravelMax);
        set => SetPercent((p, v) => p.MinThrowTravel = Math.Round(v), value, ThrowTravelMin, ThrowTravelMax,
            nameof(MinThrowTravelPercent), nameof(MinThrowTravelDisplay));
    }
    public string MinThrowTravelDisplay => $"{MinThrowTravelPercent:0} %";

    // Haptics are stored 0-1, so the percentage is a direct mapping.
    public double LeftPadHapticForcePercent
    {
        get => GetPercent(p => p.LeftPadHapticForce, 0.0, 1.0);
        set => SetPercent((p, v) => p.LeftPadHapticForce = Math.Round(v, 3), value, 0.0, 1.0,
            nameof(LeftPadHapticForcePercent), nameof(LeftPadHapticForceDisplay));
    }
    public string LeftPadHapticForceDisplay =>
        LeftPadHapticForcePercent < 1 ? "Off" : $"{LeftPadHapticForcePercent:0} %";

    public double LeftPadHapticFrequencyPercent
    {
        get => GetPercent(p => p.LeftPadHapticFrequency, 0.0, 1.0);
        set => SetPercent((p, v) => p.LeftPadHapticFrequency = Math.Round(v, 3), value, 0.0, 1.0,
            nameof(LeftPadHapticFrequencyPercent), nameof(LeftPadHapticFrequencyDisplay));
    }
    public string LeftPadHapticFrequencyDisplay => $"{LeftPadHapticFrequencyPercent:0} %";

    public double RightPadHapticForcePercent
    {
        get => GetPercent(p => p.RightPadHapticForce, 0.0, 1.0);
        set => SetPercent((p, v) => p.RightPadHapticForce = Math.Round(v, 3), value, 0.0, 1.0,
            nameof(RightPadHapticForcePercent), nameof(RightPadHapticForceDisplay));
    }
    public string RightPadHapticForceDisplay =>
        RightPadHapticForcePercent < 1 ? "Off" : $"{RightPadHapticForcePercent:0} %";

    public double RightPadHapticFrequencyPercent
    {
        get => GetPercent(p => p.RightPadHapticFrequency, 0.0, 1.0);
        set => SetPercent((p, v) => p.RightPadHapticFrequency = Math.Round(v, 3), value, 0.0, 1.0,
            nameof(RightPadHapticFrequencyPercent), nameof(RightPadHapticFrequencyDisplay));
    }
    public string RightPadHapticFrequencyDisplay => $"{RightPadHapticFrequencyPercent:0} %";

    public double RightPadInertiaPercent
    {
        get => GetPercent(p => p.RightPadInertia, InertiaDecayShort, InertiaDecayLong);
        set => SetPercent((p, v) => p.RightPadInertia = Math.Round(v, 2), value, InertiaDecayShort, InertiaDecayLong,
            nameof(RightPadInertiaPercent), nameof(RightPadInertiaDisplay));
    }
    public string RightPadInertiaDisplay => $"{RightPadInertiaPercent:0} %";

    public double LeftPadInertiaPercent
    {
        get => GetPercent(p => p.LeftPadInertia, InertiaDecayShort, InertiaDecayLong);
        set => SetPercent((p, v) => p.LeftPadInertia = Math.Round(v, 2), value, InertiaDecayShort, InertiaDecayLong,
            nameof(LeftPadInertiaPercent), nameof(LeftPadInertiaDisplay));
    }
    public string LeftPadInertiaDisplay => $"{LeftPadInertiaPercent:0} %";

    private static double ToPercent(double value, double min, double max) =>
        Math.Clamp((value - min) / (max - min) * 100.0, 0.0, 100.0);

    private static double FromPercent(double percent, double min, double max) =>
        min + (max - min) * (Math.Clamp(percent, 0.0, 100.0) / 100.0);

    private double GetPercent(Func<ProfileData, double> read, double min, double max) =>
        ActiveEdit is null ? 0.0 : ToPercent(read(ActiveEdit), min, max);

    private void SetPercent(Action<ProfileData, double> write, double percent, double min, double max,
        string percentName, string displayName)
    {
        if (ActiveEdit is null) return;
        write(ActiveEdit, FromPercent(percent, min, max));
        OnPropertyChanged(percentName);
        OnPropertyChanged(displayName);
    }

    public double RightPadSensitivityPercent
    {
        get => GetPercent(p => p.RightPadSensitivity, RightPadSensMin, RightPadSensMax);
        set => SetPercent((p, v) => p.RightPadSensitivity = Math.Round(v), value, RightPadSensMin, RightPadSensMax,
            nameof(RightPadSensitivityPercent), nameof(RightPadSensitivityDisplay));
    }
    public string RightPadSensitivityDisplay => $"{RightPadSensitivityPercent:0} %";

    public double LeftPadSensitivityPercent
    {
        get => GetPercent(p => p.LeftPadSensitivity, LeftPadSensMin, LeftPadSensMax);
        set => SetPercent((p, v) => p.LeftPadSensitivity = Math.Round(v, 1), value, LeftPadSensMin, LeftPadSensMax,
            nameof(LeftPadSensitivityPercent), nameof(LeftPadSensitivityDisplay));
    }
    public string LeftPadSensitivityDisplay => $"{LeftPadSensitivityPercent:0} %";

    public double StickDeadZonePercent
    {
        get => GetPercent(p => p.StickDeadZone, StickDeadZoneMin, StickDeadZoneMax);
        set => SetPercent((p, v) => p.StickDeadZone = Math.Round(v, 3), value, StickDeadZoneMin, StickDeadZoneMax,
            nameof(StickDeadZonePercent), nameof(StickDeadZoneDisplay));
    }
    public string StickDeadZoneDisplay => $"{StickDeadZonePercent:0} %";

    // Stick tuning on the stick-only families (PS5, Xbox). Same values as the Xbox tab, exposed
    // here so the Profils tab shows them where the pads would otherwise be — a stick has no pads,
    // and a pad-only card would be lying about what it is editing.
    private const double XboxStickDeadZoneMin = 0.0, XboxStickDeadZoneMax = 0.5;
    private const double XboxStickCurveMin = 0.2, XboxStickCurveMax = 3.0;
    private const double XboxStickSensitivityMin = 0.25, XboxStickSensitivityMax = 3.0;

    public double XboxStickDeadZonePercent
    {
        get => GetPercent(p => p.XboxStickDeadZone, XboxStickDeadZoneMin, XboxStickDeadZoneMax);
        set => SetPercent((p, v) => p.XboxStickDeadZone = Math.Round(v, 3), value, XboxStickDeadZoneMin, XboxStickDeadZoneMax,
            nameof(XboxStickDeadZonePercent), nameof(XboxStickDeadZoneDisplay));
    }
    public string XboxStickDeadZoneDisplay => $"{XboxStickDeadZonePercent:0} %";

    public double XboxStickCurvePercent
    {
        get => GetPercent(p => p.XboxStickCurve, XboxStickCurveMin, XboxStickCurveMax);
        set => SetPercent((p, v) => p.XboxStickCurve = Math.Round(v, 3), value, XboxStickCurveMin, XboxStickCurveMax,
            nameof(XboxStickCurvePercent), nameof(XboxStickCurveDisplay));
    }
    public string XboxStickCurveDisplay => $"{XboxStickCurvePercent:0} %";

    public double XboxStickSensitivityPercent
    {
        get => GetPercent(p => p.XboxStickSensitivity, XboxStickSensitivityMin, XboxStickSensitivityMax);
        set => SetPercent((p, v) => p.XboxStickSensitivity = Math.Round(v, 3), value, XboxStickSensitivityMin, XboxStickSensitivityMax,
            nameof(XboxStickSensitivityPercent), nameof(XboxStickSensitivityDisplay));
    }
    public string XboxStickSensitivityDisplay => $"{XboxStickSensitivityPercent:0} %";

    public double RightPadDeadZonePercent
    {
        get => GetPercent(p => p.RightPadDeadZone, RightPadDeadZoneMin, RightPadDeadZoneMax);
        set => SetPercent((p, v) => p.RightPadDeadZone = Math.Round(v, 5), value, RightPadDeadZoneMin, RightPadDeadZoneMax,
            nameof(RightPadDeadZonePercent), nameof(RightPadDeadZoneDisplay));
    }
    public string RightPadDeadZoneDisplay => $"{RightPadDeadZonePercent:0} %";

    public double LeftPadDeadZonePercent
    {
        get => GetPercent(p => p.LeftPadDeadZone, LeftPadDeadZoneMin, LeftPadDeadZoneMax);
        set => SetPercent((p, v) => p.LeftPadDeadZone = Math.Round(v, 5), value, LeftPadDeadZoneMin, LeftPadDeadZoneMax,
            nameof(LeftPadDeadZonePercent), nameof(LeftPadDeadZoneDisplay));
    }
    public string LeftPadDeadZoneDisplay => $"{LeftPadDeadZonePercent:0} %";

    partial void OnActiveEditChanged(ProfileData? value)
    {
        IsDefaultEdit = value?.Name == "Default";
        NotifyAllWrappers();
    }

    // ---- Overlay keyboard settings ----
    // Kept in the profile editor, next to the rest of the controller configuration, which is where
    // they belong from the user's point of view even though the file itself is shared.
    private readonly OskSettings _osk = OskSettings.Load();

    public OskTypingModeOption[] OskTypingModes { get; } =
    [
        new("Clavier complet", OskTypingMode.FullKeyboard),
        new("Daisywheel", OskTypingMode.Daisywheel),
    ];

    public OskTypingMode OskMode
    {
        get => _osk.TypingMode;
        set { if (_osk.TypingMode == value) return; _osk.TypingMode = value; OnPropertyChanged(); _osk.Save(); }
    }

    /// <summary>
    /// Size of the floating keyboard, as a percentage of the standard size.
    /// </summary>
    /// <remarks>
    /// Reach and screen size vary too much for one size to suit everyone, so this is a slider rather
    /// than a constant. It takes effect the next time the overlay is shown, since the overlay re-reads
    /// its settings on every show.
    /// </remarks>
    public int OskKeyboardScale
    {
        get => _osk.ClampedKeyboardScale;
        set
        {
            var clamped = Math.Clamp(value, OskSettings.MinKeyboardScale, OskSettings.MaxKeyboardScale);
            if (_osk.KeyboardScale == clamped) return;
            _osk.KeyboardScale = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OskKeyboardScaleDisplay));
            _osk.Save();
        }
    }

    public string OskKeyboardScaleDisplay => $"{OskKeyboardScale} %";

    /// <summary>
    /// Checked: the keyboard floats, following the text field and dodging the pointer.
    /// Unchecked: it is pinned to the bottom of the screen.
    /// </summary>
    public bool OskFloatingKeyboard
    {
        get => _osk.FloatingKeyboard;
        set
        {
            if (_osk.FloatingKeyboard == value) return;
            _osk.FloatingKeyboard = value;
            OnPropertyChanged();
            _osk.Save();
        }
    }

    public bool OskHoverHaptics
    {
        get => _osk.HoverHaptics;
        set { if (_osk.HoverHaptics == value) return; _osk.HoverHaptics = value; OnPropertyChanged(); _osk.Save(); }
    }

    public int OskHapticIntensity
    {
        get => _osk.HapticIntensity;
        set
        {
            if (_osk.HapticIntensity == value) return;
            _osk.HapticIntensity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OskHapticIntensityDisplay));
            _osk.Save();
        }
    }

    public string OskHapticIntensityDisplay =>
        _osk.HapticIntensity == 0 ? Strings.Current["Désactivées"] : $"{_osk.HapticIntensity} %";

    public bool OskValidateOnRelease
    {
        get => _osk.ValidateOnRelease;
        set { if (_osk.ValidateOnRelease == value) return; _osk.ValidateOnRelease = value; OnPropertyChanged(); _osk.Save(); }
    }

    public int OskLeftClickForce
    {
        get => _osk.LeftClickForce;
        set
        {
            if (_osk.LeftClickForce == value) return;
            _osk.LeftClickForce = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OskLeftClickForceDisplay));
            _osk.Save();
        }
    }

    public string OskLeftClickForceDisplay =>
        _osk.LeftClickForce == 0 ? "Off" : $"{_osk.LeftClickForce} %";

    public int OskRightClickForce
    {
        get => _osk.RightClickForce;
        set
        {
            if (_osk.RightClickForce == value) return;
            _osk.RightClickForce = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OskRightClickForceDisplay));
            _osk.Save();
        }
    }

    public string OskRightClickForceDisplay =>
        _osk.RightClickForce == 0 ? "Off" : $"{_osk.RightClickForce} %";

    // ComboBox options
    public string[] AvailableModes { get; } = ["Profile", "Xbox360"];
    public string[] RightPadOptions { get; } = ["Trackball", "Scroll", "None"];
    public string[] LeftPadOptions { get; } = ["Scroll", "Trackball", "None"];
    public string[] LeftStickOptions { get; } = ["ArrowKeys", "None"];
    // The right stick drives the pointer on stick-only families (PS5/Xbox) which have no pads, so
    // only pointer-or-nothing is offered — never a pad role.
    public string[] RightStickOptions { get; } = ["Souris", "Aucun"];
    public string[] RearButtonOptions { get; } = ["PrintScreen", "Win+G", "Win+R", "Alt+F4", "OSK Toggle", "Aucun"];
    public string[] BumperOptions { get; } = ["Alt+Tab", "Win+Tab", "Aucun"];
    public string[] AOptions { get; } = ["OSK Toggle", "Enter", "Aucun"];
    public string[] BOptions { get; } = ["OSK Toggle", "Aucun"];
    public string[] XOptions { get; } = ["Alt+←", "Aucun"];
    public string[] YOptions { get; } = ["Alt+→", "Aucun"];
    public string[] L3Options { get; } = ["Enter", "Aucun"];
    public string[] R3Options { get; } = ["Aucun"];
    public string[] MenuOptions { get; } = ["Win", "Aucun"];
    public string[] ViewOptions { get; } = ["Win+D", "Aucun"];
    public string[] DPadUpOptions { get; } = ["VolumeUp", "Aucun"];
    public string[] DPadDownOptions { get; } = ["VolumeDown", "Aucun"];
    public string[] DPadLeftOptions { get; } = ["Back", "Aucun"];
    public string[] DPadRightOptions { get; } = ["Forward", "Aucun"];

    public ProfileViewModel()
    {
        _service = App.ProfileSvc;

        // The test profile of the old design has no place under the per-controller model.
        var testProfile = _service.Profiles.FirstOrDefault(p =>
            p.Name.Equals("perso", StringComparison.OrdinalIgnoreCase));
        if (testProfile is not null)
            _service.Delete(testProfile);

        _service.Profiles.CollectionChanged += (_, _) => RebuildVisibleProfiles();
        Controllers.FamilyChanged += _ =>
        {
            RebuildVisibleProfiles();
            OnPropertyChanged(nameof(IsStickFamily));
            OnPropertyChanged(nameof(IsPadFamily));
        };
        Controllers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControllerStripViewModel.Selected))
                RefreshTarget();
        };

        RebuildVisibleProfiles();
        RefreshTarget();
    }

    /// <summary>
    /// Loads a profile into the editor. A Default edit becomes a working copy: the reference file is
    /// never written, and saving always produces a profile named after the selected controller.
    /// </summary>
    private void StartEditing(ProfileData profile)
    {
        // A profile written by an older build may be missing shortcut keys (X, Y, R3 and friends
        // were dropped). Backfill them from the factory defaults so the combos and the saved file
        // carry the full set again, without touching assignments the user actually set.
        foreach (var (key, value) in DefaultButtons)
        {
            if (!profile.Buttons.ContainsKey(key))
            {
                profile.Buttons[key] = value;
            }
        }

        ActiveEdit = profile.Name == "Default" ? profile.Clone() : profile;
        IsEditing = true;
        App.MainVm.SelectedProfile = profile;

        _syncingSelection = true;
        SelectedProfileItem = profile.Name == "Default"
            ? _service.Profiles.FirstOrDefault(p => p.Name == "Default")
            : profile;
        _syncingSelection = false;
    }

    /// <summary>
    /// Writes the current edit as the selected controller's profile and files that controller under
    /// it. Returns the saved profile, or null when there is no controller to receive it.
    /// </summary>
    private ProfileData? SaveCurrentEditToSelectedController()
    {
        if (ActiveEdit is null) return null;

        var selected = Controllers.Selected;
        if (selected is null)
        {
            StatusMessage = Strings.Current["Sélectionnez une manette pour enregistrer ce profil."];
            return null;
        }

        // A controller renamed "Default" must not be able to overwrite the reference profile.
        if (selected.DisplayName.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = Strings.Current["Renommez la manette : « Default » est réservé au profil de référence."];
            return null;
        }

        var profile = ActiveEdit.Clone();
        profile.Name = selected.DisplayName;
        profile.Family = FamilyOf(selected.Identity.Kind);
        _service.Save(profile);
        Controllers.AssignToSelected(profile.Name);
        return profile;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SaveCurrentEditToSelectedController() is { } saved)
        {
            StartEditing(saved);
            StatusMessage = Strings.Current.Format(
                "Profil « {0} » associé à {1}.", saved.Name, Controllers.Selected?.DisplayName ?? "");
        }
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (SaveCurrentEditToSelectedController() is { } saved)
        {
            StartEditing(saved);
            StatusMessage = Strings.Current.Format(
                "Réglages appliqués à {0}.", Controllers.Selected?.DisplayName ?? "");
        }
    }

    [RelayCommand]
    private void ResetToFactoryDefaults()
    {
        var factory = new ProfileData { Name = "Default" };
        _service.Save(factory);
        StatusMessage = Strings.Current["Profil « Default » restauré aux valeurs d'usine."];
    }

    /// <summary>
    /// Deletes the selected controller's own profile. The controller falls back to the default
    /// settings; the reference Default profile is never touched.
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedControllerProfile()
    {
        var selected = Controllers.Selected;
        if (selected is null)
        {
            StatusMessage = Strings.Current["Sélectionnez une manette pour supprimer son profil."];
            return;
        }

        var profile = _service.Profiles.FirstOrDefault(p =>
            p.Name == selected.DisplayName && p.Name != "Default");
        if (profile is null)
        {
            StatusMessage = Strings.Current.Format("La manette « {0} » n'a pas de profil.", selected.DisplayName);
            return;
        }

        _service.Delete(profile);
        Controllers.ForgetMissingProfiles(_service.Profiles.Select(p => p.Name));

        if (ActiveEdit?.Name == profile.Name)
        {
            ActiveEdit = null;
            IsEditing = false;
        }

        StatusMessage = Strings.Current.Format(
            "Profil de « {0} » supprimé. Elle revient aux réglages par défaut.", selected.DisplayName);
    }
}
