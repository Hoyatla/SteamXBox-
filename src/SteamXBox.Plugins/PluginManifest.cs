using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamXBox.Plugins;

/// <summary>What a plugin is allowed to touch.</summary>
public enum PluginCategory
{
    Unknown,

    /// <summary>Documented, reversible Windows settings: cursors, accent colour, animation timings.</summary>
    ThemeWindows,

    /// <summary>SteamXBox's own windows: acrylic, mica, transitions.</summary>
    ThemeSurface,

    /// <summary>A tile in the control centre.</summary>
    Tile,

    /// <summary>A periodic display in the environment.</summary>
    Widget,

    /// <summary>
    /// A tool of the control centre: calculator, clipboard, timer, converter.
    /// </summary>
    /// <remarks>
    /// A tool is a plugin rather than a parallel mechanism of its own. Two systems that do the same
    /// thing drift, and it is always the second one that falls behind — this project has spent
    /// enough on that lesson elsewhere.
    ///
    /// <para>
    /// The host draws; the tool describes. A tool declares what it contains and
    /// <c>SteamXBox.Desktop</c> supplies the window, the theme, the controller navigation and the
    /// on-screen keyboard. Ten tools each drawing their own window would be ten foreign windows, none
    /// of them navigable with a controller — and a controller is the one device this project
    /// guarantees. The cost is that a tool cannot have an interface the host has no words for, and
    /// the vocabulary grows when a real tool asks for it, never in anticipation.
    /// </para>
    /// </remarks>
    Tool,
}

/// <summary>A plugin's <c>plugin.json</c>, as written on disk.</summary>
public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Licence { get; set; } = "";

    /// <summary>
    /// Where the resource comes from.
    /// </summary>
    /// <remarks>
    /// Required by attribution licences such as CC BY, which oblige the redistributor to credit the
    /// author and link the original. Shown in the settings screen so the obligation is met by the
    /// product itself rather than by a line in a file nobody opens.
    /// </remarks>
    public string Source { get; set; } = "";
    public bool Revertible { get; set; }
    public string Entry { get; set; } = "";

    /// <summary>Folder the manifest was read from. Not serialised.</summary>
    [JsonIgnore]
    public string Directory { get; set; } = "";

    [JsonIgnore]
    public PluginCategory Kind => Category switch
    {
        "theme.windows" => PluginCategory.ThemeWindows,
        "theme.surface" => PluginCategory.ThemeSurface,
        "tile" => PluginCategory.Tile,
        "widget" => PluginCategory.Widget,
        "tool" => PluginCategory.Tool,
        _ => PluginCategory.Unknown,
    };

    /// <summary>Absolute path of <see cref="Entry"/>, or empty when none is declared.</summary>
    [JsonIgnore]
    public string EntryPath =>
        Entry.Length == 0 ? "" : Path.Combine(Directory, Entry.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>A plugin that was rejected, and why.</summary>
public sealed record PluginRejection(string Directory, string Reason);

/// <summary>The result of scanning the plugin folder.</summary>
public sealed record PluginScan(IReadOnlyList<PluginManifest> Loaded, IReadOnlyList<PluginRejection> Rejected);

/// <summary>
/// Finds and validates the plugins in a folder.
/// </summary>
/// <remarks>
/// Validation refuses rather than repairs. A plugin that writes into Windows without declaring how
/// to undo it, or that ships third-party artwork without saying under what licence, is a problem
/// for whoever installs SteamXBox — not something to paper over with a default.
/// </remarks>
public static class PluginCatalog
{
    public const string ManifestFileName = "plugin.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Scans <paramref name="root"/> for plugin folders.</summary>
    public static PluginScan Scan(string root)
    {
        var loaded = new List<PluginManifest>();
        var rejected = new List<PluginRejection>();

        if (!System.IO.Directory.Exists(root))
        {
            return new PluginScan(loaded, rejected);
        }

        foreach (var folder in System.IO.Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                // A folder without a manifest is not an error: the plugin library also holds the
                // README, and a user may keep working files there.
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Options);
                if (manifest is null)
                {
                    rejected.Add(new PluginRejection(folder, "plugin.json vide"));
                    continue;
                }

                manifest.Directory = folder;

                var problem = Validate(manifest);
                if (problem is not null)
                {
                    rejected.Add(new PluginRejection(folder, problem));
                    continue;
                }

                loaded.Add(manifest);
            }
            catch (Exception exception)
            {
                rejected.Add(new PluginRejection(folder, $"plugin.json illisible : {exception.Message}"));
            }
        }

        return new PluginScan(loaded, rejected);
    }

    /// <summary>Returns the reason a manifest is unusable, or null when it is fine.</summary>
    public static string? Validate(PluginManifest manifest)
    {
        if (manifest.Id.Length == 0)
        {
            return "champ 'id' manquant";
        }

        if (manifest.Kind == PluginCategory.Unknown)
        {
            return $"catégorie inconnue : '{manifest.Category}'";
        }

        // The rule the contract exists for. Without a way back, uninstalling SteamXBox would leave
        // a system carrying cursors and colours whose origin the user cannot trace.
        if (manifest.Kind == PluginCategory.ThemeWindows && !manifest.Revertible)
        {
            return "un plugin theme.windows doit déclarer revertible=true";
        }

        // Learnt the expensive way: an icon was shipped whose free tier required visible
        // attribution, which a paid edition cannot satisfy. The field makes it checkable instead of
        // something to remember.
        if (manifest.Licence.Length == 0)
        {
            return "champ 'licence' manquant";
        }

        if (manifest.Entry.Length > 0 && !File.Exists(manifest.EntryPath))
        {
            return $"point d'entrée introuvable : {manifest.Entry}";
        }

        return null;
    }
}
