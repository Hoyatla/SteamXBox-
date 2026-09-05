using SenSÉ.Shell;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenSÉ.Core.Diagnostics;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Osk;


using SenSÉ.Shell.Localization;

namespace SenSÉ.Desktop.Settings;

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
        LogFilePath = Path.Combine(AppContext.BaseDirectory, "SenSÉ-debug.log");
    }

    private DeviceDetectionService? _devices;
    private System.Windows.Threading.DispatcherTimer? _coreWatch;

    /// <summary>
    /// Starts watching the core and the controller.
    /// </summary>
    /// <remarks>
    /// This screen used to be a tab of the configuration window, which owned the device polling and
    /// pushed its results in. It lives in another process now, so it has to look for itself —
    /// otherwise the three status lines would show whatever they happened to say at startup and
    /// never move, which is worse than showing nothing on a diagnostics screen.
    /// </remarks>
    public void StartWatching()
    {
        RefreshCoreStatus();
        RefreshDriverStatus();
        RefreshControllerSurvey();

        _coreWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _coreWatch.Tick += (_, _) => RefreshCoreStatus();
        _coreWatch.Start();

        _devices = new DeviceDetectionService();
        _devices.DeviceChanged += device => UpdateDeviceStatus(device.IsConnected, device.DisplayName);
        _devices.StartPolling(Math.Clamp(
            _settings.Settings.DevicePollIntervalMs,
            AppSettings.MinPollSeconds * 1000,
            AppSettings.MaxPollSeconds * 1000));

        UpdateDeviceStatus(_devices.CurrentDevice.IsConnected, _devices.CurrentDevice.DisplayName);
    }

    public void StopWatching()
    {
        _coreWatch?.Stop();
        _coreWatch = null;
        _devices?.Dispose();
        _devices = null;
    }

    private void RefreshCoreStatus()
        => UpdateCoreStatus(Process.GetProcessesByName("SenSÉ.Core").Length > 0);

    public void UpdateDeviceStatus(bool connected, string name)
    {
        DeviceName = connected ? name : Strings.Current["Aucun device"];
    }

    public void UpdateCoreStatus(bool running)
    {
        CoreStatus = running ? Strings.Current["En cours"] : Strings.Current["Arrêté"];
    }

    /// <summary>
    /// Reads the two kernel drivers straight from the machine.
    /// </summary>
    /// <remarks>
    /// Not on the timer: installing a driver needs a reboot or at least an installer run, so
    /// re-reading the registry every two seconds would only cost work. Opening this window again is
    /// enough to see a change.
    /// </remarks>
    private void RefreshDriverStatus()
    {
        VigEmStatus = Describe(DriverDetection.ViGEmBus());
        HidHideStatus = Describe(DriverDetection.HidHide());
    }

    [ObservableProperty] private string _xinputSlots = "";
    [ObservableProperty] private string _rivalControllers = "";
    [ObservableProperty] private string _controllerWarning = "";

    /// <summary>
    /// Reports what else is competing for the game's attention.
    /// </summary>
    /// <remarks>
    /// Added after Xbox mode did nothing in a game while the core was provably healthy. The cause
    /// was a real Xbox controller paired over Bluetooth holding XInput slot 0 — the slot nearly
    /// every game reads for player one — leaving SenSÉ's virtual pad in a slot nobody read.
    ///
    /// SenSÉ cannot reassign those slots; Windows does, in connection order. Saying so turns an
    /// unexplained silence into something the user can act on in ten seconds.
    /// </remarks>
    private void RefreshControllerSurvey()
    {
        var survey = ControllerInventory.Survey();

        XinputSlots = string.Join("  ", survey.OccupiedSlots.Select(
            (occupied, index) => $"{index}:{(occupied ? Strings.Current["occupé"] : Strings.Current["libre"])}"));

        RivalControllers = survey.Rivals.Count == 0
            ? Strings.Current["Aucune"]
            : string.Join(", ", survey.Rivals
                .GroupBy(r => r.Name)
                .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));

        ControllerWarning = BuildWarning(survey);
    }

    private static string BuildWarning(ControllerSurvey survey)
    {
        // Enumerated but unopenable means Windows failed to start the device — a Code 10. It sends
        // nothing to anyone, and no amount of SenSÉ configuration changes that.
        if (survey.SteamControllerPresent && !survey.SteamControllerUsable)
        {
            return Strings.Current["La manette Steam est détectée mais ne peut pas être ouverte : Windows n'a pas réussi à la démarrer. Débranchez et rebranchez-la."];
        }

        // Slot 0 taken while other gamepads exist is the case that silently breaks Xbox mode.
        if (survey.OccupiedSlots.Count > 0 && survey.OccupiedSlots[0] && survey.Rivals.Count > 0)
        {
            return Strings.Current["Une autre manette occupe l'emplacement XInput 0, celui que les jeux lisent pour le joueur 1. La manette virtuelle de SenSÉ se retrouve après elle et n'est pas lue. Éteignez les autres manettes puis relancez le jeu."];
        }

        return "";
    }

    /// <summary>
    /// Turns a driver status into the line shown on screen.
    /// </summary>
    /// <remarks>
    /// The version is printed rather than checked. SenSÉ works with whatever version of these
    /// two drivers Windows will load, current or later, so a bug report needs to say which one is
    /// actually on the machine — not whether it matched a number chosen at build time.
    /// </remarks>
    private static string Describe(DriverStatus status)
    {
        if (!status.Installed)
        {
            return Strings.Current["Non installé"];
        }

        return status.Version is null
            ? Strings.Current["Installé"]
            : $"{Strings.Current["Installé"]} ({status.Version})";
    }


    public void LoadLogTail()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "SenSÉ-debug.log");
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
        sb.AppendLine("=== SenSÉ Debug Report ===");
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
        foreach (var name in new[] { "SenSÉ.exe", "SenSÉ.Core.exe", "SenSÉ.Osk.exe" })
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
            var logPath = Path.Combine(AppContext.BaseDirectory, "SenSÉ-debug.log");
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
            var profilesDir = ProfileData.ProfilesDirectory;
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
                $"SenSÉ_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
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
