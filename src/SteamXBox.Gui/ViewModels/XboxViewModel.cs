using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Output;
using SteamXBox.Gui.Localization;
using SteamXBox.Gui.Services;

namespace SteamXBox.Gui.ViewModels;

/// <summary>One remappable physical button, as one row of the Buttons panel.</summary>
public partial class XboxButtonBinding : ObservableObject
{
    private readonly Action _changed;

    public XboxButtonBinding(SteamControllerButtons physical, string label, Xbox360Buttons output, Action changed)
    {
        Physical = physical;
        Label = label;
        _output = output;
        _changed = changed;
    }

    public SteamControllerButtons Physical { get; }

    /// <summary>Name as it is printed on the controller, not the enum name.</summary>
    public string Label { get; }

    [ObservableProperty] private Xbox360Buttons _output;

    partial void OnOutputChanged(Xbox360Buttons value) => _changed();
}

/// <summary>
/// The Xbox tab's button mapping, with its own named profiles.
/// </summary>
/// <remarks>
/// Separate from the Profile-tab profiles on purpose: those describe desktop behaviour, these
/// describe what a game receives. Mixing them would mean a player could not keep one desktop setup
/// while switching gamepad layouts per game.
/// </remarks>
public partial class XboxViewModel : ObservableObject
{
    private readonly SettingsService _settings = App.SettingsSvc;
    private bool _loading;

    public ObservableCollection<XboxProfile> Profiles { get; } = [];

    /// <summary>Left half of the controller, top to bottom.</summary>
    public ObservableCollection<XboxButtonBinding> LeftBindings { get; } = [];

    /// <summary>Right half of the controller, top to bottom.</summary>
    public ObservableCollection<XboxButtonBinding> RightBindings { get; } = [];

    /// <summary>
    /// Every Xbox 360 output a physical button can produce, plus "none" to disable it.
    /// </summary>
    public Xbox360Buttons[] OutputOptions { get; } =
    [
        Xbox360Buttons.None,
        Xbox360Buttons.A, Xbox360Buttons.B, Xbox360Buttons.X, Xbox360Buttons.Y,
        Xbox360Buttons.LeftShoulder, Xbox360Buttons.RightShoulder,
        Xbox360Buttons.LeftThumb, Xbox360Buttons.RightThumb,
        Xbox360Buttons.DPadUp, Xbox360Buttons.DPadDown,
        Xbox360Buttons.DPadLeft, Xbox360Buttons.DPadRight,
        Xbox360Buttons.Start, Xbox360Buttons.Back,
    ];

    // ---- Sticks, gâchettes et vibration ----
    //
    // Chaque propriété est un pourcentage, comme dans l'onglet Profils, et écrit dans le profil dès
    // qu'elle change. Aucune n'est décorative : le runtime lit toutes ces valeurs.

    private XboxTuning Tuning => SelectedProfile?.Tuning ?? new XboxTuning();

    private void SetTuning(Action<XboxTuning> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (_loading || SelectedProfile is null)
        {
            return;
        }

        apply(SelectedProfile.Tuning);
        OnPropertyChanged(name);
        SaveProfile();
    }

    public int StickDeadZonePercent
    {
        get => (int)Math.Round(Tuning.StickDeadZone * 200);
        set => SetTuning(t => t.StickDeadZone = Math.Clamp(value, 0, 100) / 200.0);
    }

    /// <summary>50 % is linear; below favours fine aim, above reaches full deflection sooner.</summary>
    public int StickCurvePercent
    {
        get => (int)Math.Round((Tuning.StickCurve - 0.2) / 2.8 * 100);
        set => SetTuning(t => t.StickCurve = 0.2 + (Math.Clamp(value, 0, 100) / 100.0 * 2.8));
    }

    public int StickSensitivityPercent
    {
        get => (int)Math.Round((Tuning.StickSensitivity - 0.25) / 2.75 * 100);
        set => SetTuning(t => t.StickSensitivity = 0.25 + (Math.Clamp(value, 0, 100) / 100.0 * 2.75));
    }

    public int TriggerThresholdPercent
    {
        get => (int)Math.Round(Tuning.TriggerThreshold * 100);
        set => SetTuning(t => t.TriggerThreshold = Math.Clamp(value, 0, 95) / 100.0);
    }

    /// <summary>Where the trigger reads full. Pulling it down shortens the throw.</summary>
    public int TriggerFullPointPercent
    {
        get => (int)Math.Round(Tuning.TriggerFullPoint * 100);
        set => SetTuning(t => t.TriggerFullPoint = Math.Clamp(value, 5, 100) / 100.0);
    }

    public bool VibrationEnabled
    {
        get => Tuning.VibrationEnabled;
        set => SetTuning(t => t.VibrationEnabled = value);
    }

    public int VibrationIntensityPercent
    {
        get => (int)Math.Round(Tuning.VibrationIntensity * 100);
        set => SetTuning(t => t.VibrationIntensity = Math.Clamp(value, 0, 100) / 100.0);
    }

    public bool HapticForwarding
    {
        get => Tuning.HapticForwarding;
        set => SetTuning(t => t.HapticForwarding = value);
    }

    public bool TriggerHapticsEnabled
    {
        get => Tuning.TriggerHapticsEnabled;
        set => SetTuning(t => t.TriggerHapticsEnabled = value);
    }

    public int TriggerHapticStrengthPercent
    {
        get => (int)Math.Round(Tuning.TriggerHapticStrength * 100);
        set => SetTuning(t => t.TriggerHapticStrength = Math.Clamp(value, 0, 100) / 100.0);
    }

    public int TriggerActuatorIndex
    {
        get => Tuning.TriggerActuatorIndex;
        set => SetTuning(t => t.TriggerActuatorIndex = Math.Clamp(value, 0, 31));
    }

    private static readonly string[] TuningProperties =
    [
        nameof(StickDeadZonePercent), nameof(StickCurvePercent), nameof(StickSensitivityPercent),
        nameof(TriggerThresholdPercent), nameof(TriggerFullPointPercent),
        nameof(VibrationEnabled), nameof(VibrationIntensityPercent), nameof(HapticForwarding),
        nameof(TriggerHapticsEnabled), nameof(TriggerHapticStrengthPercent), nameof(TriggerActuatorIndex),
    ];

    [ObservableProperty] private XboxProfile? _selectedProfile;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _newProfileName = "";

    public XboxViewModel()
    {
        ReloadProfiles();

        var last = _settings.Settings.LastXboxProfile;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == last) ?? Profiles.FirstOrDefault();
    }

    private void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in XboxProfile.LoadAll().OrderBy(p => p.Name == XboxProfile.DefaultName ? 0 : 1)
                     .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Profiles.Add(profile);
        }
    }

    /// <summary>Labels as printed on the controller.</summary>
    private static string LabelFor(SteamControllerButtons button) => button switch
    {
        SteamControllerButtons.LeftBumper => "LB",
        SteamControllerButtons.RightBumper => "RB",
        SteamControllerButtons.LeftStick => "L3",
        SteamControllerButtons.RightStick => "R3",
        SteamControllerButtons.DPadUp => "D-Pad ↑",
        SteamControllerButtons.DPadDown => "D-Pad ↓",
        SteamControllerButtons.DPadLeft => "D-Pad ←",
        SteamControllerButtons.DPadRight => "D-Pad →",
        _ => button.ToString(),
    };

    partial void OnSelectedProfileChanged(XboxProfile? value)
    {
        if (value is null)
        {
            return;
        }

        _loading = true;
        try
        {
            var map = value.Map;

            LeftBindings.Clear();
            foreach (var button in XboxButtonMap.LeftSide)
            {
                LeftBindings.Add(new XboxButtonBinding(button, LabelFor(button), map[button], OnBindingChanged));
            }

            RightBindings.Clear();
            foreach (var button in XboxButtonMap.RightSide)
            {
                RightBindings.Add(new XboxButtonBinding(button, LabelFor(button), map[button], OnBindingChanged));
            }

            _settings.Settings.LastXboxProfile = value.Name;
            _settings.Save();
            StatusMessage = "";
        }
        finally
        {
            _loading = false;
        }

        // The tuning properties read through SelectedProfile, so they all change at once.
        foreach (var property in TuningProperties)
        {
            OnPropertyChanged(property);
        }
    }

    private void SaveProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            SelectedProfile.Buttons = CurrentMap().ToDictionary();
            SelectedProfile.Save();
            StatusMessage = Strings.Current.Format("Profil « {0} » sauvegardé automatiquement.", SelectedProfile.Name);
        }
        catch (Exception exception)
        {
            StatusMessage = Strings.Current.Format("Erreur d'enregistrement : {0}", exception.Message);
        }
    }

    /// <summary>
    /// Writes the change straight to disk, the way the Profile tab does. The runtime watches the
    /// file, so a rebinding takes effect without restarting anything.
    /// </summary>
    private void OnBindingChanged()
    {
        if (_loading)
        {
            return;
        }

        SaveProfile();
    }

    private XboxButtonMap CurrentMap()
    {
        var map = XboxButtonMap.Default;
        foreach (var binding in LeftBindings.Concat(RightBindings))
        {
            map[binding.Physical] = binding.Output;
        }
        return map;
    }

    [RelayCommand]
    private void CreateProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (string.Equals(name, XboxProfile.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = Strings.Current["Impossible de créer un profil nommé 'Default'."];
            return;
        }

        var created = new XboxProfile { Name = name, Buttons = CurrentMap().ToDictionary() };
        created.Save();

        ReloadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == name);
        NewProfileName = "";
        StatusMessage = Strings.Current["Nouveau profil créé à partir de la configuration actuelle."];
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null || SelectedProfile.Name == XboxProfile.DefaultName)
        {
            return;
        }

        SelectedProfile.Delete();
        ReloadProfiles();
        SelectedProfile = Profiles.FirstOrDefault();
    }

    /// <summary>Puts every button back to the mapping SteamXBox shipped with.</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        SelectedProfile.Buttons = XboxButtonMap.Default.ToDictionary();
        SelectedProfile.Save();

        // Re-running the setter rebuilds both columns from the profile.
        var current = SelectedProfile;
        SelectedProfile = null;
        SelectedProfile = current;

        StatusMessage = Strings.Current["Mapping remis aux valeurs par défaut."];
    }
}
