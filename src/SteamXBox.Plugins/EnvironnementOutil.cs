using System.Diagnostics;

namespace SteamXBox.Plugins;

/// <summary>
/// Ce qu'un outil déclare vouloir comme environnement propre.
/// </summary>
/// <remarks>
/// Première pièce du protocole d'hébergement : un outil extérieur ne demande pas à SteamXBox où
/// écrire, il l'apprend par son environnement — et c'est SteamXBox qui le lui compose. Le
/// vocabulaire est volontairement court, parce qu'il s'étoffera : une déclaration qui ne porte
/// qu'un <c>dossier</c> reçoit déjà tout le détournement standard.
/// </remarks>
public sealed class EnvironnementOutil
{
    /// <summary>Où tout ce que l'outil écrira doit atterrir.</summary>
    /// <remarks>
    /// Chemin dans le vocabulaire habituel des manifestes — <c>{tools}\comfyui</c> — résolu par
    /// l'appelant, comme une cible.
    /// </remarks>
    public string Dossier { get; set; } = "";

    /// <summary>Des variables à détourner en plus des habituelles.</summary>
    /// <remarks>
    /// Chaque entrée est <c>NOM=sous-dossier</c>. Sert aux outils qui inventent leur propre
    /// variable de cache — il y en a un par bibliothèque, et la liste standard ne peut pas les
    /// deviner toutes.
    /// </remarks>
    public Dictionary<string, string> Detourne { get; set; } = [];

    /// <summary>Des variables à poser telles quelles, sans les rattacher au dossier.</summary>
    /// <remarks>
    /// C'est par là qu'un outil reçoit ce qu'il partage avec les autres — le dossier des modèles,
    /// par exemple, qui n'a aucune raison d'être recopié dans chaque environnement.
    /// </remarks>
    public Dictionary<string, string> Ajoute { get; set; } = [];
}

/// <summary>
/// Donne à un programme extérieur un environnement qui n'est qu'à lui.
/// </summary>
/// <remarks>
/// <b>Le problème, tel qu'il s'est posé.</b> Comfy Desktop, installé normalement, a écrit son
/// programme dans <c>%LOCALAPPDATA%\Programs</c> et quarante-deux gigaoctets de modèles dans
/// <c>%LOCALAPPDATA%\Comfy-Desktop</c>. Désinstallé, il a tout laissé — parce que rien de cela
/// n'était à lui : c'était dans le dossier de l'utilisateur, et un désinstalleur n'y touche pas.
///
/// <para>
/// <b>La réponse ne demande aucune coopération de l'outil.</b> Un programme n'a pas d'autre moyen
/// de savoir où est le dossier de l'utilisateur que de lire son environnement. Le lancer avec un
/// <c>LOCALAPPDATA</c> qui pointe dans le produit suffit à faire tomber tout ce qu'il écrit au bon
/// endroit — sans l'avoir modifié, sans son accord, et sans qu'il s'en aperçoive. L'installer d'un
/// tiers devient portable parce qu'on lui a menti sur l'endroit où il se trouve.
/// </para>
///
/// <para>
/// <b>Ce que cela n'attrape pas, et il faut le savoir.</b> Le registre, qu'aucune variable ne
/// détourne. Les chemins écrits en dur — un programme qui compose lui-même
/// <c>C:\Users\...\AppData</c> ignore ce qu'on lui dit. Les services et les pilotes, qui ne sont
/// pas des enfants de ce processus. Et les programmes qui demandent le dossier à Windows par
/// <c>SHGetKnownFolderPath</c> plutôt que de lire leur environnement — Chromium et donc Electron en
/// font partie pour certains chemins. Le détournement couvre bien Python, Node et les outils en
/// ligne de commande ; il couvre partiellement les applications Electron. C'est une première pièce
/// du protocole, pas une prison.
/// </para>
/// </remarks>
public static class EnvironnementIsole
{
    /// <summary>
    /// Les variables détournées d'office, et le sous-dossier que chacune reçoit.
    /// </summary>
    /// <remarks>
    /// Les quatre premières sont celles que Windows lui-même définit et que tout le monde lit. Les
    /// suivantes sont celles des bibliothèques qui téléchargent : ce sont elles qui décident où
    /// atterrissent des dizaines de gigaoctets de modèles, et les oublier, c'est laisser un outil
    /// remplir le disque de l'utilisateur pendant qu'on croit l'avoir rangé.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Habituelles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["APPDATA"] = "itinerant",
            ["LOCALAPPDATA"] = "local",
            ["TEMP"] = "passage",
            ["TMP"] = "passage",
            ["USERPROFILE"] = "",
            ["HOME"] = "",
            ["PYTHONUSERBASE"] = "python",
            ["PIP_CACHE_DIR"] = @"cache\pip",
            ["HF_HOME"] = @"cache\huggingface",
            ["HUGGINGFACE_HUB_CACHE"] = @"cache\huggingface",
            ["TORCH_HOME"] = @"cache\torch",
            ["XDG_CACHE_HOME"] = "cache",
            ["XDG_DATA_HOME"] = "donnees",
        };

    /// <summary>
    /// Compose l'environnement du programme à lancer.
    /// </summary>
    /// <param name="depart">Le lancement à préparer.</param>
    /// <param name="dossier">Le dossier de l'outil, déjà résolu et absolu.</param>
    /// <param name="declare">Ce que le manifeste demande en plus, s'il demande quelque chose.</param>
    /// <returns>Une phrase vide si tout va bien, la raison du refus sinon.</returns>
    /// <remarks>
    /// Le reste de l'environnement est hérité tel quel : <c>PATH</c>, <c>SystemRoot</c>, la langue,
    /// le nombre de cœurs. Repartir d'un environnement vide casserait la plupart des programmes
    /// pour un gain nul — ce qu'on isole, c'est où l'outil écrit, pas ce qu'il sait de la machine.
    /// </remarks>
    public static string Preparer(ProcessStartInfo depart, string dossier, EnvironnementOutil? declare)
    {
        if (dossier.Length == 0)
        {
            return "Environnement refusé : aucun dossier déclaré.";
        }

        var racine = Path.GetFullPath(dossier);

        // Sans cela, Windows refuse un environnement sur mesure : ShellExecute lance par le shell,
        // qui transmet l'environnement de la session et non le nôtre.
        depart.UseShellExecute = false;

        try
        {
            foreach (var (nom, sous) in Habituelles.Concat(declare?.Detourne ?? []))
            {
                var vers = sous.Length == 0 ? racine : Path.Combine(racine, sous);

                Directory.CreateDirectory(vers);
                depart.Environment[nom] = vers;
            }

            foreach (var (nom, valeur) in declare?.Ajoute ?? [])
            {
                depart.Environment[nom] = valeur;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Environnement impossible : {exception.Message}";
        }

        return "";
    }
}
