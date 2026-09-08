using System.Diagnostics;
using System.IO;
using System.Windows;
using SenSÉ.Desktop.Capture;
using SenSÉ.Desktop.ControlCentre;
using SenSÉ.Shell.Localization;

namespace SenSÉ.Desktop.Tools;

/// <summary>
/// The tools SenSÉ provides, as opposed to the shortcuts it offers into Windows.
/// </summary>
/// <remarks>
/// The distinction matters and is the reason this list exists separately. A shortcut opens a
/// Windows panel and will always be a link; a tool is something SenSÉ owns, could version, and
/// will one day ship as its own process. Only tools appear here.
/// </remarks>
public static class ToolRegistry
{
    /// <summary>
    /// The compiled tools and the ones found on disk, in one list.
    /// </summary>
    /// <remarks>
    /// Deliberately one list. The contract's own measure of the loader is that a tool from a
    /// <c>plugin.json</c> produces the same tile as the compiled ones — so they arrive as the same
    /// record, and everything downstream is unable to tell them apart.
    ///
    /// <para>
    /// Loaded once, at first use. Re-reading the folder on every access would walk the disk each
    /// time the grid is drawn; a tool dropped in while SenSÉ runs appears on the next start,
    /// which is what "dropped in, it is installed" has always meant for the rest of the product.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ToolDescriptor> All => _all ??=
    [
        // The compiled tools obey the same switch as the loaded ones. A user who turns the
        // calculator off in the uninstall screen means the tile to go, and it would be a strange
        // product where that worked for the tools written last week and not for the ones that
        // shipped first.
        //
        // The system entries ignore it entirely — even a stale line in the state file cannot hide
        // them. Without that, one bad entry would remove the settings tile and with it the only way
        // back to the screen that would restore it.
        .. Compiled.Where(tool => tool.IsSystem
                                  || SenSÉ.Plugins.PluginLifecycle.IsEnabled(tool.Id, byDefault: true)),
        .. PluginTools.Load(_log),
    ];

    /// <summary>
    /// The compiled tools that are tools, for the screen that lists them.
    /// </summary>
    /// <remarks>
    /// Without the system entries. The controller configuration and the settings window are what
    /// SenSÉ is, not accessories it can do without, and offering to switch them off is offering
    /// the user a way to lock themselves out.
    /// </remarks>
    public static IReadOnlyList<ToolDescriptor> Builtin => [.. Compiled.Where(tool => !tool.IsSystem)];

    /// <summary>Whether an identifier names part of SenSÉ rather than a tool.</summary>
    public static bool IsSystem(string id)
        => Compiled.Any(tool => tool.IsSystem && tool.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ToolDescriptor>? _all;

    /// <summary>
    /// Oublie la liste retenue, pour qu'elle soit relue au prochain accès.
    /// </summary>
    /// <remarks>
    /// La liste est gardée parce que la relire à chaque dessin de la grille coûterait un balayage de
    /// dossier par image. Mais « une fois par processus » voulait dire qu'un outil déposé pendant que
    /// SenSÉ tourne n'apparaissait qu'au redémarrage suivant — ce qui contredit « un outil est un
    /// dossier : déposé, il est installé ». La veille appelle ceci quand les dossiers ont bougé.
    /// </remarks>
    public static void Oublier() => _all = null;
    private static Action<string>? _log;

    /// <summary>Gives the loader somewhere to report, before anything reads <see cref="All"/>.</summary>
    public static void LogTo(Action<string> log) => _log = log;

    private static IReadOnlyList<ToolDescriptor> Compiled { get; } =
    [
        new("controller", "Controller", "Manette : reglages et profils", Glyphs.Controller,
            Executable: "SenSÉ.exe", Arguments: "--config", IsSystem: true),

        new("calculator", "Calculatrice", "Calculatrice scientifique de SenSÉ", Glyphs.Calculator,
            Run: _ => OpenOnce<CalculatorWindow>(() => new CalculatorWindow())),

        new("capture", "Capture", "Capturer une zone de l'ecran", Glyphs.Snip,
            Run: CaptureAction.Run),

        new("clipboard", "Presse-papiers", "Historique des copies", Glyphs.Clipboard,
            Run: _ => OpenOnce<Clipboard.ClipboardWindow>(() => new Clipboard.ClipboardWindow()),
            Start: Clipboard.ClipboardService.Start),

        new("settings", "Parametres SenSÉ", "Preferences, journaux et diagnostic", Glyphs.Settings,
            Run: _ => OpenOnce<Settings.SenSÉSettingsWindow>(() => new Settings.SenSÉSettingsWindow()),
            IsSystem: true),
    ];

    /// <summary>Starts every tool that needs to be running before it is opened.</summary>
    public static void StartServices()
    {
        foreach (var tool in All)
        {
            tool.Start?.Invoke();
        }
    }

    /// <summary>Runs a tool, in this process or as its own, and returns a line for the status area.</summary>
    public static string Launch(ToolDescriptor tool, Window environment)
    {
        if (tool.Run is not null)
        {
            return tool.Run(environment);
        }

        if (tool.Executable.Length == 0)
        {
            return "";
        }

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, tool.Executable);
            if (!File.Exists(path))
            {
                return Strings.Current.Format("{0} est introuvable.", tool.Executable);
            }

            var procR = Process.Start(new ProcessStartInfo(path, tool.Arguments) { UseShellExecute = false });
            JobEnfants.Inscrire(procR);
            return "";
        }
        catch (Exception exception)
        {
            return $"{tool.Label} : {exception.Message}";
        }
    }

    /// <summary>
    /// Shows a tool window, or brings the existing one back rather than opening a second.
    /// </summary>
    /// <remarks>
    /// Every in-process tool wants this, so it lives here instead of being repeated in each one.
    /// Not owned by the environment window: an owned window is dragged in front of whatever the
    /// user was working in when the overlay is activated.
    /// </remarks>
    private static string OpenOnce<T>(Func<T> create) where T : Window
    {
        var existing = Application.Current.Windows.OfType<T>().FirstOrDefault();
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return "";
        }

        create().Show();
        return "";
    }
}
