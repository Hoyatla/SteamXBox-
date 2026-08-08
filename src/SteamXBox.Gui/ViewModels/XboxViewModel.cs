using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Output;
using SteamXBox.Shell.Configuration;
using SteamXBox.Shell.Localization;

namespace SteamXBox.Gui.ViewModels;

/// <summary>One remappable physical button, as one row of the Buttons panel.</summary>
public partial class XboxButtonBinding : ObservableObject
{
    private readonly Action _changed;

    public XboxButtonBinding(SteamControllerButtons physical, string label, Xbox360Buttons output, Action changed)
    {
        Physical = physical;
        _defaultLabel = label;
        _label = label;
        _output = output;
        _changed = changed;
    }

    public SteamControllerButtons Physical { get; }

    /// <summary>
    /// Name as it is printed on the controller, not the enum name.
    /// </summary>
    /// <remarks>
    /// Observable because it depends on which family of pad the tab is showing: the same row reads
    /// "B" under the Xbox tabs and "Rond (B)" under the PlayStation ones. The bound value does not
    /// change with it — a circle and a B are the same button in the same place — only what the user
    /// is told they are holding.
    /// </remarks>
    [ObservableProperty] private string _label;

    /// <summary>Relabels this row for the family of controller now being edited.</summary>
    public void Relabel(Sc2Xboxed.Core.Input.ControllerKind? kind)
        => Label = kind is { } family
            ? Sc2Xboxed.Core.Input.ControllerButtonLabels.Describe(family, Physical)
            : _defaultLabel;

    private readonly string _defaultLabel;

    [ObservableProperty] private Xbox360Buttons _output;

    partial void OnOutputChanged(Xbox360Buttons value) => _changed();
}

/// <summary>
/// The Xbox tab. Since the merge it edits the same profile as the Profile tab: the gamepad layout
/// travels inside the profile, so one "Sauvegarder" on the Profile tab writes desktop mapping and
/// Xbox layout together. There is no separate Xbox profile to pick and no auto-save anymore.
/// </summary>
/// <remarks>
/// Editing goes straight into <see cref="ProfileViewModel.ActiveEdit"/>; nothing reaches disk until
/// the user saves on the Profile tab. The runtime reads the same file, so the hot reload that used
/// to watch a separate Xbox profile now reloads both from the one profile.
/// </remarks>
public partial class XboxViewModel : ObservableObject
{
    private readonly ProfileViewModel _profile;
    private bool _loading;

    public ObservableCollection<XboxButtonBinding> LeftBindings { get; } = [];

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

    /// <summary>The connected controllers, shared with the other tab.</summary>
    public ControllerStripViewModel Controllers => ControllerStripViewModel.Shared;

    private ProfileData? ActiveEdit => _profile.ActiveEdit;

    public XboxViewModel(ProfileViewModel profile)
    {
        _profile = profile;
        _profile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProfileViewModel.ActiveEdit) or nameof(ProfileViewModel.IsEditing))
            {
                OnEditChanged();
            }
        };

        // The rows are named after the pad in the user's hands: "Rond (B)" under the PlayStation
        // tabs, "B" under the Xbox ones. Nothing about the mapping changes — only what the user is
        // told they are holding, so they are not translating every row in their head.
        ControllerStripViewModel.Shared.FamilyChanged += RelabelBindings;
        RelabelBindings(ControllerStripViewModel.Shared.Family);
        OnEditChanged();
    }

    private void RelabelBindings(Sc2Xboxed.Core.Input.ControllerKind? family)
    {
        foreach (var binding in LeftBindings.Concat(RightBindings))
        {
            binding.Relabel(family);
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

    /// <summary>
    /// Rebuilds both columns from the profile being edited. Runs whenever the Profile tab starts or
    /// ends an edit, so the two tabs can never show different layouts.
    /// </summary>
    private void OnEditChanged()
    {
        _loading = true;
        try
        {
            LeftBindings.Clear();
            RightBindings.Clear();

            if (ActiveEdit is { } edit)
            {
                var map = edit.XboxMap;
                foreach (var button in XboxButtonMap.LeftSide)
                {
                    LeftBindings.Add(new XboxButtonBinding(button, LabelFor(button), map[button], OnBindingChanged));
                }
                foreach (var button in XboxButtonMap.RightSide)
                {
                    RightBindings.Add(new XboxButtonBinding(button, LabelFor(button), map[button], OnBindingChanged));
                }
            }
        }
        finally
        {
            _loading = false;
        }

        // The tuning properties read through ActiveEdit, so they all change at once.
        foreach (var property in TuningProperties)
        {
            OnPropertyChanged(property);
        }

        if (ActiveEdit is null)
        {
            StatusMessage = Strings.Current["Choisissez ou créez un profil dans l'onglet Profils pour l'éditer ici."];
        }
    }

    /// <summary>
    /// Writes a rebinding into the profile being edited. The Profile tab's "Sauvegarder" is what
    /// puts it on disk — the runtime then reloads both halves from the one file.
    /// </summary>
    private void OnBindingChanged()
    {
        if (_loading || ActiveEdit is null)
        {
            return;
        }

        ActiveEdit.ApplyXboxMap(CurrentMap());
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

    // ---- Sticks, gâchettes et vibration ----
    //
    // Chaque propriété est un pourcentage, comme dans l'onglet Profils, et écrit dans le profil en
    // cours d'édition dès qu'elle change. Rien n'est décoratif : le runtime lit toutes ces valeurs.

    private bool Stage(Action<ProfileData> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (_loading || ActiveEdit is null)
        {
            return false;
        }

        apply(ActiveEdit);
        OnPropertyChanged(name);
        return true;
    }

    public int StickDeadZonePercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round(ActiveEdit.XboxStickDeadZone * 200);
        set => Stage(p => p.XboxStickDeadZone = Math.Clamp(value, 0, 100) / 200.0);
    }

    /// <summary>50 % est linéaire ; en dessous la visée fine gagne, au-dessus la pleine amplitude arrive plus tôt.</summary>
    public int StickCurvePercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round((ActiveEdit.XboxStickCurve - 0.2) / 2.8 * 100);
        set => Stage(p => p.XboxStickCurve = 0.2 + (Math.Clamp(value, 0, 100) / 100.0 * 2.8));
    }

    public int StickSensitivityPercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round((ActiveEdit.XboxStickSensitivity - 0.25) / 2.75 * 100);
        set => Stage(p => p.XboxStickSensitivity = 0.25 + (Math.Clamp(value, 0, 100) / 100.0 * 2.75));
    }

    public int TriggerThresholdPercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round(ActiveEdit.XboxTriggerThreshold * 100);
        set => Stage(p => p.XboxTriggerThreshold = Math.Clamp(value, 0, 95) / 100.0);
    }

    /// <summary>Where the trigger reads full. Pulling it down shortens the throw.</summary>
    public int TriggerFullPointPercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round(ActiveEdit.XboxTriggerFullPoint * 100);
        set => Stage(p => p.XboxTriggerFullPoint = Math.Clamp(value, 5, 100) / 100.0);
    }

    public bool VibrationEnabled
    {
        get => ActiveEdit?.XboxVibrationEnabled ?? false;
        set => Stage(p => p.XboxVibrationEnabled = value);
    }

    public int VibrationIntensityPercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round(ActiveEdit.XboxVibrationIntensity * 100);
        set => Stage(p => p.XboxVibrationIntensity = Math.Clamp(value, 0, 100) / 100.0);
    }

    public bool HapticForwarding
    {
        get => ActiveEdit?.XboxHapticForwarding ?? false;
        set => Stage(p => p.XboxHapticForwarding = value);
    }

    public bool TriggerHapticsEnabled
    {
        get => ActiveEdit?.XboxTriggerHapticsEnabled ?? false;
        set => Stage(p => p.XboxTriggerHapticsEnabled = value);
    }

    public int TriggerHapticStrengthPercent
    {
        get => ActiveEdit is null ? 0 : (int)Math.Round(ActiveEdit.XboxTriggerHapticStrength * 100);
        set => Stage(p => p.XboxTriggerHapticStrength = Math.Clamp(value, 0, 100) / 100.0);
    }

    public int TriggerActuatorIndex
    {
        get => ActiveEdit?.XboxTriggerActuatorIndex ?? 0;
        set => Stage(p => p.XboxTriggerActuatorIndex = Math.Clamp(value, 0, 31));
    }

    [ObservableProperty] private string _statusMessage = "";

    private static readonly string[] TuningProperties =
    [
        nameof(StickDeadZonePercent), nameof(StickCurvePercent), nameof(StickSensitivityPercent),
        nameof(TriggerThresholdPercent), nameof(TriggerFullPointPercent),
        nameof(VibrationEnabled), nameof(VibrationIntensityPercent), nameof(HapticForwarding),
        nameof(TriggerHapticsEnabled), nameof(TriggerHapticStrengthPercent), nameof(TriggerActuatorIndex),
    ];

    /// <summary>Puts every button back to the mapping SteamXBox shipped with.</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (ActiveEdit is null)
        {
            return;
        }

        ActiveEdit.ApplyXboxMap(XboxButtonMap.Default);
        ActiveEdit.XboxStickDeadZone = 0.018;
        ActiveEdit.XboxStickCurve = 1.0;
        ActiveEdit.XboxStickSensitivity = 1.0;
        ActiveEdit.XboxTriggerThreshold = 0.0;
        ActiveEdit.XboxTriggerFullPoint = 1.0;
        ActiveEdit.XboxVibrationEnabled = true;
        ActiveEdit.XboxVibrationIntensity = 1.0;
        ActiveEdit.XboxHapticForwarding = false;
        ActiveEdit.XboxTriggerHapticsEnabled = false;
        ActiveEdit.XboxTriggerHapticStrength = 0.6;
        ActiveEdit.XboxTriggerActuatorIndex = 2;

        OnEditChanged();
        StatusMessage = Strings.Current["Mapping remis aux valeurs par défaut. Enregistrez dans l'onglet Profils."];
    }
}
