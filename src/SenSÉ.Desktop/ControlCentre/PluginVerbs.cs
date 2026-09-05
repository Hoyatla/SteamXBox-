using System.Diagnostics;
using System.IO;
using SenSÉ.Plugins;

namespace SenSÉ.Desktop.ControlCentre;

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
    /// <param name="environnement">
    /// L'environnement propre que le manifeste réclame, s'il en réclame un. Ce qui l'emporte n'est
    /// pas la bonne volonté du programme lancé mais ce qu'on lui donne à lire : un outil ne sait
    /// où est le dossier de l'utilisateur que par là.
    /// </param>
    public static string Execute(
        string does,
        string target,
        string racine,
        EnvironnementOutil? environnement = null)
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
                PluginActions.Application => Lancer(Path.Combine(racine, target), racine, environnement),
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
    private static string Lancer(string chemin, string racine, EnvironnementOutil? environnement)
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

        var depart = new ProcessStartInfo(complet)
        {
            UseShellExecute = true,
            WorkingDirectory = racine,
        };

        if (environnement is not null)
        {
            var dossier = Path.GetFullPath(Path.Combine(racine, environnement.Dossier));

            // Le dossier d'un outil reste dans le produit, sinon l'isolement se retourne : on
            // aurait détourné le dossier de l'utilisateur vers un autre endroit de son disque.
            if (!dossier.StartsWith(Path.GetFullPath(racine), StringComparison.OrdinalIgnoreCase))
            {
                return "Environnement refusé : son dossier sort du dossier du produit.";
            }

            if (EnvironnementIsole.Preparer(depart, dossier, environnement) is { Length: > 0 } refus)
            {
                return refus;
            }
        }

        try
        {
            Process.Start(depart);
        }
        catch (System.ComponentModel.Win32Exception echec) when (echec.NativeErrorCode == 740)
        {
            // Windows ne laisse pas composer l'environnement d'un processus qu'il va élever : le
            // programme élevé repart de l'environnement du compte administrateur. Dit en clair,
            // parce que « erreur 740 » n'apprend rien et que le remède n'est pas dans le code —
            // c'est un installeur par machine, qui ne peut pas être rangé dans un dossier.
            return $"{Path.GetFileName(complet)} demande les droits administrateur : on ne peut pas "
                + "lui donner un environnement propre. Un outil isolable s'installe pour "
                + "l'utilisateur, pas pour la machine.";
        }

        return environnement is null
            ? $"Lancé : {Path.GetFileName(complet)}"
            : $"Lancé dans son propre environnement : {Path.GetFileName(complet)}";
    }

    private static string Ouvrir(string cible)
    {
        Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });
        return $"Ouvert : {cible}";
    }
}
