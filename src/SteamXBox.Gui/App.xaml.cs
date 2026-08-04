using System.IO;
using System.Windows;
using System.Windows.Markup;
using SteamXBox.Gui.Services;
using SteamXBox.Gui.ViewModels;
using Sc2Xboxed.Core.Diagnostics;

namespace SteamXBox.Gui;

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
            SteamXBox.Shell.Localization.Strings.Current.Apply(SettingsSvc.Settings.Language);
        }
        catch (Exception ex)
        {
            UiLog.Failure("loading the settings", ex);
            SteamXBox.Shell.Localization.Strings.Current.Apply(SteamXBox.Shell.Localization.AppLanguage.System);
        }

        // After the settings, not before. This used to run first, while SettingsSvc was still null,
        // so the selected theme resolved to nothing and only a loose skin.xaml was ever picked up:
        // the configuration window sat on the built-in theme while Desktop wore the chosen one.
        // Same call as Desktop makes, so the two cannot drift apart again.
        _externalSkin = SteamXBox.Shell.Theming.ThemeLoader.Apply(this, SettingsSvc.Settings.Theme);


        // Sans argument, SteamXBox.exe n'est qu'un lanceur : il ouvre l'environnement et s'efface.
        // C'est Desktop qui est la racine du produit, et double-cliquer sur SteamXBox doit ouvrir
        // le produit, pas son panneau de configuration manette. La tuile Controller de Desktop
        // rappelle ce même exécutable avec --config pour obtenir cette fenêtre.
        if (!e.Args.Any(a => a.Equals("--config", StringComparison.OrdinalIgnoreCase)))
        {
            UiLog.Info("launcher mode: opening the environment and standing down");
            LaunchDesktop();
            Shutdown();
            return;
        }

        UiLog.Info("configuration mode: opening the controller settings window");

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

    /// <summary>Why the external skin was rejected, or null when none was.</summary>
    public static string? SkinFailure { get; private set; }

    private static void RemoveExternalSkin()
    {
        SteamXBox.Shell.Theming.ThemeLoader.Remove(Current, _externalSkin);
        _externalSkin = null;
    }

    private static ResourceDictionary? _externalSkin;

    /// <summary>
    /// Starts SteamXBox Desktop, or brings it to the front if it is already up.
    /// </summary>
    /// <remarks>
    /// Quiet on failure by design: this runs before any window exists, so a message box would be the
    /// only thing on screen and would say nothing the user can act on. A missing
    /// SteamXBox.Desktop.exe next to this one means a broken installation, not a mistake to warn
    /// about mid-launch.
    ///
    /// Quiet on screen, not quiet in the record. This is the whole of what double-clicking
    /// SteamXBox.exe does, so when nothing appears there is nothing else to consult — and "I
    /// double-clicked and saw nothing" was left as the only available description of a failure that
    /// has four distinct causes, each written below.
    /// </remarks>
    private static void LaunchDesktop()
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("SteamXBox.Desktop").Length > 0)
            {
                UiLog.Process("SteamXBox.Desktop", "already running; not started again");
                return;
            }

            var executable = Path.Combine(AppContext.BaseDirectory, "SteamXBox.Desktop.exe");
            if (!File.Exists(executable))
            {
                UiLog.Error($"process SteamXBox.Desktop: executable not found at {executable}");
                return;
            }

            var started = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false });

            UiLog.Process(
                "SteamXBox.Desktop",
                started is null ? "start returned no process" : $"started, PID {started.Id}");
        }
        catch (Exception ex)
        {
            UiLog.Failure("starting SteamXBox.Desktop", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainVm?.Dispose();
        UiLog.Stop($"exited with code {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
