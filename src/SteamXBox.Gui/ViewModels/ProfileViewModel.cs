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

    public ObservableCollection<ProfileData> Profiles => _service.Profiles;

    public string[] AvailableModes { get; } = ["Profile", "Xbox360"];
    public string[] AvailableSwitchButtons { get; } = ["quick-access", "steam", "steam-or-quick-access"];

    public ProfileViewModel()
    {
        _service = App.ProfileSvc;
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
        var p = _service.CreateNew(name);
        NewProfileName = "";
        ActiveEdit = p;
        IsEditing = true;
        IsEditingDefault = false;
        StatusMessage = "";
    }

    [RelayCommand]
    private void StartEditing(ProfileData? profile)
    {
        if (profile == null) return;
        ActiveEdit = profile;
        IsEditing = true;
        IsEditingDefault = profile.Name == "Default";
        StatusMessage = "";
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
