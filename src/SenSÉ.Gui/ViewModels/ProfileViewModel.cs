using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Osk;
using SenSÉ.Gui.Services;

using SenSÉ.Shell.Localization;

namespace SenSÉ.Gui.ViewModels;

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

        // NE PAS assigner ici. Ajoute le 14 aout pour que choisir un profil le rende actif, retire
        // le meme jour : SelectedProfileItem est aussi ecrit par le code — RebuildVisibleProfiles,
        // StartEditing, la creation d'un profil — et chacune de ces ecritures reecrivait alors le
        // fichier d'assignation. Le noyau surveille ce fichier et recharge, le GUI se rafraichit, et
        // la selection change a nouveau.
        //
        // Resultat mesure : cinquante-six profils crees en deux minutes, et un fichier d'assignation
        // reduit a la seule famille xbox — les familles ps5 et steam avaient perdu la leur, donc
        // leurs manettes tournaient sans profil.
        //
        // Assigner reste le travail des boutons Sauvegarder et Appliquer, ou l'utilisateur le
        // demande explicitement.
    }

    /// <summary>The name a family's profiles carry, before their number.</summary>
    /// <remarks>
    /// The label the user already sees on their controller, not the internal family key: the files
    /// on disk read "Manette PS5 1" and "Manette Xbox 2", and a new one has to fall in the same
    /// series rather than start a second naming scheme beside it.
    /// </remarks>
    private static string FamilyLabelOf(ControllerKind kind) => kind switch
    {
        ControllerKind.SteamController => "Steam Controller",
        ControllerKind.DualSense => "PS5",
        ControllerKind.XInput => "Xbox",
        _ => "Profil",
    };

    /// <summary>
    /// Creates the next profile of the selected controller's family, and selects it.
    /// </summary>
    /// <remarks>
    /// Numbered by <see cref="ProfileNumbering"/>: the first is 1, and a number freed by a deletion
    /// is refilled before any higher one is handed out, so the family's list stays something the
    /// user can count through.
    ///
    /// <para>
    /// Selected at the end rather than merely created, which assigns it — a new profile nobody is
    /// using is the "Manette Xbox 1" already sitting on this machine, written once and never
    /// reachable.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void NewProfile()
    {
        // The tab decides the family, not the controller highlighted in the strip.
        //
        // Selecting a tab sets Controllers.Family, which filters the list — and leaves Selected
        // alone. Selected defaults to the first controller attached, whatever it is. So creating a
        // profile from the PS5 tab took the family of that first controller — an Xbox pad here —
        // wrote family="XInput", and the PS5 list could not show it. The file was correct, in the
        // wrong family, invisible where it was asked for.
        //
        // The controller is not consulted at all: preparing a PS5 profile must not require a PS5 pad
        // to be plugged in.
        if (Controllers.Family is not { } kind)
        {
            StatusMessage = Strings.Current["Ouvrez l'onglet de la famille pour laquelle créer un profil."];
            return;
        }

        var label = FamilyLabelOf(kind);
        var family = FamilyOf(kind);

        var name = ProfileNumbering.NextName(
            label,
            _service.Profiles.Where(p => p.Family == family).Select(p => p.Name));

        var created = (ActiveEdit ?? new ProfileData()).Clone();
        created.Name = name;
        created.Family = family;
        created.XboxButtons = CorrectedLayoutFor(kind, created.XboxButtons);

        _service.Save(created);

        if (!_service.Profiles.Contains(created))
        {
            _service.Profiles.Add(created);
        }

        RebuildVisibleProfiles();
        SelectedProfileItem = created;

        StatusMessage = Strings.Current.Format("Profil « {0} » créé dans la famille {1}.", name, family);
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

    /// <summary>
    /// A profile born from cloning the reference Default inherits the reference's Steam Controller
    /// layout (Menu→Back, View→Start — the measured quirk). Saved onto a DualSense or an Xbox pad
    /// unchanged, that inherited quirk crosses Options and Start. The reference Menu/View values
    /// count as "unchanged"; anything else was deliberately chosen and is left alone.
    /// </summary>
    private static Dictionary<string, string> CorrectedLayoutFor(
        ControllerKind kind, Dictionary<string, string> stored)
    {
        if (kind == ControllerKind.SteamController)
        {
            return stored;
        }

        var reference = XboxButtonMap.Default;
        var inheritsReferenceQuirk =
            stored.TryGetValue(nameof(SteamControllerButtons.Menu), out var menu)
            && menu == reference[SteamControllerButtons.Menu].ToString()
            && stored.TryGetValue(nameof(SteamControllerButtons.View), out var view)
            && view == reference[SteamControllerButtons.View].ToString();

        if (!inheritsReferenceQuirk)
        {
            return stored;
        }

        var corrected = new Dictionary<string, string>(stored);
        corrected[nameof(SteamControllerButtons.Menu)] =
            XboxButtonMap.DefaultFor(kind)[SteamControllerButtons.Menu].ToString();
        corrected[nameof(SteamControllerButtons.View)] =
            XboxButtonMap.DefaultFor(kind)[SteamControllerButtons.View].ToString();
        return corrected;
    }

    private bool _reloading;

    /// <summary>
    /// Re-reads the profiles folder, then rebuilds the family's list.
    /// </summary>
    /// <remarks>
    /// <see cref="ProfileService.LoadAll"/> clears the collection before refilling it, and the
    /// collection's own change event rebuilds the visible list — so this would re-enter itself once
    /// per profile without the guard. The guard is what makes calling it from a UI event safe.
    /// </remarks>
    public void ReloadFromDisk()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;

        try
        {
            var selected = SelectedProfileItem?.Name;

            _service.LoadAll();
            RebuildVisibleProfiles();

            // The instance is new after a reload, so the selection has to be found again by name or
            // the panel empties itself every time the list refreshes.
            if (selected is { Length: > 0 })
            {
                var again = VisibleProfiles.FirstOrDefault(p => p.Name == selected);

                if (again is not null)
                {
                    _syncingSelection = true;
                    SelectedProfileItem = again;
                    _syncingSelection = false;
                }
            }
        }
        catch (Exception failure)
        {
            SenSÉ.Core.Diagnostics.UiLog.Failure("reloading the profiles folder", failure);
        }
        finally
        {
            _reloading = false;
        }
    }

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

        // What saving would actually do, which is not what this said. It looked for a profile named
        // after the controller — the naming the save no longer uses — so it announced "aucun profil"
        // while the family had one, and "il sera écrasé" while naming a file that did not exist.
        //
        // Saving writes the profile being edited, under its own name, to its own family. The
        // controller only decides which family receives it.
        var controllerFamily = FamilyOf(selected.Identity.Kind);
        var edited = ActiveEdit?.Name;

        TargetContext = edited switch
        {
            null or "" or "Default" => Strings.Current.Format(
                "Un nouveau profil {0} sera créé à la sauvegarde.", controllerFamily),

            _ when !string.Equals(ActiveEdit?.Family, controllerFamily, StringComparison.Ordinal)
                   && !string.IsNullOrWhiteSpace(ActiveEdit?.Family) => Strings.Current.Format(
                "« {0} » est un profil {1} : il ne peut pas être enregistré sur cette manette {2}.",
                edited, ActiveEdit!.Family, controllerFamily),

            _ => Strings.Current.Format(
                "Profil « {0} » — il sera écrasé à la sauvegarde et associé à {1}.", edited, controllerFamily),
        };
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

        // Comme pour les curseurs : un bouton reassigne puis oublie doit arriver sur le disque,
        // sinon le noyau continue d'appliquer l'ancien profil sans que rien ne le dise. Le
        // requetage est reparti dans RequestSave, qui limite a une ecriture par tranche de 600 ms.
        RequestSave();
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
        nameof(LeftStickDeadZonePercent), nameof(LeftStickDeadZoneDisplay),
        nameof(RightStickDeadZonePercent), nameof(RightStickDeadZoneDisplay),
        nameof(StickPointerSpeedPercent), nameof(StickPointerSpeedDisplay),
        nameof(StickPointerCurvePercent), nameof(StickPointerCurveDisplay),
        nameof(XboxLeftStickDeadZonePercent), nameof(XboxLeftStickDeadZoneDisplay),
        nameof(XboxRightStickDeadZonePercent), nameof(XboxRightStickDeadZoneDisplay),
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
        RequestSave();
    }

    /// <summary>
    /// Writes the edited profile to disk shortly after the last change.
    /// </summary>
    /// <remarks>
    /// Every slider (<see cref="SetPercent"/>) and every button box (<see cref="SetButton"/>) calls
    /// this, so an adjustment reaches the disk on its own. Before the wiring, the file was only
    /// touched by the Save and Apply buttons, and a user who moved a slider and went back to playing
    /// lost the change — the core then went on applying a profile written days earlier, faithfully
    /// and to the wrong values.
    ///
    /// <para>
    /// The wiring was removed on 14 August 2026 because it appeared to write at every slider tick,
    /// and restored on 17 August 2026: without it, moving the right pad sensitivity slider changed
    /// nothing, because the core's live reload only ever sees what reaches the disk.
    /// </para>
    ///
    /// <para>
    /// The throttle below keeps the writes to one per drag interval — first change written at once,
    /// the rest coalesced — and the core's profile watcher waits for quiet before reloading, so the
    /// write is not the danger the 14 August removal feared.
    /// </para>
    ///
    /// <para>
    /// Only the assignment stays manual: saving the values is what the user expects to be
    /// automatic, binding a profile to a controller is a decision.
    /// </para>
    /// </remarks>
    private void RequestSave()
    {
        // A profile with no name has no file to be written to, and inventing one here would leave
        // stray profiles behind every time somebody opened the editor.
        if (ActiveEdit is not { } edit || string.IsNullOrWhiteSpace(edit.Name))
        {
            return;
        }

        // A throttle rather than a plain delay, and the difference matters: a plain delay writes
        // nothing until the gesture stops, so closing the window on the last move loses it. Writing
        // the first change at once puts the value on disk immediately and coalesces only what
        // follows inside the same drag.
        var since = DateTimeOffset.UtcNow - _savedAt;

        if (since >= SaveDelay)
        {
            Write(edit);
            return;
        }

        _saveDelay?.Cancel();
        _saveDelay = new CancellationTokenSource();

        var token = _saveDelay.Token;
        var wait = SaveDelay - since;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
                Write(edit);
            }
            catch (OperationCanceledException)
            {
                // Another change arrived first; that one carries this one's values too.
            }
        });

        void Write(ProfileData profile)
        {
            _savedAt = DateTimeOffset.UtcNow;

            try
            {
                // Save mutates the bound Profiles collection and raises ProfileSaved, both of which
                // belong to the UI thread. The throttled path reaches this from Task.Run, so the
                // whole call is marshalled here rather than trusting each consumer to be thread-safe.
                if (App.Current.Dispatcher.CheckAccess())
                    _service.Save(profile);
                else
                    App.Current.Dispatcher.Invoke(() => _service.Save(profile));
            }
            catch (Exception failure)
            {
                // Named rather than swallowed. A save that fails in silence is what let a whole day
                // of adjustments disappear without a single line anywhere.
                SenSÉ.Core.Diagnostics.UiLog.Failure(
                    $"saving the profile '{profile.Name}'", failure);
            }
        }
    }

    /// <summary>Long enough to cover a slider drag, short enough to survive closing the window.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? _saveDelay;
    private DateTimeOffset _savedAt = DateTimeOffset.MinValue;

    public double RightPadSensitivityPercent
    {
        get => ActiveEdit is null ? 100.0 : ActiveEdit.RightPadSensitivityPercent;
        set
        {
            if (ActiveEdit is null) return;
            ActiveEdit.RightPadSensitivityPercent = Math.Round(Math.Clamp(value, 0.0, 100.0));
            OnPropertyChanged(nameof(RightPadSensitivityPercent));
            OnPropertyChanged(nameof(RightPadSensitivityDisplay));
            RequestSave();
        }
    }
    public string RightPadSensitivityDisplay => $"{RightPadSensitivityPercent:0} %";

    public double LeftPadSensitivityPercent
    {
        get => GetPercent(p => p.LeftPadSensitivity, LeftPadSensMin, LeftPadSensMax);
        set => SetPercent((p, v) => p.LeftPadSensitivity = Math.Round(v, 1), value, LeftPadSensMin, LeftPadSensMax,
            nameof(LeftPadSensitivityPercent), nameof(LeftPadSensitivityDisplay));
    }
    public string LeftPadSensitivityDisplay => $"{LeftPadSensitivityPercent:0} %";

    /// <summary>
    /// Dead zone of the left stick in Profile mode.
    /// </summary>
    /// <remarks>
    /// One slider used to move both sticks at once, which made the setting impossible to use: the
    /// value that silences a worn left stick is not the value that keeps a right stick precise, and
    /// a single number forced the user to pick which of the two to spoil.
    /// </remarks>
    public double LeftStickDeadZonePercent
    {
        get => GetPercent(p => p.LeftStickDeadZone ?? p.StickDeadZone, StickDeadZoneMin, StickDeadZoneMax);
        set => SetPercent((p, v) => p.LeftStickDeadZone = Math.Round(v, 3), value, StickDeadZoneMin, StickDeadZoneMax,
            nameof(LeftStickDeadZonePercent), nameof(LeftStickDeadZoneDisplay));
    }
    public string LeftStickDeadZoneDisplay => $"{LeftStickDeadZonePercent:0} %";

    /// <summary>Dead zone of the right stick in Profile mode. See <see cref="LeftStickDeadZonePercent"/>.</summary>
    public double RightStickDeadZonePercent
    {
        get => GetPercent(p => p.RightStickDeadZone ?? p.StickDeadZone, StickDeadZoneMin, StickDeadZoneMax);
        set => SetPercent((p, v) => p.RightStickDeadZone = Math.Round(v, 3), value, StickDeadZoneMin, StickDeadZoneMax,
            nameof(RightStickDeadZonePercent), nameof(RightStickDeadZoneDisplay));
    }
    public string RightStickDeadZoneDisplay => $"{RightStickDeadZonePercent:0} %";

    // How the stick drives the desktop pointer, for the families that have no trackpad. Both were
    // constants in the runtime until now: every controller pointed at the same speed with the same
    // curve, and there was nothing here to bind to.
    private const double StickPointerSpeedMin = 200.0, StickPointerSpeedMax = 4000.0;
    private const double StickPointerCurveMin = 1.0, StickPointerCurveMax = 3.0;

    public double StickPointerSpeedPercent
    {
        get => GetPercent(p => p.StickPointerSpeed, StickPointerSpeedMin, StickPointerSpeedMax);
        set => SetPercent((p, v) => p.StickPointerSpeed = Math.Round(v), value, StickPointerSpeedMin, StickPointerSpeedMax,
            nameof(StickPointerSpeedPercent), nameof(StickPointerSpeedDisplay));
    }

    /// <summary>Shown in pixels per second rather than as a percentage: it is a speed, and it is legible.</summary>
    public string StickPointerSpeedDisplay =>
        ActiveEdit is null ? "—" : $"{ActiveEdit.StickPointerSpeed:0} px/s";

    public double StickPointerCurvePercent
    {
        get => GetPercent(p => p.StickPointerCurve, StickPointerCurveMin, StickPointerCurveMax);
        set => SetPercent((p, v) => p.StickPointerCurve = Math.Round(v, 2), value, StickPointerCurveMin, StickPointerCurveMax,
            nameof(StickPointerCurvePercent), nameof(StickPointerCurveDisplay));
    }

    public string StickPointerCurveDisplay =>
        ActiveEdit is null ? "—" : $"{ActiveEdit.StickPointerCurve:0.00}";

    // Stick tuning on the stick-only families (PS5, Xbox). Same values as the Xbox tab, exposed
    // here so the Profils tab shows them where the pads would otherwise be — a stick has no pads,
    // and a pad-only card would be lying about what it is editing.
    private const double XboxStickDeadZoneMin = 0.0, XboxStickDeadZoneMax = 0.5;
    private const double XboxStickCurveMin = 0.2, XboxStickCurveMax = 3.0;
    private const double XboxStickSensitivityMin = 0.25, XboxStickSensitivityMax = 3.0;

    /// <summary>Dead zone of the left stick in Xbox mode. See <see cref="LeftStickDeadZonePercent"/>.</summary>
    public double XboxLeftStickDeadZonePercent
    {
        get => GetPercent(p => p.XboxLeftStickDeadZone ?? p.XboxStickDeadZone, XboxStickDeadZoneMin, XboxStickDeadZoneMax);
        set => SetPercent((p, v) => p.XboxLeftStickDeadZone = Math.Round(v, 3), value, XboxStickDeadZoneMin, XboxStickDeadZoneMax,
            nameof(XboxLeftStickDeadZonePercent), nameof(XboxLeftStickDeadZoneDisplay));
    }
    public string XboxLeftStickDeadZoneDisplay => $"{XboxLeftStickDeadZonePercent:0} %";

    /// <summary>Dead zone of the right stick in Xbox mode.</summary>
    public double XboxRightStickDeadZonePercent
    {
        get => GetPercent(p => p.XboxRightStickDeadZone ?? p.XboxStickDeadZone, XboxStickDeadZoneMin, XboxStickDeadZoneMax);
        set => SetPercent((p, v) => p.XboxRightStickDeadZone = Math.Round(v, 3), value, XboxStickDeadZoneMin, XboxStickDeadZoneMax,
            nameof(XboxRightStickDeadZonePercent), nameof(XboxRightStickDeadZoneDisplay));
    }
    public string XboxRightStickDeadZoneDisplay => $"{XboxRightStickDeadZonePercent:0} %";

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

    // Floating or pinned now lives on the controller's profile, as ActiveEdit.OskFloating, and the
    // checkbox binds straight to it. The property that used to sit here wrote the shared setting,
    // which the runtime keeps only as a fallback for an overlay launched without being told — so
    // once the profile started deciding, this control silently stopped doing anything. Removed
    // rather than left in place: a switch that saves a value nobody reads is worse than no switch.

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
    public string[] LeftStickOptions { get; } = ["ArrowKeys", "Wheel", "None"];
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
            // From disk, not from memory. The collection is filled once at startup, so a profile
            // written since — by the other tab, by a previous run, or by anything that touched the
            // folder — simply was not in the list, and its family tab looked empty while the file
            // sat on disk. Found 14 August: fifty-six profiles present in the folder, none of them
            // in the panel of the family they belong to.
            ReloadFromDisk();

            OnPropertyChanged(nameof(IsStickFamily));
            OnPropertyChanged(nameof(IsPadFamily));
        };
        Controllers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControllerStripViewModel.Selected))
                RefreshTarget();
        };

        ReloadFromDisk();
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

        // The tab, not the controller in the strip. Selecting a tab filters the list and leaves the
        // strip selection alone, so editing a PS5 profile with an Xbox pad first in the strip gave
        // controllerFamily = XInput — the guard below then refused the save, and the setting the
        // user had just chosen never reached the disk at all.
        //
        // Falls back to the controller only when no tab family is set.
        var controllerFamily = Controllers.Family is { } tabKind
            ? FamilyOf(tabKind)
            : FamilyOf(selected.Identity.Kind);

        var profile = ActiveEdit.Clone();

        // The profile keeps its own name and its own family. Both used to be overwritten from the
        // controller selected in the strip: editing "PS5 1" with an Xbox pad selected wrote the
        // result under the Xbox pad's name, stamped family=XInput, and left the PS5 profile
        // untouched. That is the whole of "les sauvegardes PS5 sont enregistrées comme du Xbox".
        //
        // A profile freshly cloned from the reference has no family yet, and only then does the
        // selected controller decide it.
        if (string.IsNullOrWhiteSpace(profile.Family))
        {
            profile.Family = controllerFamily;
        }

        // A profile belongs to one family and is assigned to one family. Saving a PS5 profile onto
        // an Xbox pad would either re-stamp the profile or file it under the wrong family, and both
        // are how one family's tuning ends up on another's hardware. Refused, and named.
        if (!string.Equals(profile.Family, controllerFamily, StringComparison.Ordinal))
        {
            StatusMessage = Strings.Current.Format(
                "« {0} » est un profil {1} ; il ne peut pas être enregistré sur {2}, qui est {3}.",
                profile.Name, profile.Family, selected.DisplayName, controllerFamily);
            return null;
        }

        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name == "Default")
        {
            // Editing the reference produces a new profile of the family rather than overwriting it.
            profile.Name = ProfileNumbering.NextName(
                FamilyLabelOf(Controllers.Family ?? selected.Identity.Kind),
                _service.Profiles.Where(p => p.Family == controllerFamily).Select(p => p.Name));
        }

        profile.XboxButtons = CorrectedLayoutFor(Controllers.Family ?? selected.Identity.Kind, profile.XboxButtons);
        _service.Save(profile);

        // A save without a controller to receive it is a failed save: the profile exists, but it was
        // not assigned to any family, and must not read as if it was.
        if (!Controllers.AssignToSelected(profile.Name))
        {
            return null;
        }

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

        // The profile shown in the list, which is the one the user is looking at when they press
        // Delete. This used to hunt for a profile whose name equalled the controller's display name
        // — so "Steam Controller perso", "Manette PS5 2" and everything else a user ever named or
        // numbered was undeletable, and the button answered that the controller had no profile while
        // its profile sat selected on screen.
        var profile = SelectedProfileItem;

        if (profile is null)
        {
            StatusMessage = Strings.Current["Choisissez le profil à supprimer dans la liste."];
            return;
        }

        // The reference profile is the one every new profile is cloned from; deleting it would leave
        // the product with nothing to start from.
        if (profile.Name == "Default")
        {
            StatusMessage = Strings.Current["« Default » est le profil de référence et ne peut pas être supprimé."];
            return;
        }

        var removed = profile.Name;

        _service.Delete(profile);
        Controllers.ForgetMissingProfiles(_service.Profiles.Select(p => p.Name));

        if (ActiveEdit?.Name == removed)
        {
            ActiveEdit = null;
            IsEditing = false;
        }

        // The list is rebuilt and the selection dropped, or the deleted profile stays on screen as a
        // row that opens nothing. Its number goes back into the family's pool: the next profile
        // created takes it rather than counting past it.
        SelectedProfileItem = null;
        RebuildVisibleProfiles();

        StatusMessage = Strings.Current.Format("Profil « {0} » supprimé.", removed);
    }
}
