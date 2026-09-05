using System.Windows;

namespace SenSÉ.Desktop.Tools;

/// <summary>
/// A tool SenSÉ owns.
/// </summary>
/// <remarks>
/// Deliberately shaped like a <c>plugin.json</c> — an id, a name, an icon, a category, an entry
/// point — while still being compiled into the environment. Tools are not plugins yet, and it is
/// too early for them to be: extracting a contract from one working tool would be guessing at it.
///
/// What this buys now is that the tile grid is generated from a list instead of being written by
/// hand, so adding a tool is adding a line. What it buys later is that moving a tool out of the
/// process is mechanical: fill in <see cref="Executable"/>, drop <see cref="Run"/>, and nothing
/// else in the environment changes.
/// </remarks>
/// <param name="Id">Stable identifier. Becomes the plugin folder name if the tool is extracted.</param>
/// <param name="Label">Short name, in French, which doubles as the translation key.</param>
/// <param name="Hint">One line describing what it does.</param>
/// <param name="Glyph">Segoe Fluent Icons code point.</param>
/// <param name="Run">
/// Runs the tool in this process. Takes the environment window because a tool may need to hide and
/// restore it — the capture does. Returns a line for the status area.
/// </param>
/// <param name="Executable">
/// Name of a separate executable to launch instead, next to SenSÉ.Desktop.exe. Empty today:
/// this is the seam along which a tool becomes its own process, which is how it will eventually
/// become a real plugin. A tool in its own process cannot take the environment down with it — the
/// operating system guarantees that boundary rather than a try/catch.
/// </param>
/// <param name="Start">
/// Started once with the environment, for a tool that must be running before anyone opens it.
/// The clipboard forced this field: a history only exists if something was listening at the moment
/// of the copy, so it cannot be created on demand like the calculator. Worth discovering now rather
/// than after a plugin contract had been frozen without it.
/// </param>
/// <param name="IsSystem">
/// SenSÉ itself rather than a tool of it, and therefore not something to switch off.
/// </param>
/// <remarks>
/// <para>
/// <b>Why the distinction exists.</b> The controller configuration and the settings window are the
/// product, not accessories to it. Listing them where tools are switched off and archived offers
/// the user a way to lock themselves out: turning off the settings tile removes the only tile that
/// opens the screen it would be turned back on from, and archiving would invite the system to
/// compress itself. A tool is something the environment can live without; these two are what the
/// environment is.
/// </para>
/// </remarks>
public sealed record ToolDescriptor(
    string Id,
    string Label,
    string Hint,
    string Glyph,
    Func<Window, string>? Run = null,
    string Executable = "",
    string Arguments = "",
    Action? Start = null,
    bool IsSystem = false,
    System.Windows.Media.Geometry? Icon = null,
    Func<string>? Compte = null);
