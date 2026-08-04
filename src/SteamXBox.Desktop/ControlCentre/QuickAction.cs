using System.Diagnostics;
using SteamXBox.Desktop.Tools;

namespace SteamXBox.Desktop.ControlCentre;

/// <summary>One tile of the control centre.</summary>
/// <param name="Glyph">Segoe Fluent Icons code point, from <see cref="Glyphs"/>.</param>
/// <param name="Label">Short label, in French, which doubles as the translation key.</param>
/// <param name="Hint">One line describing what the tile does, shown under the title on focus.</param>
/// <param name="YieldsForeground">
/// Whether the window must get out of the way before the action runs. True only for the actions
/// that send keystrokes: those go to whatever window is in front, and it must not be this one.
/// </param>
/// <param name="WithEnvironment">
/// Set instead of <paramref name="Invoke"/> for actions that need the environment window itself —
/// the capture has to hide and restore it. Typed rather than left as an empty Invoke that some
/// other file secretly intercepts. Returns a line for the status area.
/// </param>
public sealed record QuickAction(
    string Glyph,
    string Label,
    string Hint,
    Action Invoke,
    bool YieldsForeground = false,
    Func<System.Windows.Window, string>? WithEnvironment = null);

/// <summary>
/// What the control centre offers: SteamXBox's own tools, then shortcuts into Windows.
/// </summary>
/// <remarks>
/// The grid is composed from two lists rather than written out by hand, because the two halves are
/// not the same kind of thing.
///
/// The tools come from <see cref="ToolRegistry"/>. They belong to SteamXBox, and each is one field
/// away from running as its own process — which is how they will eventually become plugins.
///
/// The shortcuts below stay shortcuts. They open a Windows panel that already exists and works;
/// what costs the user time is finding it, and a deep link removes that cost today where rebuilding
/// the panel would take weeks and end up worse. There is nothing here worth extracting.
///
/// Real in-place controls — a volume slider, a Wi-Fi switch, changing the output device without
/// leaving the window — need Core Audio and the radio APIs. Absent rather than present and dead.
/// </remarks>
public static class QuickActions
{
    public static IReadOnlyList<QuickAction> All { get; } =
    [
        .. ToolRegistry.All.Select(FromTool),
        .. WindowsShortcuts,
    ];

    /// <summary>Turns a tool into a tile, so the grid does not care which half it came from.</summary>
    private static QuickAction FromTool(ToolDescriptor tool) => new(
        tool.Glyph,
        tool.Label,
        tool.Hint,
        Invoke: () => { },
        WithEnvironment: environment => ToolRegistry.Launch(tool, environment));

    /// <summary>Links into panels Windows already provides.</summary>
    private static IEnumerable<QuickAction> WindowsShortcuts =>
    [
        new(Glyphs.Volume, "Sortie audio", "Casque, enceintes ou HDMI", () => OpenSettings("sound")),
        new(Glyphs.Wifi, "Wi-Fi", "Reseaux et connexion sans fil", () => OpenSettings("network-wifi")),
        new(Glyphs.Bluetooth, "Bluetooth", "Appareils et appairage", () => OpenSettings("bluetooth")),
        new(Glyphs.Brightness, "Luminosite", "Affichage et luminosite", () => OpenSettings("display")),
        new(Glyphs.QuietHours, "Ne pas deranger", "Assistant de concentration", () => OpenSettings("quiethours")),
        new(Glyphs.TaskView, "Gestionnaire des taches", "Processus et performances", () => Launch("taskmgr.exe")),
    ];

    /// <summary>Opens a Settings page directly, skipping the navigation the notes complain about.</summary>
    private static void OpenSettings(string page) => Start($"ms-settings:{page}");

    private static void Launch(string executable) => Start(executable);

    private static void Start(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch
        {
            // Not every edition of Windows carries every one of these — Settings pages come and go
            // between releases. A missing one is not worth interrupting the user over.
        }
    }

}
