using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenSÉ.Gui.Services;

using SenSÉ.Core.Diagnostics;
using SenSÉ.Shell.Localization;

namespace SenSÉ.Gui.ViewModels;

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

    /// <summary>The connected controllers, shared with every other tab.</summary>
    /// <remarks>
    /// The same instance the Profile and Xbox tabs show. Home used to answer the same question from
    /// <c>DeviceDetectionService</c>, which enumerates on its own — two sources of truth about what
    /// is plugged in, free to disagree, and one of them limited to a single Valve device so it read
    /// "déconnecté" with a DualSense in hand.
    /// </remarks>
    public ControllerStripViewModel Controllers => ControllerStripViewModel.Shared;

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
            });
        };

        _device.DeviceChanged += dev =>
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                IsDeviceConnected = dev.IsConnected;
                DeviceName = dev.IsConnected ? dev.DisplayName : Strings.Current["Aucun device"];

                // One detector, one display. This service already watches for arrivals and
                // departures, so it is what tells the strip to refresh — rather than the strip
                // polling HID and XInput on its own alongside it. Two enumerations of the same
                // hardware are free to disagree, and the one here only ever saw a single Valve
                // device: it reported "déconnecté" with a DualSense in hand while the other tabs
                // listed it.
                //
                // The properties above are kept because the Start and Stop buttons still read them.
                // Nothing displays them any more.
                Controllers.Refresh();

                // Start on detection, when the setting asks for it. This used to be here, was
                // removed because the configuration window and the environment both launched the
                // bridge with --restart and killed each other in a loop, and never came back — so
                // "démarrer automatiquement à la détection" has been a setting that did nothing ever
                // since. The loop is no longer possible: the core holds a single-instance mutex, and
                // starting it when it is already running is a no-op.
                if (_settings.Settings.AutoStart && dev.IsConnected && !IsCoreRunning)
                {
                    StatusText = Strings.Current["Manette détectée"];
                    StartCoreCommand.Execute(null);
                }

                // The configuration window no longer starts the bridge on its own. SenSÉ.Desktop
                // owns that lifecycle now: it starts the core with the environment and stops it when
                // the environment closes. Two owners meant two launches, each passing --restart and
                // killing the other's instance in a loop.
                //
                // The Start and Stop buttons on this screen still work: starting it by hand is a
                // deliberate act, and the single-instance guard in the core makes it safe.

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

        // App already loaded the profiles. Loading them again here rebuilt the collection while
        // ProfileService.ActiveProfile kept pointing at an object that was no longer in it, so the
        // editor's list had nothing selected.
        AutoStart = _settings.Settings.AutoStart;

        // The bridge is very often already running when this window opens: the environment starts it
        // at boot, and the Controller tile only opens a view onto something that has been working
        // for a while. Assuming it stopped meant the button read "Démarrer" over a running bridge —
        // so there was no way to stop it at all, and pressing Start restarted what was already fine.
        //
        // Adopted rather than merely displayed: the service takes ownership of the process it finds,
        // so Stop acts on it.
        if (_core.AdoptRunningInstance())
        {
            IsCoreRunning = true;
            StatusText = Strings.Current["En cours"];
        }

        var lastProfile = _settings.Settings.LastActiveProfile;
        var match = _profileService.Profiles.FirstOrDefault(p => p.Name == lastProfile);
        SelectedProfile = match ?? _profileService.ActiveProfile;
        CurrentMode = SelectedProfile?.Mode ?? "Profile";

        // Only now: a controller that is already plugged in fires immediately, and with auto-start on
        // that would launch the runtime before the saved profile had been restored — starting the
        // previous session's controller configuration under whichever profile happened to be first.
        // Clamped here too: the runtime must not inherit a pathological interval from a settings file
        // written by an older build, where the slider allowed up to 30 seconds.
        _device.StartPolling(Math.Clamp(
            _settings.Settings.DevicePollIntervalMs,
            AppSettings.MinPollSeconds * 1000,
            AppSettings.MaxPollSeconds * 1000));
    }

    [RelayCommand]
    private void StartCore()
    {
        if (_core.IsRunning) return;

        // Tue tous les Core.exe orphelins (quel que soit le chemin)
        foreach (var p in Process.GetProcessesByName("SenSÉ.Core"))
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
    }

    [RelayCommand]
    private void StopCore()
    {
        _core.Stop();

        // The overlay keyboard as well as the bridge. It is started by the core, outlives it, and
        // owns a topmost window: left running after a stop, it stays on screen with nothing feeding
        // it, which reads as SenSÉ having half-quit. Asked through the exit signal it already
        // watches, so it can put down any latched modifier key before going.
        StopOverlayKeyboard();

        IsCoreRunning = false;
        StatusText = Strings.Current["Arrêté"];
    }

    /// <summary>Asks the overlay keyboard to close, then kills it if it will not.</summary>
    private static void StopOverlayKeyboard()
    {
        try
        {
            System.IO.File.WriteAllText(
                CheminDebug.Signal("osk-exit"),
                DateTime.UtcNow.Ticks.ToString());

            foreach (var process in System.Diagnostics.Process.GetProcessesByName("SenSÉ.Osk"))
            {
                using (process)
                {
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SenSÉ.Core.Diagnostics.UiLog.Failure("stopping the overlay keyboard", ex);
        }
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
        _core.Dispose();
        _device.Dispose();
    }
}
