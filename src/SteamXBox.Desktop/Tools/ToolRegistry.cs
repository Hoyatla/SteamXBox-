using System.Diagnostics;
using System.IO;
using System.Windows;
using SteamXBox.Desktop.Capture;
using SteamXBox.Desktop.ControlCentre;
using SteamXBox.Shell.Localization;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// The tools SteamXBox provides, as opposed to the shortcuts it offers into Windows.
/// </summary>
/// <remarks>
/// The distinction matters and is the reason this list exists separately. A shortcut opens a
/// Windows panel and will always be a link; a tool is something SteamXBox owns, could version, and
/// will one day ship as its own process. Only tools appear here.
/// </remarks>
public static class ToolRegistry
{
    public static IReadOnlyList<ToolDescriptor> All { get; } =
    [
        new("controller", "Controller", "Manette : reglages et profils", Glyphs.Controller,
            Executable: "SteamXBox.exe", Arguments: "--config"),

        new("calculator", "Calculatrice", "Calculatrice scientifique de SteamXBox", Glyphs.Calculator,
            Run: _ => OpenOnce<CalculatorWindow>(() => new CalculatorWindow())),

        new("capture", "Capture", "Capturer une zone de l'ecran", Glyphs.Snip,
            Run: CaptureAction.Run),

        new("clipboard", "Presse-papiers", "Historique des copies", Glyphs.Clipboard,
            Run: _ => OpenOnce<Clipboard.ClipboardWindow>(() => new Clipboard.ClipboardWindow()),
            Start: Clipboard.ClipboardService.Start),

        new("settings", "Parametres SteamXBox", "Preferences, journaux et diagnostic", Glyphs.Settings,
            Run: _ => OpenOnce<Settings.SteamXBoxSettingsWindow>(() => new Settings.SteamXBoxSettingsWindow())),
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

            Process.Start(new ProcessStartInfo(path, tool.Arguments) { UseShellExecute = false });
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
