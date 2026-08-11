namespace SteamXBox.Plugins;

/// <summary>One element of what the host draws for a tool.</summary>
/// <remarks>
/// A vocabulary, not a layout language. The tool names what it contains; where those things go is
/// the host's business, which is what lets a tool written by a user be navigable with a controller
/// without its author having thought about it.
///
/// <para>
/// It grows when a real tool asks for something, never in anticipation. The line not to cross is
/// written in <c>Plugins/README.md</c>: the moment a tool needs to <i>compute</i> something the host
/// cannot name, it belongs to the next level and does not get one more <c>does</c>.
/// </para>
/// </remarks>
public sealed class PluginContentItem
{
    /// <summary>What it is: <c>text</c>, <c>number</c>, <c>choice</c> or <c>action</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Names the value, for <c>remembers</c> and for the action that reads it.</summary>
    public string Id { get; set; } = "";

    /// <summary>Shown beside it.</summary>
    public string Label { get; set; } = "";

    /// <summary>Starting value, as text whatever the kind.</summary>
    public string Value { get; set; } = "";

    public int Min { get; set; }

    public int Max { get; set; } = 100;

    /// <summary>The options, for a <c>choice</c>.</summary>
    public List<string> Options { get; set; } = [];

    /// <summary>For an <c>action</c>: what the host does, and to what.</summary>
    public string Does { get; set; } = "";

    public string Target { get; set; } = "";
}

/// <summary>
/// What a declarative tool may ask the host to do.
/// </summary>
/// <remarks>
/// Every entry answers the question the contract sets: <i>does the host already do this for
/// itself?</i> Opening a Windows settings page, launching an application, opening a path and
/// clearing the screen are all things the environment does on its own tiles and shortcuts, so
/// exposing them takes nothing new into the manifest.
///
/// <para>
/// Nothing here computes. A tool that needs a value worked out — a timer counting down, a
/// calculator evaluating an expression — is level two, and adding a <c>does</c> for it is how a
/// manifest turns into a language with no designer.
/// </para>
/// </remarks>
public static class PluginActions
{
    /// <summary>Opens a Windows settings page; the target is its <c>ms-settings</c> name.</summary>
    public const string WindowsSetting = "windows-setting";

    /// <summary>Launches an application; the target is its executable name.</summary>
    public const string Application = "application";

    /// <summary>Opens a file, a folder or an address; the target is the path.</summary>
    public const string Path = "path";

    /// <summary>Opens the search launcher.</summary>
    public const string Search = "search";

    /// <summary>Clears the screen, or puts the windows back.</summary>
    public const string ClearScreen = "clear-screen";

    /// <summary>
    /// Converts a document the user designated into another format.
    /// </summary>
    /// <remarks>
    /// The first action of the second level, and it obeys the same test as the first level's: the
    /// host already converts documents — the indexer has read the old Office and OpenDocument
    /// formats through LibreOffice for weeks, with filter names measured on real hardware rather
    /// than taken from documentation.
    ///
    /// <para>
    /// So "pdf to word" needs no code from the tool and no arbitrary program to run. That matters
    /// more than the convenience: the property that makes a downloaded manifest safe to install —
    /// it can do no more than what it shows to whoever reads it — survives intact.
    /// </para>
    /// </remarks>
    public const string Convert = "convert";

    /// <summary>Every action a manifest may name.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        WindowsSetting, Application, Path, Search, ClearScreen, Convert,
    };

    /// <summary>Whether the action needs something to act on.</summary>
    public static bool NeedsTarget(string does)
        => does.Equals(WindowsSetting, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Application, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Path, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Convert, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The actions that only a tool with a panel can name.
    /// </summary>
    /// <remarks>
    /// Converting needs a document, and a document is designated by the user in a panel. A tile has
    /// no panel, so a tile that named this action would be a button that could never do anything.
    /// </remarks>
    public static bool NeedsPanel(string does)
        => does.Equals(Convert, StringComparison.OrdinalIgnoreCase);
}
