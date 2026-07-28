using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;

namespace SteamXBox.Gui.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CoreProcessService _core;
    private readonly DeviceDetectionService _device;
    private readonly ProfileService _profileService;
    private readonly SettingsService _settings;

    [ObservableProperty] private bool _isCoreRunning;
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private string _deviceName = "Recherche...";
    [ObservableProperty] private string _currentMode = "Profile";
    [ObservableProperty] private string _statusText = "Arrêté";
    [ObservableProperty] private ProfileData? _selectedProfile;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _autoStart;
    private bool _wasDeviceConnected;

    public ObservableCollection<ProfileData> Profiles => _profileService.Profiles;

    public MainViewModel()
    {
        _core = new CoreProcessService();
        _device = new DeviceDetectionService();
        _profileService = new ProfileService();
        _settings = new SettingsService();
        _settings.Load();

        _core.OutputReceived += msg =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                LogText += $"[{time}] {msg}\n";
                if (LogText.Length > 50000)
                    LogText = LogText[^40000..];
            });
        };

        _core.ProcessExited += _ =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                IsCoreRunning = false;
                StatusText = "Arrêté";
                App.DebugVm?.UpdateCoreStatus(false);
            });
        };

        _device.DeviceChanged += dev =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                IsDeviceConnected = dev.IsConnected;
                DeviceName = dev.IsConnected ? dev.DisplayName : "Aucun device";
                App.DebugVm?.UpdateDeviceStatus(dev.IsConnected, DeviceName);

                if (AutoStart && dev.IsConnected && !_wasDeviceConnected && !IsCoreRunning)
                    StartCore();

                _wasDeviceConnected = dev.IsConnected;
            });
        };

        _profileService.ProfileSaved += profile =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                if (SelectedProfile?.Name == profile.Name)
                {
                    SelectedProfile = profile;
                    CurrentMode = profile.Mode;
                }
            });
        };

        _profileService.LoadAll();

        AutoStart = _settings.Settings.AutoStart;
        _device.StartPolling(_settings.Settings.DevicePollIntervalMs);

        var lastProfile = _settings.Settings.LastActiveProfile;
        var match = _profileService.Profiles.FirstOrDefault(p => p.Name == lastProfile);
        SelectedProfile = match ?? _profileService.ActiveProfile;
        CurrentMode = SelectedProfile?.Mode ?? "Profile";
    }

    [RelayCommand]
    private void StartCore()
    {
        if (_core.IsRunning) return;
        var profile = SelectedProfile ?? new ProfileData();
        _core.Start(profile);
        IsCoreRunning = true;
        StatusText = $"En cours ({profile.Mode})";
        CurrentMode = profile.Mode;
        App.DebugVm?.UpdateCoreStatus(true);
    }

    [RelayCommand]
    private void StopCore()
    {
        _core.Stop();
        IsCoreRunning = false;
        StatusText = "Arrêté";
        App.DebugVm?.UpdateCoreStatus(false);
    }

    [RelayCommand]
    private void ToggleCore()
    {
        if (IsCoreRunning) StopCore();
        else StartCore();
    }

    [RelayCommand]
    private void ClearLog() => LogText = "";

    partial void OnSelectedProfileChanged(ProfileData? value)
    {
        if (value != null)
        {
            _profileService.ActiveProfile = value;
            CurrentMode = value.Mode;
            _settings.Settings.LastActiveProfile = value.Name;
            _settings.Save();
            if (IsCoreRunning)
                StatusText = $"En cours ({value.Mode})";
        }
    }

    partial void OnCurrentModeChanged(string value)
    {
        App.DebugVm?.UpdateDriverStatus(true, true);
        if (IsCoreRunning)
            StatusText = $"En cours ({value})";
    }

    partial void OnAutoStartChanged(bool value)
    {
        _settings.Settings.AutoStart = value;
        _settings.Save();
    }

    public void Dispose()
    {
        _device.StopPolling();
        _core.Stop();
        _core.Dispose();
        _device.Dispose();
    }
}
