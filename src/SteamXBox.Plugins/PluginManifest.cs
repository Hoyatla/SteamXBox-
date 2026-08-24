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

    /// <summary>
    /// Un programme extérieur dont un outil a besoin, décrit pour que l'utilisateur le voie.
    /// </summary>
    /// <remarks>
    /// Ce n'est pas un outil : il n'a ni tuile, ni panneau, ni action. Il existe parce que la moitié
    /// de ce qui peut manquer à SteamXBox n'est pas dans SteamXBox — ffmpeg, LibreOffice, un Python.
    /// Sans description, leur absence se manifestait par un outil qui refuse de fonctionner, et
    /// l'utilisateur devait deviner lequel installer.
    ///
    /// <para>
    /// Une dépendance déclare où on la cherche (<c>target</c>) et où on l'obtient (<c>source</c>).
    /// L'hôte vérifie et le dit dans les réglages : présent, ou absent avec l'adresse. C'est la même
    /// règle que partout ailleurs — le manifeste décrit, l'hôte constate.
    /// </para>
    ///
    /// <para>
    /// Rien n'est installé automatiquement, et c'est délibéré : ces programmes ont leurs licences,
    /// leurs versions et leur entretien propres. Certains, comme poppler, ne pourraient même pas
    /// être livrés sans exposer le code du produit.
    /// </para>
    /// </remarks>
    Dependency,
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

    // ---- Tools ----

    /// <summary>Segoe Fluent Icons code point, as the compiled tools already use.</summary>
    public string Glyph { get; set; } = "";

    /// <summary>
    /// La clé d'une géométrie du dictionnaire d'icônes, à dessiner au lieu du glyphe.
    /// </summary>
    /// <remarks>
    /// Une clé, jamais un chemin de fichier : WPF ne lit pas le SVG, et les tracés sont convertis une
    /// fois pour toutes dans le dictionnaire de l'environnement. Vide, la tuile garde son glyphe de
    /// police — une icône absente ne coûte pas l'outil.
    /// </remarks>
    public string Icon { get; set; } = "";

    /// <summary>One line under the title when the tile has the focus.</summary>
    public string Hint { get; set; } = "";

    /// <summary>
    /// <c>tile</c> — the tool acts without showing anything — or <c>panel</c>, where the host opens
    /// a window and draws <see cref="Content"/> in it.
    /// </summary>
    public string Surface { get; set; } = "tile";

    /// <summary>The action the host performs, named from the vocabulary it publishes.</summary>
    public string Does { get; set; } = "";

    /// <summary>What the action applies to: a settings page, an application, a path.</summary>
    public string Target { get; set; } = "";

    /// <summary>What the host draws when the surface is a panel.</summary>
    public List<PluginContentItem> Content { get; set; } = [];

    /// <summary>Values the host keeps for the tool between sessions, named one by one.</summary>
    public List<string> Remembers { get; set; } = [];

    /// <summary>
    /// Whether the tool is on when nobody has said otherwise.
    /// </summary>
    /// <remarks>
    /// Three states, not two, and the distinction earns its keep: a tool the user switched off, a
    /// tool the user switched on, and a tool nobody has touched. Only the third consults this.
    ///
    /// <para>
    /// It exists for tools that should ship present but idle — a diagnostic monitor is the first.
    /// Shipping it enabled would put a watcher on every customer's machine by default; shipping it
    /// absent would mean it is not there on the day it is needed.
    /// </para>
    /// </remarks>
    public bool Enabled { get; set; } = true;

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
        "dependency" => PluginCategory.Dependency,
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

        // Une dépendance qui ne dit pas où on la cherche ne peut être ni constatée ni réclamée :
        // elle ne rendrait service à personne, et la liste des réglages afficherait une ligne
        // dont l'état serait toujours inconnu.
        if (manifest.Kind == PluginCategory.Dependency && manifest.Target.Length == 0)
        {
            return "une dépendance doit déclarer un 'target' : où le programme est cherché";
        }

        return manifest.Kind == PluginCategory.Tool ? ValidateTool(manifest) : null;
    }

    /// <summary>
    /// The rules a declarative tool has to meet on top of the common ones.
    /// </summary>
    /// <remarks>
    /// Refused rather than repaired, for the same reason as the rest: a tool whose action the host
    /// does not know would appear as a tile that does nothing when pressed, and the user would have
    /// no way to tell that from a bug in SteamXBox.
    /// </remarks>
    private static string? ValidateTool(PluginManifest manifest)
    {
        if (manifest.Name.Length == 0)
        {
            return "champ 'name' manquant";
        }

        if (manifest.Glyph.Length == 0)
        {
            return "champ 'glyph' manquant : une tuile sans icône n'est pas atteignable du regard";
        }

        var surface = manifest.Surface.ToLowerInvariant();

        if (surface is not ("tile" or "panel"))
        {
            return $"surface inconnue : '{manifest.Surface}' (attendu 'tile' ou 'panel')";
        }

        // A tile is only its action, so it must have one. A panel gets its actions from its content.
        if (surface == "tile")
        {
            return PluginActions.NeedsPanel(manifest.Does)
                ? $"l'action '{manifest.Does}' a besoin d'un panneau : elle agit sur ce que l'utilisateur désigne"
                : Action(manifest.Does, manifest.Target, "l'outil");
        }

        if (manifest.Content.Count == 0)
        {
            return "une surface 'panel' sans 'content' n'aurait rien à montrer";
        }

        foreach (var item in manifest.Content)
        {
            var kind = item.Kind.ToLowerInvariant();

            if (kind is not ("text" or "number" or "choice" or "action" or "file"))
            {
                return $"élément inconnu dans 'content' : '{item.Kind}'";
            }

            // A file is designated by the user in the host's own panel — the tool never sees the
            // filesystem, only what was pointed at. Without an id there is nothing for an action to
            // refer to afterwards.
            if (kind == "file" && item.Id.Length == 0)
            {
                return "un élément 'file' doit avoir un 'id' pour que l'action puisse le nommer";
            }

            // Un choix tire ses valeurs du manifeste, du générateur ou de ses recettes, et il lui
            // faut au moins l'une des trois : sans elles, la liste serait vide quoi qu'il arrive.
            if (kind == "choice"
                && item.Options.Count == 0
                && item.From.Length == 0
                && item.Recettes.Count == 0)
            {
                return $"l'élément '{item.Id}' est un choix sans options, sans 'from' ni recettes";
            }

            // Une recette se nomme depuis la cible d'une action, en « {recette:id-du-choix} » :
            // sans identifiant sur le choix, elle serait déclarée et inatteignable.
            if (item.Recettes.Count > 0 && item.Id.Length == 0)
            {
                return "un choix qui porte des recettes doit avoir un 'id' : c'est par lui qu'une "
                    + "action nomme celle qui a été retenue";
            }

            foreach (var recette in item.Recettes)
            {
                if (recette.Id.Length == 0 || recette.Target.Length == 0)
                {
                    return $"une recette de '{item.Id}' n'a pas d'« id » ou pas de « target »";
                }
            }

            if (item.From.Length > 0 && !item.From.Contains('.', StringComparison.Ordinal))
            {
                return $"le 'from' de '{item.Id}' s'écrit NomDuNoeud.nom_de_l_entree";
            }

            if (kind == "action" && Action(item.Does, item.Target, $"l'élément '{item.Id}'") is { } problem)
            {
                return problem;
            }

            // Un bouton qui tire au sort un réglage absent ne tirerait rien et se contenterait de
            // relancer à l'identique — « Autre proposition » rendrait deux fois la même chose, et
            // rien ne dirait pourquoi.
            if (item.Hasard.Length > 0
                && !manifest.Content.Exists(c =>
                    c.Id.Equals(item.Hasard, StringComparison.OrdinalIgnoreCase)))
            {
                return $"'{item.Label}' tire au sort '{item.Hasard}', qui n'est pas un réglage de cet outil";
            }
        }

        return null;
    }

    /// <summary>Checks one named action against the vocabulary the host publishes.</summary>
    private static string? Action(string does, string target, string who)
    {
        if (does.Length == 0)
        {
            return $"{who} ne déclare aucune action 'does'";
        }

        if (!PluginActions.Known.Contains(does))
        {
            return $"action inconnue : '{does}' (connues : {string.Join(", ", PluginActions.Known)})";
        }

        return PluginActions.NeedsTarget(does) && target.Length == 0
            ? $"l'action '{does}' de {who} demande un 'target'"
            : null;
    }
}
