using System.IO;
using System.Text.Json;

namespace SenSÉ.Shell.Theming;

/// <summary>A theme found in the Themes folder.</summary>
/// <param name="Id">Folder name. This is what is stored in the settings.</param>
/// <param name="Name">Display name, from theme.json or the folder name.</param>
/// <param name="Description">One line shown under the selector.</param>
/// <param name="Directory">Absolute path to the theme folder.</param>
public sealed record ThemeInfo(string Id, string Name, string Description, string Directory)
{
    /// <summary>The built-in look, used when no theme folder is selected.</summary>
    public static ThemeInfo BuiltIn { get; } = new("", "Thème intégré", "L'apparence par défaut de SenSÉ.", "");

    public bool IsBuiltIn => Id.Length == 0;

    public string SkinPath => Path.Combine(Directory, "skin.xaml");
    public string PalettePath => Path.Combine(Directory, "skin.json");

    public override string ToString() => Name;
}

/// <summary>
/// Discovers the themes shipped next to the executable.
/// </summary>
/// <remarks>
/// One folder per theme under <c>Themes</c>, each holding a <c>skin.xaml</c> for the interface and a
/// <c>skin.json</c> for the overlay. An optional <c>theme.json</c> gives it a display name and a
/// description; without one the folder name is used, so dropping a folder in is enough to add a
/// theme.
///
/// A folder with no <c>skin.xaml</c> is ignored rather than listed and then found to do nothing.
/// </remarks>
public static class ThemeCatalog
{
    public const string FolderName = "Themes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Root => Path.Combine(AppContext.BaseDirectory, FolderName);

    public static IReadOnlyList<ThemeInfo> Discover()
    {
        var themes = new List<ThemeInfo> { ThemeInfo.BuiltIn };

        try
        {
            if (!Directory.Exists(Root))
            {
                return themes;
            }

            foreach (var folder in Directory.GetDirectories(Root).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!File.Exists(Path.Combine(folder, "skin.xaml")))
                {
                    continue;
                }

                themes.Add(ReadManifest(folder));
            }
        }
        catch
        {
            // An unreadable Themes folder must not stop the application from starting.
        }

        return themes;
    }

    /// <summary>Resolves a stored id, falling back to the built-in look when it no longer exists.</summary>
    public static ThemeInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ThemeInfo.BuiltIn;
        }

        var folder = Path.Combine(Root, id);
        return File.Exists(Path.Combine(folder, "skin.xaml")) ? ReadManifest(folder) : ThemeInfo.BuiltIn;
    }

    private static ThemeInfo ReadManifest(string folder)
    {
        var id = Path.GetFileName(folder);
        var name = id;
        var description = "";

        try
        {
            var manifest = Path.Combine(folder, "theme.json");
            if (File.Exists(manifest))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

                var root = document.RootElement;
                if (root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } value)
                {
                    name = value;
                }

                if (root.TryGetProperty("description", out var d) && d.GetString() is { } text)
                {
                    description = text;
                }
            }
        }
        catch
        {
            // A malformed manifest costs the theme its label, not its existence.
        }

        return new ThemeInfo(id, name, description, folder);
    }
}
