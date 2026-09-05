using System.Windows;
using System.Windows.Threading;
using SenSÉ.Shell.Configuration;
using SenSÉ.Core.Diagnostics;
using SenSÉ.Mcp.Bus;
using SenSÉ.Desktop.Observateurs;
using SenSÉ.Shell.Localization;
using SenSÉ.Shell.Theming;

namespace SenSÉ.Desktop;

/// <summary>
/// The SenSÉ environment.
/// </summary>
/// <remarks>
/// Its own process, separate from the controller bridge and from its configuration window: what
/// gets built here has nothing to do with configuring a gamepad, and must not start, stop or be
/// reasoned about together with it.
///
/// It reads the same settings file as the GUI, so the theme and the language chosen there apply to
/// the whole environment without anything having to be synchronised.
/// </remarks>
public partial class App : Application
{
    public static SettingsService SettingsSvc { get; private set; } = null!;

    /// <summary>Where an unhandled exception is written, next to the executable.</summary>
    private static string CrashLog => System.IO.Path.Combine(AppContext.BaseDirectory, "SenSÉ-desktop-crash.log");

    /// <summary>
    /// Records an unhandled exception instead of letting the environment vanish.
    /// </summary>
    /// <remarks>
    /// Closing this window shuts down the controller bridge and the overlay keyboard, so an
    /// exception nobody catches does not look like one failing feature — it looks like all of
    /// SenSÉ disappearing at once, with nothing in the Windows event log because the process
    /// exits rather than faults. This turns that into a file and a message.
    /// </remarks>
    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UiLog.Crash("unhandled exception on the dispatcher", e.Exception);

        try
        {
            System.IO.File.AppendAllText(CrashLog,
                $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            // Nothing further to try; the session log and the message box are the remaining channels.
            UiLog.Failure("writing the crash log", ex);
        }

        MessageBox.Show(
            $"{e.Exception.GetType().Name}\n\n{e.Exception.Message}\n\n{CrashLog}",
            "SenSÉ Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);

        // Handled: one failure must not take the environment, the bridge and the keyboard down.
        e.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Opened before anything else can fail, so that everything below leaves a trace.
        UiLog.Start("desktop");

        // The dispatcher handler only covers the UI thread. An exception on a worker thread ends the
        // process outright, which from the screen looks exactly like SenSÉ vanishing for no
        // reason — the single hardest thing to report and the one that most needs recording.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                UiLog.Crash("unhandled exception on a background thread", ex);
            }

            UiLog.Stop("process terminating on an unhandled exception");
        };

        SettingsSvc = new SettingsService();
        try
        {
            SettingsSvc.Load();
        }
        catch (Exception ex)
        {
            // A missing or corrupt settings file is not a reason to refuse to start: the defaults
            // are usable, and the GUI is where it gets repaired.
            UiLog.Failure("loading the settings", ex);
        }

        Strings.Current.Apply(SettingsSvc.Settings.Language);
        UiLog.Info($"language={SettingsSvc.Settings.Language}, theme={SettingsSvc.Settings.Theme}");

        // The controller bridge comes up with the environment, headless. The Controller tile is
        // where it becomes visible; closing this window takes it back down.
        SenSÉProcesses.StartCore();

        try
        {
            // Before the tools, because a screen left cleared by a session that died is the first
            // thing the user is looking at — and the only thing that knows about it is a marker on
            // disk that nothing else reads.
            Input.DesktopWindows.RepairOnStart(message => UiLog.Info(message));

            Tools.ToolRegistry.LogTo(message => UiLog.Info(message));
            Tools.ToolRegistry.StartServices();
            UiLog.Info("tool services started");


            // Bus d'evenements pour la proactivite. In-process, thread-safe, demarre une fois
            // pour toute la duree de l'environnement. L'Assistant s'y branche quand sa fenetre
            // s'ouvre, et l'observateur de projets y pousse les changements de fichiers.
            EventBus = new SenSÉ.Mcp.Bus.EventBus();
            _runnerProactif = new ProactifRunner(EventBus, message => UiLog.Info(message));
            _runnerProactif.Demarrer();

            // Observateur de demo : surveille Outils\Projets\ et pousse les changements de
            // fichiers sur le bus. Le premier observateur reel, a etendre ensuite avec
            // mbox, processus, scheduler.
            try
            {
                var racineProjets = System.IO.Path.Combine(AppContext.BaseDirectory, "Outils", "Projets");
                _watcherProjets = new ProjetsWatcher(racineProjets, EventBus, message => UiLog.Info(message));
                _watcherProjets.Demarrer();
            }
            catch (Exception ex)
            {
                UiLog.Failure("demarrage du ProjetsWatcher", ex);
            }

            // Le générateur est décrit par les réglages, pas par des constantes semées dans le code.
            // Port et « installation à part » sont lus une fois, ici, et tout le reste du produit
            // les demande à ComfyServer — il n'y a plus qu'un endroit où ce nombre existe.
            SenSÉ.Tools.Generation.ComfyServer.Port = SettingsSvc.Settings.GenerateurPort;

            // Externe dès que ComfyUI Desktop est là, sans qu'on ait à le régler.
            //
            // La dépendance le déclare, l'hôte constate — la règle habituelle. Desktop est une
            // application que l'utilisateur installe, ouvre et garde ouverte : la démarrer serait
            // en ouvrir une seconde, et la tuer à notre fermeture serait fermer la fenêtre de
            // quelqu'un d'autre. Le réglage reste là pour le cas inverse, une installation à part
            // que rien ne permet de deviner.
            SenSÉ.Tools.Generation.ComfyServer.Externe =
                SettingsSvc.Settings.GenerateurExterne || Tools.PluginTools.Presente("dependance-comfyui-desktop");

            UiLog.Info(
                $"générateur : port {SettingsSvc.Settings.GenerateurPort}"
                + (SettingsSvc.Settings.GenerateurExterne ? ", installation à part" : ", lancé par le produit"));

            PrechaufferGenerateur();

            // Les carnets qu'on n'a pas rouverts depuis un mois s'effacent au démarrage. Sans cela
            // un dossier de travaux à moitié faits s'accumulerait indéfiniment, et un produit qui
            // laisse des traces qu'il ne nettoie pas finit par en être jugé.
            var oublies = SenSÉ.Tools.Assistant.FichierTravail.Purger(
                message => UiLog.Info(message));

            if (oublies > 0)
            {
                UiLog.Info($"{oublies} carnet(s) abandonné(s) effacé(s).");
            }

            StartGlobalHotkeys();
        }
        catch (Exception ex)
        {
            UiLog.Failure("starting the tool services", ex);
        }

        var skin = ThemeLoader.Apply(this, SettingsSvc.Settings.Theme);
        UiLog.Info($"theme '{SettingsSvc.Settings.Theme}' applied: {ThemeLoader.LastOutcome}");

        // A skin only fails when a window resolves it, so the first window is built inside a try and
        // the skin is dropped if it throws. A theme is cosmetic; it must never stop the environment
        // from running.
        try
        {
            ShowShell();
            return;
        }
        catch (Exception ex)
        {
            UiLog.Failure($"building the first window with the '{SettingsSvc.Settings.Theme}' skin", ex);
            ThemeLoader.Remove(this, skin);
        }

        UiLog.Warn("retrying without the skin");
        ShowShell();
    }

    /// <summary>
    /// Restarts the environment so a newly chosen theme takes effect everywhere.
    /// </summary>
    /// <remarks>
    /// Merging the new dictionary at runtime is not enough and would be worse than doing nothing.
    /// Most styles resolve their colours through <c>StaticResource</c>, which is looked up once when
    /// the window is built; only the handful using <c>DynamicResource</c> would change. The result
    /// is an interface repainted by halves, which reads as a bug rather than as a theme.
    ///
    /// Restarting is honest and complete. This is a launcher overlay, not a document editor: there
    /// is nothing to lose and it comes back in under a second.
    /// </remarks>
    public static void RestartForTheme()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is null)
            {
                return;
            }

            // Started before this process exits, so the desktop is never left uncovered.
            _restarting = true;
            UiLog.Info("restarting for a theme change; the bridge and the keyboard must survive");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            _restarting = false;
            UiLog.Failure("restarting for a theme change", ex);
            return;
        }

        Current.Shutdown();
    }

    /// <summary>
    /// True while the environment is restarting itself rather than being closed.
    /// </summary>
    /// <remarks>
    /// Without it, <see cref="OnExit"/> would take the controller bridge and the overlay keyboard
    /// down with it — a theme change would unplug the user's controller.
    /// </remarks>
    private static bool _restarting;

    /// <summary>Shows the environment's window.</summary>
    /// <remarks>
    /// <see cref="ShutdownMode.OnExplicitShutdown"/>, deliberately. Under any other mode a window
    /// closing can end the process, and this application closes windows constantly: the calculator,
    /// the settings, the capture selector. Worse, the environment also hides itself for the actions
    /// that need the foreground — and with the wrong mode that behaves like a quit.
    ///
    /// Since the environment shutting down also takes the controller bridge and the overlay
    /// keyboard with it, that turned "close a feature" into "close all of SenSÉ". Nothing may
    /// end this process except the user asking for it, which is <see cref="Quit"/>.
    /// </remarks>
    private Search.SearchLauncherWindow? _searchLauncher;
    private Input.DoubleTapHotkey? _hotkeys;
    private Input.DesktopSignalWatcher? _signals;

    /// <summary>Runs what a controller asked for, whichever button it came from.</summary>
    /// <remarks>
    /// Deliberately the same calls the keyboard gestures make, not a parallel implementation. A
    /// controller path that drifted from the keyboard path is how one of them ends up fixed and the
    /// other forgotten.
    /// </remarks>
    private void RunControllerRequest(string signal)
    {
        if (signal == SenSÉ.Core.Runtime.DesktopSignal.Search)
        {
            _searchLauncher?.Toggle();
            return;
        }

        if (signal == SenSÉ.Core.Runtime.DesktopSignal.ClearScreen)
        {
            Input.DesktopWindows.Toggle(message => UiLog.Info(message));
        }
    }

    /// <summary>
    /// Puts the session's double-tap shortcuts in place: Shift for the launcher, the section key to
    /// clear the screen.
    /// </summary>
    /// <remarks>
    /// The launcher window is built now and hidden, not built on each summon: it is the index that is
    /// expensive, and a launcher that takes a second to appear is one nobody uses. The index itself
    /// is not built here — it waits for the first summon, so starting the environment does not walk
    /// every drive while the user is trying to launch something.
    ///
    /// <para>
    /// Both gestures share one keyboard hook, which is why they are registered together. A low-level
    /// hook is in the path of every keystroke on the machine, so the cost is per hook and not per
    /// shortcut.
    /// </para>
    ///
    /// <para>
    /// A failure here costs the shortcuts and nothing else. The environment starts either way, and
    /// the log says which it was.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Démarre le générateur au lancement, si le réglage le demande.
    /// </summary>
    /// <remarks>
    /// <b>Éteint par défaut, et le rester est la décision.</b> Le générateur est le plus gros
    /// consommateur du produit : l'allumer sans qu'on l'ait demandé, c'est un processus Python et sa
    /// mémoire dès le démarrage, sur une machine qui n'ouvrira peut-être jamais cet outil de la
    /// journée. Le registre des ressources existe précisément pour que rien ne tourne sans raison,
    /// et il serait absurde de le contredire ici.
    ///
    /// <para>
    /// Ce que le réglage achète quand on le met : le produit vit sur un disque externe, et le
    /// premier démarrage du générateur d'une session coûte environ deux minutes — le temps d'ouvrir
    /// un par un les soixante-douze mille fichiers de Python. Allumé, ce temps se paie pendant
    /// qu'on fait autre chose.
    /// </para>
    ///
    /// <para>
    /// Sur un fil à part, toujours : deux minutes sur le fil de démarrage retarderaient l'apparition
    /// de l'environnement d'autant, et le produit paraîtrait mort au lancement.
    /// </para>
    /// </remarks>
    private static void PrechaufferGenerateur()
    {
        if (!SettingsSvc.Settings.PrechaufferGenerateur)
        {
            return;
        }

        if (!SenSÉ.Tools.Generation.ComfyServer.Installe)
        {
            UiLog.Info("préchauffage demandé, mais le générateur n'est pas installé.");

            return;
        }

        UiLog.Info("préchauffage du générateur au démarrage.");

        Task.Run(() => SenSÉ.Tools.Generation.ComfyServer.Preparer(message => UiLog.Info(message)));
    }

    private void StartGlobalHotkeys()
    {
        try
        {
            _searchLauncher = new Search.SearchLauncherWindow(message => UiLog.Info(message));

            // Looked up on this machine's layout rather than written down: the section key has a
            // different code on every keyboard, and none at all on some.
            var section = Input.KeyboardLayout.VirtualKeyFor('§', message => UiLog.Info(message));

            _hotkeys = new Input.DoubleTapHotkey(
                [
                    new Input.DoubleTapGesture(
                        "double-Shift → launcher",
                        Input.DoubleTapHotkey.ShiftKeys,
                        () => _searchLauncher?.Toggle()),

                    new Input.DoubleTapGesture(
                        "double-§ → clear the screen, and put it back",
                        Input.DoubleTapHotkey.Key(section),
                        () => Input.DesktopWindows.Toggle(message => UiLog.Info(message))),
                ],
                message => UiLog.Info(message));

            _hotkeys.Start();

            // The same two actions, reached from a controller. Profile mode binds A and Menu to
            // them, and the runtime asks for them from its own process.
            _signals = new Input.DesktopSignalWatcher(RunControllerRequest, message => UiLog.Info(message));
            _signals.Start();
        }
        catch (Exception exception)
        {
            UiLog.Info($"global hotkeys unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ShowShell()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        UiLog.Window(nameof(MainWindow), "shown", "environment overlay");
    }

    /// <summary>
    /// Ends the environment and everything that belongs to it.
    /// </summary>
    /// <remarks>
    /// The single deliberate exit: the ✕ on the overlay. Nothing else calls it, so no failure and
    /// no window closing anywhere can shut SenSÉ down by accident.
    /// </remarks>
    public static void Quit()
    {
        // Idempotent: Shutdown closes the overlay, whose Closed handler calls back here. Without the
        // guard that is an infinite recursion, and StopAll would run twice.
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        UiLog.Info("quit requested by the user; stopping everything that belongs to SenSÉ");
        SenSÉProcesses.StopAll();
        Current.Shutdown();
    }

    private static bool _quitting;

    protected override void OnExit(ExitEventArgs e)
    {
        // Before anything else, and on every path out including a theme restart. A keyboard hook
        // left installed by a process that is ending is a cost the whole machine keeps paying for
        // nothing — every keystroke of every application still goes through a callback that is no
        // longer there to answer.
        _hotkeys?.Dispose();
        _hotkeys = null;

        _signals?.Dispose();
        _signals = null;

        // Les serveurs que le produit a lancés meurent avec lui, sauf ceux qui ont demandé à
        // survivre. Sans cela ils restaient en vie avec la mémoire vidéo prise — dix gigaoctets et
        // demi mesurés après une fermeture — et l'utilisateur ne pouvait même pas les arrêter :
        // enfants d'un processus élevé, ils héritent de ses droits.
        var rendues = SenSÉ.Tools.Serveurs.Ressources.AuRevoir(message => UiLog.Info(message));

        if (rendues > 0)
        {
            UiLog.Info($"{rendues} ressource(s) arrêtée(s) à la fermeture.");
        }

        // Before the process goes: an environment that cleared the screen and then left would hand
        // the user an empty desktop with nothing left to undo it.
        Input.DesktopWindows.RestoreOnExit(message => UiLog.Info(message));

        // And let go of the shell object. Every activation of it makes objects inside explorer.exe
        // that outlive this process, which is measurable: explorer grew by eighty-nine handles per
        // session and never gave them back.
        Input.DesktopWindows.Release();

        // A safety net for the paths that bypass Quit — a session logoff, or Windows shutting down.
        // Skipped while restarting for a theme, where the bridge must survive.
        if (!_restarting)
        {
            SenSÉProcesses.StopAll();
        }

        UiLog.Stop(_restarting
            ? "restarting for a theme change"
            : _quitting ? "closed by the user" : $"exited with code {e.ApplicationExitCode}");

        base.OnExit(e);
    }

    /// <summary>Le bus d'evenements, partage par tous les observateurs et l'Assistant.</summary>
    public static SenSÉ.Mcp.Bus.EventBus? EventBus { get; private set; }

    private static ProactifRunner? _runnerProactif;
    private static ProjetsWatcher? _watcherProjets;
}