using System.Diagnostics;
using System.IO;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.ControlCentre;

/// <summary>
/// Exécute ce qu'un outil déclaré a demandé.
/// </summary>
/// <remarks>
/// <b>C'est l'hôte qui agit, jamais l'outil.</b> Un manifeste ne contient aucun code : il nomme un
/// verbe et une cible, et c'est ici — dans l'environnement, pas dans le dossier de l'outil — que la
/// liste de ce qui a le droit de se produire est tenue. Un outil téléchargé quelque part ne peut donc
/// rien faire de plus que ce que son manifeste montre à qui le lit.
///
/// <para>
/// Un verbe inconnu est refusé et dit en clair. Ajouter un verbe se fait ici, en conscience, pas en
/// écrivant un mot nouveau dans un <c>plugin.json</c> — sans quoi la liste blanche ne serait qu'une
/// intention.
/// </para>
///
/// <para>
/// C'est aussi le point où la confirmation devra s'attacher le jour où un modèle local demandera la
/// même chose par le serveur MCP : le contrat veut que ce qui agit soit confirmé par une fenêtre que
/// l'environnement dessine, et cette fenêtre-là se posera ici, pour les deux appelants à la fois.
/// </para>
/// </remarks>
public static class PluginVerbs
{
    /// <summary>Fait ce que le verbe demande, et rend une ligne pour la zone d'état.</summary>
    /// <param name="racine">La racine du produit, pour les cibles relatives.</param>
    public static string Execute(string does, string target, string racine)
    {
        if (!PluginActions.Known.Contains(does))
        {
            return $"Action inconnue : « {does} ».";
        }

        if (PluginActions.NeedsTarget(does) && target.Trim().Length == 0)
        {
            return $"L'action « {does} » demande une cible, et elle est vide.";
        }

        try
        {
            return does.ToLowerInvariant() switch
            {
                PluginActions.Application => Lancer(Path.Combine(racine, target), racine),
                PluginActions.Path => Ouvrir(target),
                PluginActions.WindowsSetting => Ouvrir($"ms-settings:{target}"),

                // Les verbes qui appartiennent a l'environnement lui-meme — vider l'ecran, ouvrir le
                // lanceur — passeront par lui et non par un processus. Dits en clair plutot que
                // silencieusement ignores : un bouton qui ne fait rien sans le dire est ce qui coute
                // le plus cher a diagnostiquer.
                _ => $"« {does} » n'est pas encore branché sur l'environnement.",
            };
        }
        catch (Exception exception)
        {
            return $"Échec de « {does} » : {exception.Message}";
        }
    }

    /// <summary>
    /// Lance un exécutable du produit.
    /// </summary>
    /// <remarks>
    /// Sous la racine et nulle part ailleurs : une cible qui remonte hors du dossier du produit est
    /// refusée. Sans ce garde, <c>"target": "..\\..\\Windows\\System32\\cmd.exe"</c> ferait d'un
    /// manifeste déclaratif un lanceur de n'importe quoi.
    /// </remarks>
    private static string Lancer(string chemin, string racine)
    {
        var complet = Path.GetFullPath(chemin);

        if (!complet.StartsWith(Path.GetFullPath(racine), StringComparison.OrdinalIgnoreCase))
        {
            return "Cible refusée : elle sort du dossier du produit.";
        }

        if (!File.Exists(complet))
        {
            return $"Introuvable : {Path.GetFileName(complet)}";
        }

        Process.Start(new ProcessStartInfo(complet) { UseShellExecute = true, WorkingDirectory = racine });
        return $"Lancé : {Path.GetFileName(complet)}";
    }

    private static string Ouvrir(string cible)
    {
        Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });
        return $"Ouvert : {cible}";
    }
}
