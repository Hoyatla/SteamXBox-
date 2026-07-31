using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Osk;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;

using SteamXBox.Gui.Localization;

namespace SteamXBox.Gui.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ProfileService _service;

    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private ProfileData? _activeEdit;
    [ObservableProperty] private bool _isEditingDefault;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _activeProfileName = "";
    [ObservableProperty] private ProfileData? _selectedProfileItem;

    partial void OnSelectedProfileItemChanged(ProfileData? value)
    {
        if (value != null)
            StartEditing(value);
    }

    public ObservableCollection<ProfileData> Profiles => _service.Profiles;

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

    private string GetButton(string key) => ActiveEdit?.Buttons.GetValueOrDefault(key) ?? "";
    private void SetButton(string key, string value)
    {
        if (ActiveEdit == null) return;
        ActiveEdit.Buttons[key] = value;
        OnPropertyChanged(ButtonPropName(key));
    }
    private string GetMotion(string key) => ActiveEdit?.Motions.GetValueOrDefault(key) ?? "";
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
    private const double XboxStickDeadZoneMin = 0.0, XboxStickDeadZoneMax = 0.30;
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

    public double XboxStickDeadZonePercent
    {
        get => GetPercent(p => p.XboxStickDeadZone, XboxStickDeadZoneMin, XboxStickDeadZoneMax);
        set => SetPercent((p, v) => p.XboxStickDeadZone = Math.Round(v, 3), value, XboxStickDeadZoneMin, XboxStickDeadZoneMax,
            nameof(XboxStickDeadZonePercent), nameof(XboxStickDeadZoneDisplay));
    }
    public string XboxStickDeadZoneDisplay => $"{XboxStickDeadZonePercent:0} %";

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
        SyncActiveProfileName();

        App.MainVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedProfile))
                SyncActiveProfileName();
        };
    }

    private void SyncActiveProfileName()
    {
        var name = App.MainVm.SelectedProfile?.Name ?? "";
        ActiveProfileName = name;
        // Keep ListBox selection in sync
        SelectedProfileItem = App.MainVm.SelectedProfile;
    }

    [RelayCommand]
    private void CreateNewProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;
        var name = NewProfileName.Trim();
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = Strings.Current["Impossible de créer un profil nommé 'Default'."];
            return;
        }
        var source = App.MainVm.SelectedProfile ?? new ProfileData();
        var p = _service.CreateNew(name, source);
        NewProfileName = "";
        SelectedProfileItem = p;
        StatusMessage = Strings.Current["Nouveau profil créé à partir de la configuration actuelle."];
    }

    private void StartEditing(ProfileData? profile)
    {
        if (profile == null) return;

        // Auto-save previous profile before switching
        if (ActiveEdit != null && ActiveEdit != profile && ActiveEdit.Name != "Default")
        {
            _service.Save(ActiveEdit);
            StatusMessage = Strings.Current.Format("Profil « {0} » sauvegardé automatiquement.", ActiveEdit.Name);
        }
        else
        {
            StatusMessage = "";
        }

        ActiveEdit = profile;
        IsEditing = true;
        IsEditingDefault = profile.Name == "Default";
        ActiveProfileName = profile.Name;
        App.MainVm.SelectedProfile = profile;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (ActiveEdit == null) return;
        if (ActiveEdit.Name == "Default") return;
        _service.Save(ActiveEdit);
        StatusMessage = Strings.Current["Profil sauvegardé."];
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (ActiveEdit == null) return;
        if (ActiveEdit.Name == "Default") return;
        _service.Save(ActiveEdit);
        StatusMessage = Strings.Current["Paramètres appliqués."];
    }

    [RelayCommand]
    private void ResetToFactoryDefaults()
    {
        var factory = new ProfileData { Name = "Default" };
        _service.Save(factory);
        StatusMessage = Strings.Current["Profil « Default » restauré aux valeurs d'usine."];
    }

    [RelayCommand]
    private void DeleteActiveProfile(ProfileData? profile)
    {
        if (profile == null) return;
        if (profile.Name == "Default") return;
        _service.Delete(profile);
        if (ActiveEdit?.Name == profile.Name)
        {
            IsEditing = false;
            ActiveEdit = null;
            IsEditingDefault = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ActiveEdit = null;
        IsEditingDefault = false;
    }
}
