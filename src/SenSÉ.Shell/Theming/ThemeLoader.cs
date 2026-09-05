using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace SenSÉ.Shell.Theming;

/// <summary>
/// Applies the selected theme on top of the built-in one.
/// </summary>
/// <remarks>
/// Shared by the GUI and by Desktop on purpose. SenSÉ has one theme, loaded once and applied to
/// the whole environment; two copies of this logic would drift, and the first symptom would be a
/// control centre that does not match the window that opened it.
///
/// Every view refers to the theme only through named resource keys, so a dictionary that redefines
/// those keys restyles everything without touching a single view.
/// </remarks>
public static class ThemeLoader
{
    /// <summary>Name of the optional skin dropped next to the executable.</summary>
    private const string SkinFileName = "skin.xaml";

    /// <summary>
    /// Merges the active skin into <paramref name="application"/> and returns it, or null when the
    /// built-in theme is being used or the skin could not be read.
    /// </summary>
    /// <remarks>
    /// A malformed skin must never stop the application from starting, so every failure here is
    /// silent and leaves the built-in theme in place. Note that loading a skin cannot fully validate
    /// it: a dictionary parses happily while holding a resource of the wrong type — a Color where a
    /// Brush is expected — and that only throws when a window resolves it. Callers that can afford
    /// to should build their first window inside a try and call <see cref="Remove"/> on failure.
    /// </remarks>
    /// <summary>
    /// What the last call to <see cref="Apply"/> did.
    /// </summary>
    /// <remarks>
    /// Every failure here is swallowed so a broken skin cannot stop the application — which also
    /// means a theme silently not loading looks exactly like a theme that loaded and changed
    /// nothing. This records the outcome so the settings screen can say which one happened, instead
    /// of leaving the user and the next person to debug it guessing.
    /// </remarks>
    public static string LastOutcome { get; private set; } = "";

    public static ResourceDictionary? Apply(Application application, string? themeId)
    {
        try
        {
            // A loose skin.xaml next to the executable still wins, so a single-theme package keeps
            // working. Otherwise the selected theme folder is used.
            var path = Path.Combine(AppContext.BaseDirectory, SkinFileName);
            if (!File.Exists(path))
            {
                var theme = ThemeCatalog.Resolve(themeId);
                if (theme.IsBuiltIn)
                {
                    LastOutcome = string.IsNullOrWhiteSpace(themeId)
                        ? "Thème intégré."
                        : $"Thème « {themeId} » introuvable sous {ThemeCatalog.Root} — thème intégré utilisé.";
                    return null;
                }

                path = theme.SkinPath;
            }

            if (!File.Exists(path))
            {
                LastOutcome = $"skin.xaml absent : {path}";
                return null;
            }

            using var stream = File.OpenRead(path);
            if (XamlReader.Load(stream) is not ResourceDictionary skin)
            {
                LastOutcome = $"{Path.GetFileName(path)} n'est pas un ResourceDictionary.";
                return null;
            }

            // Merged last so it wins the lookup.
            application.Resources.MergedDictionaries.Add(skin);
            LastOutcome = $"Thème chargé : {path} ({skin.Count} clés).";
            return skin;
        }
        catch (Exception exception)
        {
            // Le message compte plus que l'exception : une erreur de type dans un skin ne se voit
            // qu'ici, et sans ce texte elle reste indiscernable d'un thème qui n'existe pas.
            LastOutcome = $"Échec du chargement : {exception.GetType().Name} — {exception.Message}";
            return null;
        }
    }

    /// <summary>Removes a previously applied skin, falling back to the built-in theme.</summary>
    public static void Remove(Application application, ResourceDictionary? skin)
    {
        if (skin is not null)
        {
            application.Resources.MergedDictionaries.Remove(skin);
        }
    }
}
