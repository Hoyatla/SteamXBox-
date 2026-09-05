using System.IO;
using System.Windows;
using System.Windows.Markup;
using SenSÉ.Gui.Services;
using SenSÉ.Gui.ViewModels;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Gui;

public partial class App : Application
{
    public static ProfileService ProfileSvc { get; private set; } = null!;

    /// <summary>
    /// The one settings instance for the whole application.
    /// </summary>
    /// <remarks>
    /// Each view model used to build its own, and every <c>Save()</c> rewrites the entire file from
    /// that instance's in-memory copy. Whichever tab saved last silently reverted the others: picking
    /// a profile then touching anything in Settings put <c>lastActiveProfile</c> back to whatever it
    /// was at launch, and ticking "start with Windows" was undone the same way.
    /// </remarks>
    public static SettingsService SettingsSvc { get; private set; } = null!;

    public static MainViewModel MainVm { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UiLog.Start("gui");
        UiLog.Info($"arguments: {(e.Args.Length == 0 ? "(none)" : string.Join(' ', e.Args))}");

        // Both channels, because they fail differently. An exception on the UI thread is survivable
        // and is recorded; one on a worker thread ends the process outright, which looks from the
        // outside like the window vanishing for no reason — and is the harder of the two to report.
        DispatcherUnhandledException += (_, args) =>
        {
            UiLog.Crash("unhandled exception on the dispatcher", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                UiLog.Crash("unhandled exception on a background thread", ex);
            }

            UiLog.Stop("process terminating on an unhandled exception");
        };

        SettingsSvc = new SettingsService();

        // Before any window is built, so the first render is already in the right language.
        try
        {
            SettingsSvc.Load();
            SenSÉ.Shell.Localization.Strings.Current.Apply(SettingsSvc.Settings.Language);
        }
        catch (Exception ex)
        {
            UiLog.Failure("loading the settings", ex);
            SenSÉ.Shell.Localization.Strings.Current.Apply(SenSÉ.Shell.Localization.AppLanguage.System);
        }

        // After the settings, not before. This used to run first, while SettingsSvc was still null,
        // so the selected theme resolved to nothing and only a loose skin.xaml was ever picked up:
        // the configuration window sat on the built-in theme while Desktop wore the chosen one.
        // Same call as Desktop makes, so the two cannot drift apart again.
        _externalSkin = SenSÉ.Shell.Theming.ThemeLoader.Apply(this, SettingsSvc.Settings.Theme);


        // Sans argument, SenSÉ.exe n'est qu'un lanceur : il ouvre l'environnement et s'efface.
        // C'est Desktop qui est la racine du produit, et double-cliquer sur SenSÉ doit ouvrir
        // le produit, pas son panneau de configuration manette. La tuile Controller de Desktop
        // rappelle ce même exécutable avec --config pour obtenir cette fenêtre.
        if (!e.Args.Any(a => a.Equals("--config", StringComparison.OrdinalIgnoreCase)))
        {
            UiLog.Info("launcher mode: opening the environment and standing down");
            LaunchDesktop();

            // The environment brings the bridge up, so an attached controller is taken in charge on
            // its own. Nothing opens the configuration window here, deliberately: "start
            // automatically when the controller is detected" is about the controller working, not
            // about a window appearing. A settings window that opens by itself on every boot is a
            // nuisance, and I briefly made it do exactly that by misreading the request.
            if (SettingsSvc.Settings.AutoStart)
            {
                UiLog.Info($"auto-start is on; {AttachedControllerCount()} controller(s) attached");
            }

            Shutdown();
            return;
        }

        UiLog.Info("configuration mode: opening the controller settings window");

        // One configuration window, ever. The Controller tile calls this executable again with
        // --config each time it is clicked, and nothing stopped a second window from opening on top
        // of the first — two views of the same settings file, each holding its own copy in memory,
        // where whichever saved last silently reverted the other.
        _configInstance = new System.Threading.Mutex(true, @"Global\SenSÉ.Gui.Config", out var isFirst);

        if (!isFirst)
        {
            UiLog.Info("a configuration window is already open; bringing it to the front");
            BringExistingConfigWindowToFront();
            Shutdown();
            return;
        }

        ProfileSvc = new ProfileService();
        ProfileSvc.LoadAll();
        UiLog.Info($"{ProfileSvc.Profiles.Count} profile(s) loaded");

        MainVm = new MainViewModel();

        ShowMainWindow();
    }

    /// <summary>
    /// Builds and shows the main window, dropping the external skin and retrying once if it throws.
    /// </summary>
    /// <remarks>
    /// Loading a skin cannot validate it. A dictionary parses happily while still holding a resource
    /// of the wrong type — a Color where a Brush is expected, say — and that only throws when a
    /// window actually resolves it. Under StartupUri that exception was unhandled and the process
    /// died with no window and no message, which is precisely how a bad skin presented itself.
    /// A skin is cosmetic; it must never be able to stop the application from running.
    /// </remarks>
    private void ShowMainWindow()
    {
        try
        {
            new MainWindow().Show();
            UiLog.Window(nameof(MainWindow), "shown");
            return;
        }
        catch (Exception exception)
        {
            UiLog.Failure("building the main window with the external skin", exception);
            RemoveExternalSkin();
            SkinFailure = exception.Message;
        }

        // Second attempt on the built-in theme. If this throws too, the fault is not the skin and
        // the exception must surface rather than be swallowed.
        new MainWindow().Show();
        UiLog.Window(nameof(MainWindow), "shown", "on the built-in theme after the skin was rejected");
    }

    /// <summary>
    /// How many controllers are attached, across all three families.
    /// </summary>
    /// <remarks>
    /// All three, deliberately. A count that only saw Valve devices is what made the old status
    /// card read "déconnecté" with a DualSense in hand, and an auto-start that inherited the same
    /// blindness would never fire for anyone using a PlayStation or Xbox pad — the two the feature
    /// is most likely to be wanted for.
    /// </remarks>
    private static int AttachedControllerCount()
    {
        var count = 0;

        try
        {
            count += new SenSÉ.Hid.SteamHidDiscovery().ListValveDevices().Count;
        }
        catch (Exception ex)
        {
            UiLog.Failure("counting Valve devices", ex);
        }

        try
        {
            count += SenSÉ.Windows.XInputControllerSource.ConnectedSlots().Count;
        }
        catch (Exception ex)
        {
            UiLog.Failure("counting XInput slots", ex);
        }

        try
        {
            count += SenSÉ.Hid.DualSenseControllerSource.Discover().Count;
        }
        catch (Exception ex)
        {
            UiLog.Failure("counting DualSense devices", ex);
        }

        return count;
    }

    /// <summary>
    /// Opens the configuration window as a separate process.
    /// </summary>
    /// <remarks>
    /// A new process rather than a window in this one: this instance is the launcher and is about to
    /// shut down. The single-instance mutex on the configuration path means a window already open is
    /// raised instead of duplicated, so this is safe to call without checking first.
    /// </remarks>
    private static void LaunchConfigWindow()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is null)
            {
                return;
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(executable, "--config") { UseShellExecute = false });

            UiLog.Info("configuration window opened by auto-start");
        }
        catch (Exception ex)
        {
            UiLog.Failure("opening the configuration window on auto-start", ex);
        }
    }

    /// <summary>Held for the lifetime of the one configuration window.</summary>
    private static System.Threading.Mutex? _configInstance;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    /// <summary>
    /// Raises the configuration window that is already open.
    /// </summary>
    /// <remarks>
    /// Restored before being raised: a minimised window accepts the foreground request and stays
    /// minimised, so the click would appear to do nothing at all — which is worse than opening a
    /// second window, because at least that was visible.
    /// </remarks>
    private static void BringExistingConfigWindowToFront()
    {
        const int Restore = 9;

        try
        {
            var self = Environment.ProcessId;

            foreach (var other in System.Diagnostics.Process.GetProcessesByName("SenSÉ"))
            {
                using (other)
                {
                    if (other.Id == self || other.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(other.MainWindowHandle, Restore);
                    SetForegroundWindow(other.MainWindowHandle);
                    return;
                }
            }

            UiLog.Warn("no existing configuration window found to raise");
        }
        catch (Exception ex)
        {
            UiLog.Failure("raising the existing configuration window", ex);
        }
    }

    /// <summary>Why the external skin was rejected, or null when none was.</summary>
    public static string? SkinFailure { get; private set; }

    private static void RemoveExternalSkin()
    {
        SenSÉ.Shell.Theming.ThemeLoader.Remove(Current, _externalSkin);
        _externalSkin = null;
    }

    private static ResourceDictionary? _externalSkin;

    /// <summary>
    /// Starts SenSÉ Desktop, or brings it to the front if it is already up.
    /// </summary>
    /// <remarks>
    /// Quiet on failure by design: this runs before any window exists, so a message box would be the
    /// only thing on screen and would say nothing the user can act on. A missing
    /// SenSÉ.Desktop.exe next to this one means a broken installation, not a mistake to warn
    /// about mid-launch.
    ///
    /// Quiet on screen, not quiet in the record. This is the whole of what double-clicking
    /// SenSÉ.exe does, so when nothing appears there is nothing else to consult — and "I
    /// double-clicked and saw nothing" was left as the only available description of a failure that
    /// has four distinct causes, each written below.
    /// </remarks>
    private static void LaunchDesktop()
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("SenSÉ.Desktop").Length > 0)
            {
                UiLog.Process("SenSÉ.Desktop", "already running; not started again");
                return;
            }

            var executable = Path.Combine(AppContext.BaseDirectory, "SenSÉ.Desktop.exe");
            if (!File.Exists(executable))
            {
                UiLog.Error($"process SenSÉ.Desktop: executable not found at {executable}");
                return;
            }

            var started = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false });

            UiLog.Process(
                "SenSÉ.Desktop",
                started is null ? "start returned no process" : $"started, PID {started.Id}");
        }
        catch (Exception ex)
        {
            UiLog.Failure("starting SenSÉ.Desktop", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainVm?.Dispose();
        UiLog.Stop($"exited with code {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
