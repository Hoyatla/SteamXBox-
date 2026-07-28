using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamXBox.Gui.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SteamXBox.Gui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private bool _autoStart = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private int _devicePollInterval = 3;
    [ObservableProperty] private bool _isHidHideInstalled;
    [ObservableProperty] private bool _isVigEmInstalled;
    [ObservableProperty] private string _hidHideStatus = "Inconnu";
    [ObservableProperty] private string _vigEmStatus = "Inconnu";

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
    }

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
                    HidHideStatus = IsHidHideInstalled ? "Installé" : "Non installé";
                }
            }

            IsVigEmInstalled = IsViGEmBusInstalled();
            VigEmStatus = IsVigEmInstalled ? "Installé" : "Non installé";
        }
        catch
        {
            HidHideStatus = "Erreur";
            VigEmStatus = "Erreur";
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
            FileName = "https://github.com/ViGEm/HidHide/releases",
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
            FileName = "https://github.com/ViGEm/ViGEmBus/releases",
            UseShellExecute = true,
        });
    }
}
