using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Osk;
using SteamXBox.Gui.Localization;
using SteamXBox.Gui.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SteamXBox.Gui.ViewModels;

/// <summary>
/// ComboBox entry pairing a localized label with the enum value actually stored. Binding to the
/// value rather than the label keeps the UI and the settings model from drifting apart.
/// </summary>
public sealed record OskTypingModeOption(string Display, OskTypingMode Value);

/// <summary>ComboBox entry for the interface language.</summary>
public sealed record LanguageOption(string Display, AppLanguage Value);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private bool _autoStart = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private int _devicePollInterval = 3;
    [ObservableProperty] private bool _isHidHideInstalled;
    [ObservableProperty] private bool _isVigEmInstalled;
    [ObservableProperty] private string _hidHideStatus = Strings.Current["Inconnu"];
    [ObservableProperty] private string _vigEmStatus = Strings.Current["Inconnu"];

    public SettingsViewModel()
    {
        _settingsService = new SettingsService();
        _settingsService.Load();

        AutoStart = _settingsService.Settings.AutoStart;
        MinimizeToTray = _settingsService.Settings.MinimizeToTray;
        DevicePollInterval = _settingsService.Settings.DevicePollIntervalMs / 1000;
        if (DevicePollInterval < 1) DevicePollInterval = 1;

        CheckDriverStatus();
    }

    partial void OnAutoStartChanged(bool value)
    {
        _settingsService.Settings.AutoStart = value;
        _settingsService.Save();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.Settings.MinimizeToTray = value;
        _settingsService.Save();
    }

    partial void OnDevicePollIntervalChanged(int value)
    {
        _settingsService.Settings.DevicePollIntervalMs = value * 1000;
        _settingsService.Save();
        OnPropertyChanged(nameof(DevicePollIntervalDisplay));
    }

    public string DevicePollIntervalDisplay => $"{DevicePollInterval} s";

    // ---- Language ----

    public LanguageOption[] LanguageOptions { get; } =
    [
        new("Suivre Windows", AppLanguage.System),
        new("Français", AppLanguage.French),
        new("English", AppLanguage.English),
    ];

    public AppLanguage Language
    {
        get => _settingsService.Settings.Language;
        set
        {
            if (_settingsService.Settings.Language == value) return;
            _settingsService.Settings.Language = value;
            _settingsService.Save();
            Strings.Current.Apply(value);
            OnPropertyChanged();
        }
    }

    // ---- Windows startup ----

    /// <summary>
    /// Reads the registry rather than the settings file: the Run key can be removed by other tools,
    /// and the checkbox has to show what Windows will actually do.
    /// </summary>
    public bool StartWithWindows
    {
        get => WindowsStartupService.IsEnabled();
        set
        {
            if (WindowsStartupService.IsEnabled() == value) return;

            if (!WindowsStartupService.SetEnabled(value))
            {
                StartupStatus = Strings.Current["Impossible de modifier le démarrage Windows."];
                OnPropertyChanged();
                return;
            }

            _settingsService.Settings.StartWithWindows = value;
            _settingsService.Save();
            StartupStatus = "";
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _startupStatus = "";

    // Overlay keyboard settings live in the profile editor (ProfileViewModel), with the rest of the
    // controller configuration.

    [RelayCommand]
    private void CheckDriverStatus()
    {
        try
        {
            var hidHidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SteamXBox.Core.exe");
            if (File.Exists(hidHidePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = hidHidePath,
                    Arguments = "hidhide-status",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    IsHidHideInstalled = !output.Contains("not installed") && !output.Contains("error");
                    HidHideStatus = IsHidHideInstalled ? Strings.Current["Installé"] : Strings.Current["Non installé"];
                }
            }

            IsVigEmInstalled = IsViGEmBusInstalled();
            VigEmStatus = IsVigEmInstalled ? Strings.Current["Installé"] : Strings.Current["Non installé"];
        }
        catch
        {
            HidHideStatus = Strings.Current["Erreur"];
            VigEmStatus = Strings.Current["Erreur"];
        }
    }

    private static bool IsViGEmBusInstalled()
    {
        try
        {
            var vigemKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (vigemKey == null) return false;

            foreach (var subKeyName in vigemKey.GetSubKeyNames())
            {
                using var subKey = vigemKey.OpenSubKey(subKeyName);
                var name = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                if (name.Contains("ViGEmBus", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    [RelayCommand]
    private void OpenHidHideDownload()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/nefarius/HidHide/releases/latest",
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private static void OpenUrl(string? url)
    {
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenViGEmDownload()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/nefarius/ViGEmBus/releases/latest",
            UseShellExecute = true,
        });
    }
}
