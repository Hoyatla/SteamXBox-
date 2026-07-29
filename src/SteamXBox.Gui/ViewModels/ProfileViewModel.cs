using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;

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
    }

    partial void OnActiveEditChanged(ProfileData? value)
    {
        NotifyAllWrappers();
    }

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
            StatusMessage = "Impossible de créer un profil nommé 'Default'.";
            return;
        }
        var source = App.MainVm.SelectedProfile ?? new ProfileData();
        var p = _service.CreateNew(name, source);
        NewProfileName = "";
        SelectedProfileItem = p;
        StatusMessage = "Nouveau profil créé à partir de la configuration actuelle.";
    }

    private void StartEditing(ProfileData? profile)
    {
        if (profile == null) return;

        // Auto-save previous profile before switching
        if (ActiveEdit != null && ActiveEdit != profile && ActiveEdit.Name != "Default")
        {
            _service.Save(ActiveEdit);
            StatusMessage = $"Profil « {ActiveEdit.Name} » sauvegardé automatiquement.";
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
        StatusMessage = "Profil sauvegardé.";
    }

    [RelayCommand]
    private void ResetToFactoryDefaults()
    {
        var factory = new ProfileData { Name = "Default" };
        _service.Save(factory);
        StatusMessage = "Profil « Default » restauré aux valeurs d'usine.";
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
