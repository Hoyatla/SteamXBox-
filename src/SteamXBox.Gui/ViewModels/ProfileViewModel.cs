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

    public ObservableCollection<ProfileData> Profiles => _service.Profiles;

    public string[] AvailableModes { get; } = ["Profile", "Xbox360"];
    public string[] AvailableSwitchButtons { get; } = ["quick-access", "steam", "steam-or-quick-access"];

    public ProfileViewModel(ProfileService service)
    {
        _service = service;
    }

    [RelayCommand]
    private void CreateNewProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;
        var p = _service.CreateNew(NewProfileName.Trim());
        NewProfileName = "";
        ActiveEdit = p;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEditing(ProfileData? profile)
    {
        if (profile == null) return;
        ActiveEdit = profile;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (ActiveEdit == null) return;
        _service.Save(ActiveEdit);
        IsEditing = false;
        ActiveEdit = null;
    }

    [RelayCommand]
    private void DeleteActiveProfile(ProfileData? profile)
    {
        if (profile == null) return;
        _service.Delete(profile);
        if (ActiveEdit?.Name == profile.Name)
        {
            IsEditing = false;
            ActiveEdit = null;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ActiveEdit = null;
    }
}
