using System.Diagnostics;
using System.IO;
using System.Windows;
using SteamXBox.Plugins;
using SteamXBox.Tools.Documents;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// Turns the tools found on disk into tiles of the control centre.
/// </summary>
/// <remarks>
/// This is the loader the contract in <c>Plugins/README.md</c> was written for. Its measure of
/// success is stated there: it is right on the day it produces the same tile as the compiled
/// calculator and clipboard from a <c>plugin.json</c> alone, with nothing else in the environment
/// changing. It does — a loaded tool becomes a <see cref="ToolDescriptor"/>, the same record the
/// compiled ones are, and the grid cannot tell them apart.
///
/// <para>
/// <b>A tool is a folder.</b> Dropped in, it is installed; thrown away, it is uninstalled. Nothing
/// is compiled, nothing is registered elsewhere, and nothing is left behind — which is the only
/// definition of detachable that can be checked.
/// </para>
///
/// <para>
/// <b>No code runs from a manifest.</b> A tool names an action the host already performs for
/// itself; it cannot bring one. So a tool downloaded from anywhere can do no more than what its
/// manifest shows to whoever reads it, and reading it needs no programmer.
/// </para>
/// </remarks>
public static class PluginTools
{
    /// <summary>Where tools live, beside the executable.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Plugins");

    /// <summary>
    /// The tools SteamXBox ships with, which its own interface will not delete.
    /// </summary>
    /// <remarks>
    /// These are what a customer paid for. They can be switched off and they can be archived —
    /// both reversible, both the user's business — but the uninstall screen does not offer to
    /// destroy them. Somebody who really means it deletes the folder in the file manager, which is
    /// a deliberate act outside the product rather than one click inside it.
    ///
    /// <para>
    /// Known by the host rather than declared in a manifest, and that is the point. A field saying
    /// "I am a default tool" would be written by whoever wrote the manifest, so any downloaded tool
    /// could make itself undeletable from the screen meant to remove it.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> Shipped { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "convertir-document",
        "moniteur",
    };

    /// <summary>
    /// Every usable tool on disk, as tiles.
    /// </summary>
    /// <remarks>
    /// A tool that is missing, corrupt or whose manifest cannot be read is skipped with a line in
    /// the log — never an error that takes the environment or the other tools down with it. That is
    /// the second rule of the contract, and it is enforced here rather than trusted.
    /// </remarks>
    public static IReadOnlyList<ToolDescriptor> Load(Action<string>? log = null)
    {
        var scan = PluginCatalog.Scan(Folder);

        foreach (var rejection in scan.Rejected)
        {
            log?.Invoke($"plugin refused: {Path.GetFileName(rejection.Directory)}: {rejection.Reason}");
        }

        var tools = new List<ToolDescriptor>();

        foreach (var manifest in scan.Loaded.Where(m => m.Kind == PluginCategory.Tool))
        {
            // Switched off in the uninstall screen, or shipped idle and never switched on. The
            // folder is untouched either way — this is the difference between "not now" and "gone",
            // and the user should not have to move files to say which one they mean.
            if (!PluginLifecycle.IsEnabled(manifest.Id, manifest.Enabled))
            {
                log?.Invoke($"plugin tool skipped: {manifest.Id} is switched off.");
                continue;
            }

            tools.Add(new ToolDescriptor(
                manifest.Id,
                manifest.Name,
                manifest.Hint,
                manifest.Glyph,
                Run: environment => Run(manifest, environment, log)));

            log?.Invoke($"plugin tool loaded: {manifest.Id} v{manifest.Version} ({manifest.Surface}).");
        }

        return tools;
    }

    /// <summary>Runs a declarative tool, and returns a line for the status area.</summary>
    private static string Run(PluginManifest manifest, Window environment, Action<string>? log)
    {
        if (manifest.Surface.Equals("panel", StringComparison.OrdinalIgnoreCase))
        {
            return PluginPanelWindow.Open(manifest, log);
        }

        return Perform(manifest.Does, manifest.Target, log);
    }

    /// <summary>
    /// Carries out one named action.
    /// </summary>
    /// <remarks>
    /// The whole of what a level-one tool can cause to happen. Each of these is something the
    /// environment already does on its own tiles — which is the test the contract sets before an
    /// action may be published, and the reason this list is short and stays short.
    /// </remarks>
    public static string Perform(string does, string target, Action<string>? log)
    {
        try
        {
            switch (does.ToLowerInvariant())
            {
                case PluginActions.WindowsSetting:
                    Start($"ms-settings:{target}");
                    return "";

                case PluginActions.Application:
                    Start(target);
                    return "";

                case PluginActions.Path:
                    Start(target);
                    return "";

                case PluginActions.Search:
                    Sc2Xboxed.Core.Runtime.DesktopSignal.Raise(Sc2Xboxed.Core.Runtime.DesktopSignal.Search);
                    return "";

                case PluginActions.ClearScreen:
                    Input.DesktopWindows.Toggle(log);
                    return "";

                case PluginActions.Convert:
                    return Convert(target, log);

                default:
                    // Unreachable through the catalogue, which refuses an unknown action at load.
                    // Kept because an action removed from the vocabulary would otherwise fail here
                    // silently for anyone whose tool still names it.
                    log?.Invoke($"plugin action unknown at run time: '{does}'.");
                    return $"Action inconnue : {does}";
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin action '{does}' failed: {exception.GetType().Name}: {exception.Message}");
            return exception.Message;
        }
    }

    /// <summary>
    /// Converts a document the user designated, and shows where the result went.
    /// </summary>
    /// <remarks>
    /// The target is <c>path|format</c> — the document, then what to turn it into. Written beside
    /// the original rather than somewhere of the host's choosing: the user pointed at that folder,
    /// so it is the one place they already agreed to, and it is where they will look for the result.
    /// </remarks>
    private static string Convert(string target, Action<string>? log)
    {
        var parts = target.Split('|', 2);

        if (parts.Length < 2 || parts[0].Length == 0)
        {
            return "Choisissez un document et un format.";
        }

        var input = parts[0];
        var format = parts[1];

        var folder = Path.GetDirectoryName(input);

        if (string.IsNullOrEmpty(folder))
        {
            return "Le document choisi n'a pas de dossier.";
        }

        // A PDF takes the text route, never LibreOffice. Its PDF import goes through Draw and
        // rebuilds the page as a drawing: measured on one real document, ten thousand floating
        // objects for four hundred paragraphs, and Word took seconds per turn of the mouse wheel.
        // Taking the text instead loses the layout and gives a document that can actually be edited.
        // A slide is a fixed rectangle holding boxes at coordinates, which is what a PDF page is, so
        // this route keeps the layout instead of throwing it away. Same input, same reader, opposite
        // decision — because the target is different, not because one of them is better written.
        var result = PdfToPresentation.Handles(input, format)
            ? PdfToPresentation.Convert(input, format, folder)
            : PdfToDocument.Handles(input, format)
                ? PdfToDocument.Convert(input, format, folder)
                // Extraction rather than conversion, and the only route here that infers something
                // the file never held. Offered because the alternative was a spreadsheet holding one
                // enormous picture of the page.
                : PdfToTable.Handles(input, format)
                    ? PdfToTable.Convert(input, format, folder)
                    // A message never goes to LibreOffice either: it has no idea what one is, and
                    // the reader that does was already written for the index.
                    : MailToDocument.Handles(input, format)
                        ? MailToDocument.Convert(input, format, folder)
                        : LibreOffice.IsInstalled
                            ? LibreOffice.Convert(input, format, folder, log)
                            : new LibreOffice.Result(
                                "", "LibreOffice n'est pas installé : cette conversion a besoin de lui.");

        if (!result.Worked)
        {
            return result.Problem;
        }

        // Shown where it landed rather than opened. Opening would guess which application the user
        // wants, and the file may be one of several they are producing in a row.
        Search.ShellFolders.Reveal(result.Produced, log);

        return $"Écrit : {Path.GetFileName(result.Produced)}";
    }

    /// <summary>Hands something to the shell, the way the launcher does.</summary>
    private static void Start(string target)
        => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
