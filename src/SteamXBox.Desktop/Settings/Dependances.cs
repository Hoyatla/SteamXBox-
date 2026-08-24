using System.IO;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Settings;

/// <summary>
/// Les programmes extérieurs dont les outils ont besoin, et ce qu'on en constate.
/// </summary>
/// <remarks>
/// <b>Constater, jamais installer.</b> Ces programmes ont leurs licences, leurs versions et leur
/// entretien propres ; certains — poppler, ComfyUI — ne pourraient pas être livrés sans exposer le
/// code du produit. L'hôte regarde s'ils sont là et le dit. Le reste appartient à l'utilisateur.
///
/// <para>
/// L'ordre de recherche est celui du produit : le dossier <c>Outils</c> d'abord, le PATH ensuite.
/// Une copie posée dans <c>Outils</c> gagne donc sur celle du système, ce qui permet de figer une
/// version connue sans toucher aux réglages de Windows.
/// </para>
/// </remarks>
public static class Dependances
{
    /// <summary>Ce qu'on sait d'une dépendance : ce qu'elle est, et si elle est là.</summary>
    /// <param name="Nom">Le nom montré.</param>
    /// <param name="Role">À quoi elle sert, et pour quel outil.</param>
    /// <param name="Presente">Trouvée sur cette machine.</param>
    /// <param name="Ou">Le chemin trouvé, ou l'endroit où elle était attendue.</param>
    /// <param name="Source">Où l'obtenir, quand elle manque.</param>
    /// <param name="Licence">Sa licence, telle que le manifeste la déclare.</param>
    public sealed record Etat(
        string Nom,
        string Role,
        bool Presente,
        string Ou,
        string Source,
        string Licence)
    {
        public string Detail
        {
            get
            {
                var morceaux = new List<string>();

                if (Role.Length > 0)
                {
                    morceaux.Add(Role);
                }

                morceaux.Add(Presente ? Ou : "absent — attendu dans " + Ou);

                if (!Presente && Source.Length > 0)
                {
                    morceaux.Add(Source);
                }

                if (Licence.Length > 0)
                {
                    morceaux.Add(Licence);
                }

                return string.Join("  ·  ", morceaux);
            }
        }

        /// <summary>Un mot, pour lire l'état d'un coup d'œil.</summary>
        public string Marque => Presente ? "présent" : "absent";
    }

    /// <summary>Toutes les dépendances déclarées, avec leur état constaté.</summary>
    public static IReadOnlyList<Etat> Lister(string dossierPlugins)
        => PluginCatalog.Scan(dossierPlugins).Loaded
            .Where(m => m.Kind == PluginCategory.Dependency)
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Constater)
            .ToList();

    private static Etat Constater(PluginManifest manifeste)
    {
        var attendu = Resoudre(manifeste.Target);
        var trouve = Chercher(attendu);

        return new Etat(
            manifeste.Name,
            manifeste.Hint,
            trouve is not null,
            trouve ?? attendu,
            manifeste.Source,
            manifeste.Licence);
    }

    /// <summary>Le chemin attendu, jetons résolus.</summary>
    private static string Resoudre(string cible)
    {
        var racine = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        return cible
            .Replace("{app}", racine, StringComparison.OrdinalIgnoreCase)
            .Replace("{tools}", Path.Combine(racine, "Outils"), StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Le programme, à l'endroit attendu ou à défaut sur le PATH.
    /// </summary>
    /// <remarks>
    /// Le repli sur le PATH n'est pas une commodité : l'utilisateur qui installe LibreOffice ou
    /// ffmpeg normalement ne les pose pas dans le dossier du produit, et une dépendance déclarée
    /// absente alors qu'elle est installée serait pire que pas de liste du tout.
    /// </remarks>
    private static string? Chercher(string attendu)
    {
        if (File.Exists(attendu))
        {
            return attendu;
        }

        var nom = Path.GetFileName(attendu);

        if (nom.Length == 0)
        {
            return Directory.Exists(attendu) ? attendu : null;
        }

        var chemin = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(chemin))
        {
            return null;
        }

        foreach (var dossier in chemin.Split(Path.PathSeparator))
        {
            if (dossier.Length == 0)
            {
                continue;
            }

            try
            {
                var candidat = Path.Combine(dossier, nom);

                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (ArgumentException)
            {
                // Une entrée de PATH mal formée ne doit pas arrêter la recherche.
            }
        }

        return null;
    }
}
