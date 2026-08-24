using System.Globalization;
using System.Text.Json;

namespace SteamXBox.Tools.Modeles;

/// <summary>Un fichier d'un jeu, et où il doit atterrir.</summary>
/// <param name="Url">D'où il vient.</param>
/// <param name="Vers">Son chemin sous <c>Outils\ComfyUI\models</c>, séparateurs en avant.</param>
/// <param name="Octets">
/// Sa taille exacte, qui sert à constater qu'il est entier.
/// </param>
/// <remarks>
/// La taille exacte n'est pas une commodité d'affichage : c'est la seule façon de distinguer un
/// fichier complet d'un téléchargement interrompu. Un modèle tronqué ne se plaint pas à
/// l'installation — il échoue au premier usage, sur un message qui parle de format invalide et
/// qu'on va chercher partout sauf dans le réseau.
/// </remarks>
public sealed record FichierModele(string Url, string Vers, long Octets);

/// <summary>La classe de machine à laquelle un jeu s'adresse.</summary>
/// <remarks>
/// Nommée par la machine et non par la qualité : « le meilleur » ne veut rien dire à qui ne sait
/// pas ce que sa carte porte, alors que « poste familial » ou « PC de jeu » se reconnaît sans
/// rien connaître aux modèles. C'est le seul axe sur lequel l'utilisateur peut réellement choisir.
/// </remarks>
public enum Palier
{
    /// <summary>Carte intégrée ou petite carte dédiée : jusqu'à six gigaoctets.</summary>
    Bureau,

    /// <summary>Carte de jeu courante : jusqu'à douze gigaoctets.</summary>
    Jeu,

    /// <summary>Au-delà : les cartes qu'on achète en connaissance de cause.</summary>
    Expert,
}

/// <summary>Ce qu'on constate d'un jeu sur cette machine.</summary>
public enum EtatJeu
{
    /// <summary>Aucun de ses fichiers n'est là.</summary>
    Absent,

    /// <summary>Une partie seulement : interrompu, ou à moitié effacé.</summary>
    Partiel,

    /// <summary>Tous les fichiers sont là, et entiers.</summary>
    Installe,
}

/// <summary>
/// Un ensemble de modèles qui vont ensemble, et ce qu'il réclame de la machine.
/// </summary>
/// <param name="Id">Son identifiant, stable.</param>
/// <param name="Nom">Ce qui est montré.</param>
/// <param name="Role">
/// Ce qu'il sert à faire, sous une forme comparable : <c>texte-image</c>, <c>image-video</c>.
/// </param>
/// <param name="Pour">Ce qu'il permet de faire, en une phrase, pour un humain.</param>
/// <param name="Licence">Sous quelle licence, ce qui décide de son usage commercial.</param>
/// <param name="Modules">Les modules ComfyUI qu'il faut en plus, s'il y en a.</param>
/// <param name="Fichiers">Ce qu'il faut télécharger.</param>
/// <param name="MemoireDeclaree">
/// La mémoire vidéo réclamée, en mégaoctets, quand la déclaration la précise. Zéro sinon.
/// </param>
public sealed record JeuModeles(
    string Id,
    string Nom,
    string Role,
    string Pour,
    string Licence,
    IReadOnlyList<string> Modules,
    IReadOnlyList<FichierModele> Fichiers,
    int MemoireDeclaree)
{
    /// <summary>Ce qu'il faut télécharger en tout.</summary>
    public long Octets => Fichiers.Sum(f => f.Octets);

    /// <summary>
    /// La mémoire vidéo qu'il réclame : déclarée si elle l'est, déduite sinon.
    /// </summary>
    /// <remarks>
    /// <b>Déduite du plus gros fichier, et c'est un choix.</b> ComfyUI charge l'encodeur de texte,
    /// s'en sert, le libère, puis charge le modèle : les deux ne sont jamais sur la carte en même
    /// temps. Le pic est donc celui du plus gros morceau, pas la somme — un jeu de 13 Go peut très
    /// bien tenir sur une carte de 12.
    ///
    /// <para>
    /// <b>Déduire plutôt que réclamer un chiffre à l'auteur, parce qu'un chiffre écrit à la main
    /// ment.</b> Ce produit en a fait l'expérience : le générateur a déclaré pendant des semaines un
    /// pic de 20 000 Mio sur une carte qui en a 12 282, et c'est sur la foi de ce chiffre qu'un
    /// modèle de 16 Go a été installé — pour tourner quinze minutes avec la carte à 13 %, sans
    /// jamais calculer. Une taille de fichier, elle, ne peut pas se tromper.
    /// </para>
    ///
    /// <para>
    /// La déclaration reste possible pour les cas où la déduction serait fausse — un modèle dont le
    /// calcul demande bien plus que ses poids. Elle doit alors être justifiée dans le fichier.
    /// </para>
    /// </remarks>
    public int MemoireVideoMo => MemoireDeclaree > 0
        ? MemoireDeclaree
        : (int)(Fichiers.Count == 0 ? 0 : Fichiers.Max(f => f.Octets) / (1024 * 1024));

    /// <summary>
    /// La classe de machine à laquelle ce jeu s'adresse.
    /// </summary>
    /// <remarks>
    /// <b>Déduite de la mémoire réclamée, pas déclarée.</b> Un auteur qui range lui-même son jeu
    /// dans un palier se trompe ou se flatte ; la mémoire, elle, ne discute pas. Et un jeu ajouté
    /// demain atterrit tout seul au bon endroit, sans que personne ait à y penser.
    ///
    /// <para>
    /// Les bornes sont celles du marché : six gigaoctets couvrent les cartes intégrées et les
    /// petites cartes dédiées d'un poste familial ; douze couvrent la grande majorité des cartes de
    /// jeu, dont la 4070 SUPER sur laquelle ce produit se développe ; au-delà commencent les cartes
    /// qu'on achète en connaissance de cause.
    /// </para>
    /// </remarks>
    public Palier Palier => MemoireVideoMo switch
    {
        <= 6000 => Palier.Bureau,
        <= 12000 => Palier.Jeu,
        _ => Palier.Expert,
    };

    /// <summary>Tient-il dans la mémoire vidéo libre, en mégaoctets ?</summary>
    /// <remarks>
    /// <b>Contre la mémoire libre, jamais contre celle de la carte.</b> Le bureau de Windows en
    /// garde une part qui ne se rend pas — un gestionnaire de fenêtres, un navigateur, un logiciel
    /// de clavier. Sur la machine de développement, 1 404 Mio sur 12 282 avant qu'un seul modèle
    /// ne soit chargé.
    /// </remarks>
    public bool Tient(int libreMo) => libreMo > 0 && MemoireVideoMo <= libreMo;
}

/// <summary>
/// Les jeux de modèles proposés, lus depuis une déclaration.
/// </summary>
/// <remarks>
/// <b>Pourquoi cela existe.</b> Le produit dépend de modèles, et ces modèles changeront au besoin
/// des clients : celui qui a une carte de 8 Go et celui qui en a 24 n'installeront pas les mêmes,
/// et aucun des deux ne doit avoir à le deviner. Sans ce choix, il ne reste que deux issues — un
/// produit qui ne tourne que sur la machine de son auteur, ou un client qui télécharge treize
/// gigaoctets pour découvrir que sa carte ne les prend pas.
///
/// <para>
/// <b>Une déclaration, pas du code.</b> Ajouter un jeu se fait en ajoutant quelques lignes au
/// fichier ; personne ne recompile, et un client peut proposer les siens. C'est la même règle que
/// pour les manifestes d'outils, et pour la même raison : ce qui change au rythme du monde
/// extérieur n'a rien à faire dans un binaire.
/// </para>
/// </remarks>
public static class JeuxModeles
{
    /// <summary>Le fichier de déclaration, à côté du produit.</summary>
    public static string Fichier
        => Path.Combine(AppContext.BaseDirectory, "Modeles", "jeux-modeles.json");

    /// <summary>Là où les fichiers atterrissent.</summary>
    public static string Racine
        => Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI", "models");

    /// <summary>Lit la déclaration livrée avec le produit.</summary>
    public static IReadOnlyList<JeuModeles> Charger(Action<string>? journal)
    {
        if (!File.Exists(Fichier))
        {
            journal?.Invoke($"jeux de modèles : aucune déclaration en {Fichier}");

            return [];
        }

        try
        {
            return Lire(File.ReadAllText(Fichier));
        }
        catch (IOException exception)
        {
            journal?.Invoke($"jeux de modèles illisibles : {exception.Message}");

            return [];
        }
    }

    /// <summary>Lit une déclaration.</summary>
    /// <remarks>
    /// Un jeu mal formé est écarté et les autres restent : une virgule de trop dans une entrée ne
    /// doit pas priver l'utilisateur de tout le catalogue. Le document entier illisible, en
    /// revanche, ne rend rien — il n'y a alors rien à sauver.
    /// </remarks>
    public static IReadOnlyList<JeuModeles> Lire(string json)
    {
        var jeux = new List<JeuModeles>();

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("jeux", out var tableau)
                || tableau.ValueKind != JsonValueKind.Array)
            {
                return jeux;
            }

            foreach (var element in tableau.EnumerateArray())
            {
                if (Un(element) is { } jeu)
                {
                    jeux.Add(jeu);
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return jeux;
    }

    private static JeuModeles? Un(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Texte(element, "id");
        var fichiers = Fichiers(element);

        // Un jeu sans identifiant ne peut être ni retrouvé ni retenu ; un jeu sans fichier ne
        // téléchargerait rien. Dans les deux cas il vaut mieux qu'il n'apparaisse pas du tout que
        // de proposer un bouton qui ne fait rien.
        if (id.Length == 0 || fichiers.Count == 0)
        {
            return null;
        }

        return new JeuModeles(
            id,
            Texte(element, "nom") is { Length: > 0 } nom ? nom : id,
            Texte(element, "role"),
            Texte(element, "pour"),
            Texte(element, "licence"),
            Mots(element, "modules"),
            fichiers,
            Entier(element, "memoireVideoMo"));
    }

    private static IReadOnlyList<FichierModele> Fichiers(JsonElement element)
    {
        var fichiers = new List<FichierModele>();

        if (!element.TryGetProperty("fichiers", out var tableau)
            || tableau.ValueKind != JsonValueKind.Array)
        {
            return fichiers;
        }

        foreach (var un in tableau.EnumerateArray())
        {
            if (un.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = Texte(un, "url");
            var vers = Texte(un, "vers");
            var octets = un.TryGetProperty("octets", out var taille)
                && taille.ValueKind == JsonValueKind.Number
                && taille.TryGetInt64(out var lu)
                    ? lu
                    : 0;

            // Une entrée sans taille ne permettrait pas de constater qu'un fichier est entier, et
            // un chemin absolu ou remontant sortirait du dossier des modèles.
            if (url.Length == 0 || vers.Length == 0 || octets <= 0 || !Sage(vers))
            {
                continue;
            }

            fichiers.Add(new FichierModele(url, vers, octets));
        }

        return fichiers;
    }

    /// <summary>Ce chemin reste-t-il sous le dossier des modèles ?</summary>
    /// <remarks>
    /// La déclaration peut venir d'ailleurs que du produit — un client la complète, quelqu'un la
    /// partage. Un chemin remontant y écrirait n'importe où sur la machine, sous couvert
    /// d'installer un modèle.
    /// </remarks>
    private static bool Sage(string vers)
        => !Path.IsPathRooted(vers)
           && !vers.Contains("..", StringComparison.Ordinal)
           && vers.IndexOfAny(Path.GetInvalidPathChars()) < 0;

    /// <summary>Ce qu'on constate de ce jeu sur cette machine.</summary>
    /// <remarks>
    /// Constaté par la taille exacte et non par la seule présence : un téléchargement interrompu
    /// laisse un fichier qui existe et qui ment. Le distinguer ici évite qu'il ne soit découvert
    /// bien plus tard, sous la forme d'une erreur de format au premier usage.
    /// </remarks>
    public static EtatJeu Etat(JeuModeles jeu, string racine)
    {
        var entiers = 0;

        foreach (var fichier in jeu.Fichiers)
        {
            var ou = Path.Combine(racine, fichier.Vers.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(ou) && new FileInfo(ou).Length == fichier.Octets)
            {
                entiers++;
            }
        }

        return entiers == 0 ? EtatJeu.Absent
            : entiers == jeu.Fichiers.Count ? EtatJeu.Installe
            : EtatJeu.Partiel;
    }

    /// <summary>
    /// Le meilleur jeu d'un rôle donné qui tienne dans la mémoire libre.
    /// </summary>
    /// <remarks>
    /// « Meilleur » se lit ici « le plus exigeant qui passe encore » : à qualité croissante avec la
    /// taille, le jeu le plus lourd qui tient est celui qui exploite la carte sans la déborder.
    /// Rien ne tient, on rend null plutôt que de proposer quand même — c'est exactement la
    /// proposition qu'il ne faut pas faire.
    ///
    /// <para>
    /// <b>Le rôle n'est pas facultatif, et c'est une mesure qui l'a montré.</b> Sans lui, la règle
    /// comparait des jeux qui ne font pas la même chose et conseillait un modèle image-vidéo à qui
    /// voulait dessiner à partir d'une phrase — il était simplement le plus lourd de la liste. Plus
    /// lourd ne veut dire meilleur qu'entre choses comparables.
    /// </para>
    /// </remarks>
    public static JeuModeles? Recommander(
        IReadOnlyList<JeuModeles> jeux, int libreMo, string role)
        => jeux
            .Where(j => j.Tient(libreMo)
                        && (role.Length == 0
                            || j.Role.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(j => j.MemoireVideoMo)
            .ThenBy(j => j.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Le conseil pour chaque rôle présent dans la déclaration.</summary>
    /// <remarks>
    /// Un produit qui sait faire deux choses a besoin d'un modèle pour chacune : conseiller un seul
    /// jeu laisserait la moitié de ses outils sans rien.
    /// </remarks>
    public static IReadOnlyList<JeuModeles> Conseils(IReadOnlyList<JeuModeles> jeux, int libreMo)
        => jeux.Select(j => j.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(role => Recommander(jeux, libreMo, role))
            .Where(j => j is not null)
            .Select(j => j!)
            .ToList();

    /// <summary>La taille d'un jeu, écrite pour un humain.</summary>
    public static string Poids(long octets)
        => octets >= 1024L * 1024 * 1024
            ? (octets / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " Go"
            : (octets / (1024d * 1024)).ToString("0", CultureInfo.InvariantCulture) + " Mo";

    private static string Texte(JsonElement element, string nom)
        => element.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? ""
            : "";

    private static int Entier(JsonElement element, string nom)
        => element.TryGetProperty(nom, out var valeur)
           && valeur.ValueKind == JsonValueKind.Number
           && valeur.TryGetInt32(out var lu)
               ? lu
               : 0;

    private static IReadOnlyList<string> Mots(JsonElement element, string nom)
    {
        var mots = new List<string>();

        if (element.TryGetProperty(nom, out var tableau) && tableau.ValueKind == JsonValueKind.Array)
        {
            foreach (var mot in tableau.EnumerateArray())
            {
                if (mot.ValueKind == JsonValueKind.String && mot.GetString() is { Length: > 0 } lu)
                {
                    mots.Add(lu);
                }
            }
        }

        return mots;
    }
}
