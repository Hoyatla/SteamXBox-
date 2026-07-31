using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;

using SteamXBox.Gui.Localization;

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
    [ObservableProperty] private string _statusText = Strings.Current["Arrêté"];
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
        _profileService = App.ProfileSvc;
        _settings = App.SettingsSvc;
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

        _core.ProcessExited += code =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_core.IsRunning)
                {
                    LogText += $"[{DateTime.Now:HH:mm:ss}] [WARN] Stale ProcessExited ignored (core is still running)\n";
                    return;
                }
                LogText += Strings.Current.Format("[{0}] [INFO] Core arrêté (code {1})\n", DateTime.Now.ToString("HH:mm:ss"), code);
                IsCoreRunning = false;
                StatusText = Strings.Current["Arrêté"];
                App.DebugVm?.UpdateCoreStatus(false);
            });
        };

        _device.DeviceChanged += dev =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                IsDeviceConnected = dev.IsConnected;
                DeviceName = dev.IsConnected ? dev.DisplayName : Strings.Current["Aucun device"];
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

        // Tue tous les Core.exe orphelins (quel que soit le chemin)
        foreach (var p in Process.GetProcessesByName("SteamXBox.Core"))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
        }

        var profile = SelectedProfile ?? new ProfileData();

        // Core resolves the profile by name from disk and silently falls back to built-in defaults
        // when the file is absent. Without this, launching with a profile that was never saved makes
        // every value in the editor inert at runtime, with no feedback anywhere.
        if (!File.Exists(profile.FilePath))
        {
            try
            {
                profile.Save();
                LogText += Strings.Current.Format("[{0}] [INFO] Profil '{1}' écrit sur disque avant démarrage\n", DateTime.Now.ToString("HH:mm:ss"), profile.Name);
            }
            catch (Exception ex)
            {
                LogText += Strings.Current.Format("[{0}] [ERROR] Impossible d'écrire le profil '{1}' : {2}\n", DateTime.Now.ToString("HH:mm:ss"), profile.Name, ex.Message);
            }
        }

        var corePath = _core.GetCorePath();
        LogText += Strings.Current.Format("[{0}] [INFO] Démarrage Core : {1} (exists={2})\n", DateTime.Now.ToString("HH:mm:ss"), corePath, File.Exists(corePath));
        LogText += Strings.Current.Format("[{0}] [INFO] Profil actif : {1} ({2})\n", DateTime.Now.ToString("HH:mm:ss"), profile.Name, profile.FilePath);
        if (!_core.Start(profile))
        {
            LogText += Strings.Current.Format("[{0}] [ERROR] Échec du démarrage de Core\n", DateTime.Now.ToString("HH:mm:ss"));
            StatusText = Strings.Current["Erreur au démarrage"];
            return;
        }
        IsCoreRunning = true;
        StatusText = Strings.Current.Format("En cours ({0})", profile.Mode);
        CurrentMode = profile.Mode;
        App.DebugVm?.UpdateCoreStatus(true);
    }

    [RelayCommand]
    private void StopCore()
    {
        _core.Stop();
        IsCoreRunning = false;
        StatusText = Strings.Current["Arrêté"];
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
                StatusText = Strings.Current.Format("En cours ({0})", value.Mode);
        }
    }

    partial void OnCurrentModeChanged(string value)
    {
        App.DebugVm?.UpdateDriverStatus(true, true);
        if (IsCoreRunning)
            StatusText = Strings.Current.Format("En cours ({0})", value);
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
