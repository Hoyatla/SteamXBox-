using System.Globalization;
using System.Text.Json;

namespace SenSÉ.Tools.Assistant;

/// <summary>Un moteur déclaré par son manifeste : ce qu'il est, où il écoute, ce qu'il sait faire.</summary>
/// <param name="Id">Son identifiant, unique dans la table.</param>
/// <param name="Nom">Ce qu'on montre à l'utilisateur.</param>
/// <param name="Role">La voie qu'il tient : dialogue, orchestre, codage, transcription, image, video.</param>
/// <param name="Cadre">Le programme qui l'exécute : <c>llama.cpp</c>, <c>whisper.cpp</c>, <c>sd.cpp</c>.</param>
/// <param name="Port">Sur quel port de la boucle locale il répond.</param>
/// <param name="Dossier">Le dossier du manifeste. Les chemins de <paramref name="Fichiers"/> lui sont relatifs.</param>
/// <param name="Fichiers">Les poids et leurs compagnons, en chemins ABSOLUS une fois résolus.</param>
/// <param name="Contexte">La place de travail en jetons, pour les moteurs qui en ont une.</param>
/// <param name="Arguments">Ce qu'il faut passer au programme, tel que le manifeste le dicte.</param>
/// <param name="Vision">Vrai s'il lit les images.</param>
/// <param name="Actif">Faux pour un moteur gardé sur le disque mais écarté — voir Ling.</param>
public sealed record Moteur(
    string Id,
    string Nom,
    string Role,
    string Cadre,
    int Port,
    string Dossier,
    IReadOnlyDictionary<string, string> Fichiers,
    int Contexte,
    IReadOnlyList<string> Arguments,
    bool Vision,
    bool Actif)
{
    /// <summary>Où l'interroger.</summary>
    public string Adresse => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Le fichier de poids principal, ou null s'il manque.</summary>
    public string? Poids => Fichiers.TryGetValue("modele", out var m) ? m : null;

    /// <summary>Le projecteur d'images, s'il en a un.</summary>
    public string? Projecteur => Fichiers.TryGetValue("projecteur", out var p) ? p : null;
}

/// <summary>
/// La table des moteurs : ce que la flottille contient, lu sur le disque plutôt qu'écrit en dur.
/// </summary>
/// <remarks>
/// <b>Ce que ceci remplace.</b> <see cref="ServeurModele"/> ne savait tenir qu'un modèle : un
/// <c>.gguf</c> au premier niveau de <c>Outils/Modeles</c>, un port, un processus. Essayer un
/// second modèle demandait donc de <i>remplacer</i> le premier — c'est exactement ce qui s'est
/// passé le 9 septembre 2026 quand le 9B a pris la place du 4B.
///
/// <para>
/// <b>La vision n'est pas une voie.</b> Elle est une capacité du modèle de texte, par son
/// projecteur. Le 9B lit une image et rend du <i>texte</i> — une description, une extraction, un
/// jugement — et ce texte circule ensuite comme n'importe quel autre. Vérifié dans le fichier :
/// son projecteur déclare <c>clip.has_vision_encoder</c> et une vingtaine de clés
/// <c>clip.vision.*</c>, aucune clé audio. Il <b>lit</b> les images, il n'en fabrique pas — c'est
/// l'affaire de sd.cpp — et il n'entend rien — c'est celle de whisper.cpp.
/// </para>
///
/// <para>
/// <b>Ce qui circule entre les moteurs est du texte et des chemins de fichiers</b>, jamais des
/// objets. C'est déjà le mécanisme de <see cref="FichierProduit"/> : un outil nomme ce qu'il vient
/// d'écrire, le suivant le reprend tel quel. La table n'a donc pas de bus à fournir, seulement des
/// adresses.
/// </para>
/// </remarks>
public static class Moteurs
{
    /// <summary>Le nom du manifeste, dans chaque dossier de rôle.</summary>
    private const string Manifeste = "modele.json";

    /// <summary>Où la table est lue. Null pour l'emplacement du produit.</summary>
    /// <remarks>
    /// Le même procédé que <see cref="FichierTravail.Racine"/>, et pour la même raison : une suite
    /// de tests ne doit ni lire les modèles réels — soixante-dix gigaoctets — ni dépendre de ce qui
    /// est installé sur la machine qui l'exécute.
    /// </remarks>
    public static string? Racine { get; set; }

    private static string Dossier
        => Racine ?? Path.Combine(AppContext.BaseDirectory, "Outils", "Modeles");

    /// <summary>
    /// Lit la table sur le disque. Un manifeste illisible est signalé et sauté, jamais fatal.
    /// </summary>
    /// <remarks>
    /// Un dossier de rôle sans manifeste n'est pas une erreur : <c>vision/</c> est vide et
    /// volontairement, il attend un modèle dédié le jour où il en vaudra la peine.
    ///
    /// <para>
    /// Les chemins déclarés sont résolus <b>relativement au dossier du manifeste</b>, et vérifiés.
    /// Un moteur dont un fichier manque est écarté de la table avec son nom au journal — mieux vaut
    /// une voie absente qu'une voie qui échoue au premier appel. C'est la règle déjà appliquée aux
    /// recherches distantes non configurées.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Moteur> Lire(Action<string>? journal = null)
    {
        if (!Directory.Exists(Dossier))
        {
            return [];
        }

        var table = new List<Moteur>();

        foreach (var manifeste in Directory.GetFiles(Dossier, Manifeste, SearchOption.AllDirectories))
        {
            if (Lire(manifeste, journal) is { } moteur)
            {
                table.Add(moteur);
            }
        }

        return table;
    }

    /// <summary>Un manifeste, ou null s'il est illisible ou incomplet.</summary>
    private static Moteur? Lire(string chemin, Action<string>? journal)
    {
        var dossier = Path.GetDirectoryName(chemin)!;

        try
        {
            using var lu = JsonDocument.Parse(File.ReadAllText(chemin));
            var racine = lu.RootElement;

            var id = Texte(racine, "id");

            if (id.Length == 0)
            {
                journal?.Invoke($"manifeste sans identifiant, ignoré : {chemin}");

                return null;
            }

            // Ecarte sans bruit : un moteur garde sur le disque mais retire du service le declare
            // par « actif: false ». Ling 3.0 tiny l'a ete apres mesure.
            if (racine.TryGetProperty("actif", out var actif)
                && actif.ValueKind == JsonValueKind.False)
            {
                journal?.Invoke($"moteur « {id} » écarté : son manifeste le déclare inactif.");

                return null;
            }

            var fichiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (racine.TryGetProperty("fichiers", out var declares)
                && declares.ValueKind == JsonValueKind.Object)
            {
                foreach (var paire in declares.EnumerateObject())
                {
                    if (paire.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var absolu = Path.GetFullPath(Path.Combine(dossier, paire.Value.GetString() ?? ""));

                    if (!File.Exists(absolu) && !Directory.Exists(absolu))
                    {
                        journal?.Invoke(
                            $"moteur « {id} » écarté : {paire.Name} introuvable ({paire.Value.GetString()}).");

                        return null;
                    }

                    fichiers[paire.Name] = absolu;
                }
            }

            return new Moteur(
                id,
                Texte(racine, "nom", id),
                Texte(racine, "role", "texte"),
                Texte(racine, "moteur", "llama.cpp"),
                Entier(racine, "port"),
                dossier,
                fichiers,
                Entier(racine, "contexte", ServeurModele.Contexte),
                Liste(racine, "arguments"),
                fichiers.ContainsKey("projecteur"),
                Actif: true);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            journal?.Invoke($"manifeste illisible, ignoré ({exception.GetType().Name}) : {chemin}");

            return null;
        }
    }

    /// <summary>Le moteur qui tient ce rôle, ou null.</summary>
    /// <remarks>
    /// Par rôle et non par identifiant : l'appelant demande « qui code ? », pas « où est
    /// Qwen 2.5 Coder 3B ? ». C'est ce qui permet de remplacer un modèle en déposant un fichier et
    /// en corrigeant son manifeste, sans qu'une ligne de code le nomme.
    /// </remarks>
    public static Moteur? Pour(string role, Action<string>? journal = null)
        => Lire(journal).FirstOrDefault(
            m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>Le moteur qui lit les images, ou null si aucun ne le sait.</summary>
    /// <remarks>
    /// <b>La vision se demande par capacité, jamais par rôle.</b> Elle appartient aujourd'hui au
    /// modèle de dialogue et pourrait demain appartenir à un modèle dédié : les appelants ne
    /// doivent pas avoir à le savoir. « Qui sait lire une image ? » survit au changement, « le
    /// modèle de texte » non.
    /// </remarks>
    public static Moteur? Voyant(Action<string>? journal = null)
        => Lire(journal).FirstOrDefault(m => m.Vision);

    /// <summary>Les rôles réellement disponibles, pour les montrer à l'aiguilleur.</summary>
    /// <remarks>
    /// L'aiguilleur ne doit connaître que ce qui existe : une voie proposée mais absente lui fait
    /// dépenser un tour et lui apprend à s'entêter. Même règle que pour les capacités de recherche
    /// non configurées.
    /// </remarks>
    public static IReadOnlyList<string> Voies(Action<string>? journal = null)
        => [.. Lire(journal).Select(m => m.Role).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];

    private static string Texte(JsonElement objet, string nom, string defaut = "")
        => objet.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? defaut
            : defaut;

    private static int Entier(JsonElement objet, string nom, int defaut = 0)
        => objet.TryGetProperty(nom, out var valeur)
           && valeur.ValueKind == JsonValueKind.Number
           && valeur.TryGetInt32(out var lu)
            ? lu
            : defaut;

    private static IReadOnlyList<string> Liste(JsonElement objet, string nom)
    {
        if (!objet.TryGetProperty(nom, out var valeur) || valeur.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. valeur.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => s.Length > 0)];
    }
}
