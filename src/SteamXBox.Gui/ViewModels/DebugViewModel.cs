using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamXBox.Gui.Services;

namespace SteamXBox.Gui.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly SettingsService _settings = new();
    private string _debugLogTail = "";
    private string _logFilePath = "";

    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _dotNetVersion = "";
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _coreStatus = "Arrêté";
    [ObservableProperty] private string _vigEmStatus = "Inconnu";
    [ObservableProperty] private string _hidHideStatus = "Inconnu";

    public string DebugLogTail
    {
        get => _debugLogTail;
        set => SetProperty(ref _debugLogTail, value);
    }

    public string LogFilePath
    {
        get => _logFilePath;
        set => SetProperty(ref _logFilePath, value);
    }

    public DebugViewModel()
    {
        RefreshSystemInfo();
        LoadLogTail();
    }

    public void RefreshSystemInfo()
    {
        OsVersion = RuntimeInformation.OSDescription;
        DotNetVersion = RuntimeInformation.FrameworkDescription;
        LogFilePath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
    }

    public void UpdateDeviceStatus(bool connected, string name)
    {
        DeviceName = connected ? name : "Aucun device";
    }

    public void UpdateCoreStatus(bool running)
    {
        CoreStatus = running ? "En cours" : "Arrêté";
    }

    public void UpdateDriverStatus(bool vigem, bool hidhide)
    {
        VigEmStatus = vigem ? "Installé" : "Non installé";
        HidHideStatus = hidhide ? "Installé" : "Non installé";
    }

    public void LoadLogTail()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadLines(logPath).ToList();
                var tail = lines.Skip(Math.Max(0, lines.Count - 100)).ToList();
                DebugLogTail = string.Join("\n", tail);
            }
            else
            {
                DebugLogTail = "Aucun fichier de log trouvé.";
            }
        }
        catch (Exception ex)
        {
            DebugLogTail = $"Erreur lecture log: {ex.Message}";
        }
    }

    private string GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SteamXBox Debug Report ===");
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Version: v2.3");
        sb.AppendLine($"OS: {OsVersion}");
        sb.AppendLine($".NET: {DotNetVersion}");
        sb.AppendLine($"Device: {DeviceName}");
        sb.AppendLine($"Core: {CoreStatus}");
        sb.AppendLine($"ViGEmBus: {VigEmStatus}");
        sb.AppendLine($"HidHide: {HidHideStatus}");
        sb.AppendLine();

        sb.AppendLine("=== Last 200 log lines ===");
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadLines(logPath).ToList();
                var tail = lines.Skip(Math.Max(0, lines.Count - 200)).ToList();
                foreach (var line in tail)
                    sb.AppendLine(line);
            }
            else
            {
                sb.AppendLine("No log file found.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error reading log: {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Settings ===");
        try
        {
            _settings.Load();
            var s = _settings.Settings;
            sb.AppendLine($"AutoStart: {s.AutoStart}");
            sb.AppendLine($"MinimizeToTray: {s.MinimizeToTray}");
            sb.AppendLine($"DevicePollInterval: {s.DevicePollIntervalMs}ms");
            sb.AppendLine($"LastActiveProfile: {s.LastActiveProfile}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error: {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Profiles ===");
        try
        {
            var profilesDir = Models.ProfileData.ProfilesDirectory;
            if (Directory.Exists(profilesDir))
            {
                foreach (var f in Directory.GetFiles(profilesDir, "*.json"))
                {
                    sb.AppendLine($"Profile file: {Path.GetFileName(f)}");
                    sb.AppendLine(File.ReadAllText(f));
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("No profiles directory.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error: {ex.Message}");
        }

        return sb.ToString();
    }

    [RelayCommand]
    private void CopyDebugReport()
    {
        try
        {
            var report = GenerateReport();
            System.Windows.Clipboard.SetText(report);
        }
        catch { }
    }

    [RelayCommand]
    private void SaveDebugReport()
    {
        try
        {
            var report = GenerateReport();
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SteamXBox_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, report);
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var logDir = AppContext.BaseDirectory;
        if (Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true,
            });
        }
    }
}
