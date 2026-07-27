using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SteamXBox.Gui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _autoStart = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private int _devicePollInterval = 3;
    [ObservableProperty] private bool _isHidHideInstalled;
    [ObservableProperty] private bool _isVigEmInstalled;
    [ObservableProperty] private string _hidHideStatus = "Inconnu";
    [ObservableProperty] private string _vigEmStatus = "Inconnu";

    public SettingsViewModel()
    {
        CheckDriverStatus();
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
