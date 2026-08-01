using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Osk;
using SteamXBox.Gui.Services;

using SteamXBox.Gui.Localization;

namespace SteamXBox.Gui.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly SettingsService _settings = App.SettingsSvc;
    private string _debugLogTail = "";
    private string _logFilePath = "";

    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _dotNetVersion = "";
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _coreStatus = Strings.Current["Arrêté"];
    [ObservableProperty] private string _vigEmStatus = Strings.Current["Inconnu"];
    [ObservableProperty] private string _hidHideStatus = Strings.Current["Inconnu"];

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

    public static string AppVersion => AppVersionInfo.Display;

    public void RefreshSystemInfo()
    {
        OsVersion = RuntimeInformation.OSDescription;
        DotNetVersion = RuntimeInformation.FrameworkDescription;
        LogFilePath = Path.Combine(AppContext.BaseDirectory, "steamxbox-debug.log");
    }

    public void UpdateDeviceStatus(bool connected, string name)
    {
        DeviceName = connected ? name : Strings.Current["Aucun device"];
    }

    public void UpdateCoreStatus(bool running)
    {
        CoreStatus = running ? Strings.Current["En cours"] : Strings.Current["Arrêté"];
    }

    public void UpdateDriverStatus(bool vigem, bool hidhide)
    {
        VigEmStatus = vigem ? Strings.Current["Installé"] : Strings.Current["Non installé"];
        HidHideStatus = hidhide ? Strings.Current["Installé"] : Strings.Current["Non installé"];
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
                DebugLogTail = Strings.Current["Aucun fichier de log trouvé."];
            }
        }
        catch (Exception ex)
        {
            DebugLogTail = Strings.Current.Format("Erreur lecture log : {0}", ex.Message);
        }
    }

    private string GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SteamXBox Debug Report ===");
        sb.AppendLine($"Date: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"OS: {OsVersion}");
        sb.AppendLine($".NET: {DotNetVersion}");
        sb.AppendLine($"Device: {DeviceName}");
        sb.AppendLine($"Core: {CoreStatus}");
        sb.AppendLine($"ViGEmBus: {VigEmStatus}");
        sb.AppendLine($"HidHide: {HidHideStatus}");
        sb.AppendLine();

        // Read from the assemblies instead of a hardcoded string: the previous report claimed v2.3
        // while v3.0 was shipping, which makes every bug report it produced untrustworthy.
        sb.AppendLine("=== Binaries ===");
        foreach (var name in new[] { "SteamXBox.exe", "SteamXBox.Core.exe", "Sc2Xboxed.Osk.exe" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?";
                sb.AppendLine($"{name,-22} v{version,-10} {info.Length,12:N0} bytes  {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                sb.AppendLine($"{name,-22} MISSING");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Overlay keyboard settings ===");
        try
        {
            foreach (var line in SessionReport.OverlayKeyboard(OskSettings.Load()))
                sb.AppendLine(line);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error: {ex.Message}");
        }
        sb.AppendLine();

        sb.AppendLine("=== Effective mapping settings ===");
        try
        {
            var active = _settings.Settings.LastActiveProfile;
            var resolved = ProfileMapper.LoadDetailed(active);
            foreach (var line in SessionReport.Profile(active, resolved))
                sb.AppendLine(line);
            sb.AppendLine();
            foreach (var line in SessionReport.EffectiveSettings(resolved.Settings))
                sb.AppendLine(line);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error: {ex.Message}");
        }
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
