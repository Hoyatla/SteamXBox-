using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SenSÉ.Tools.Generation;

/// <summary>Ce qu'une entrée de nœud accepte.</summary>
/// <param name="Type">
/// Le type déclaré : <c>INT</c>, <c>FLOAT</c>, <c>STRING</c>, <c>BOOLEAN</c> pour une valeur écrite ;
/// <c>MODEL</c>, <c>LATENT</c>, <c>CONDITIONING</c> et les autres pour un câble ; <c>COMBO</c> quand
/// le serveur a énuméré les valeurs admises.
/// </param>
/// <param name="Valeurs">Les valeurs admises, quand il y en a une liste. Vide sinon.</param>
/// <param name="Min">La borne basse d'un nombre, si elle est déclarée.</param>
/// <param name="Max">La borne haute d'un nombre, si elle est déclarée.</param>
public sealed record EntreeSchema(
    string Type,
    IReadOnlyList<string> Valeurs,
    double? Min,
    double? Max)
{
    /// <summary>
    /// Cette entrée attend-elle un câble venu d'un autre nœud plutôt qu'une valeur écrite ?
    /// </summary>
    /// <remarks>
    /// La règle vient de ComfyUI et non d'une liste tenue à la main : tout ce qui n'est pas un des
    /// quatre types écrits, ni un choix, est un type de données qui circule entre nœuds. Une liste
    /// de types « à câbler » aurait vieilli au premier module tiers qui invente le sien.
    ///
    /// <para>
    /// Le choix se reconnaît au mot <c>COMBO</c> quelque part dans le type, et pas seulement au type
    /// <c>COMBO</c> tout court. ComfyUI en décline plusieurs — <c>COMFY_DYNAMICCOMBO_V3</c> pour un
    /// choix dont les options dépendent d'une autre entrée, par exemple le codec d'une vidéo, qui
    /// change avec le format. Constaté sur le flux livré : le codec de <c>SaveVideo</c> se voyait
    /// reprocher d'être une valeur écrite là où il n'a jamais rien attendu d'autre.
    /// </para>
    /// </remarks>
    public bool Cable => Valeurs.Count == 0
        && Type is not ("INT" or "FLOAT" or "STRING" or "BOOLEAN")
        && !Type.Contains("COMBO", StringComparison.Ordinal);
}

/// <summary>Ce qu'un nœud attend et ce qu'il rend.</summary>
/// <param name="Nom">Son <c>class_type</c>, le nom que le flux doit employer.</param>
/// <param name="Requises">Les entrées sans lesquelles il refuse de tourner.</param>
/// <param name="Optionnelles">Celles qu'il accepte en plus.</param>
/// <param name="Sorties">Le type de chaque sortie, dans l'ordre des rangs.</param>
/// <param name="EstSortie">Enregistre-t-il quelque chose ? Un flux sans cela ne produit rien.</param>
/// <param name="Description">Ce qu'il fait, dans les mots de son auteur.</param>
/// <param name="Categorie">Où il est rangé dans le menu.</param>
/// <param name="Alias">
/// Les mots sous lesquels son auteur veut qu'on le trouve — <c>sampler</c>, <c>txt2img</c>,
/// <c>denoise</c> pour <c>KSampler</c>. C'est ce qui rattrape l'écart entre l'intention d'une
/// demande et le nom d'une classe, que rien d'autre ne relie.
/// </param>
public sealed record NoeudSchema(
    string Nom,
    IReadOnlyDictionary<string, EntreeSchema> Requises,
    IReadOnlyDictionary<string, EntreeSchema> Optionnelles,
    IReadOnlyList<string> Sorties,
    bool EstSortie,
    string Description,
    string Categorie,
    IReadOnlyList<string> Alias)
{
    /// <summary>Le schéma d'une entrée, requise ou non.</summary>
    public EntreeSchema? Entree(string nom)
        => Requises.TryGetValue(nom, out var requise) ? requise
            : Optionnelles.TryGetValue(nom, out var libre) ? libre
            : null;
}

/// <summary>
/// Ce que le générateur installé sait faire, tel qu'il le déclare lui-même.
/// </summary>
/// <remarks>
/// <b>La source est la machine, jamais le souvenir.</b> Un modèle de langage a lu le ComfyUI public
/// pendant son entraînement : il connaît des nœuds qui ne sont pas installés ici, ignore ceux des
/// modules ajoutés depuis, et n'a évidemment aucune idée des fichiers présents sur ce disque. Le
/// point d'entrée <c>/object_info</c>, lui, énumère les valeurs réellement admises — la liste des
/// modèles installés apparaît telle quelle dans le schéma du nœud qui les charge.
///
/// <para>
/// <b>Il ne tient pas dans un contexte, et c'est un fait mesuré</b>, pas une prudence : 1084
/// classes de nœuds pour 1,4 Mo de schéma, soit de l'ordre de trois cent cinquante mille jetons —
/// quarante fois ce que le modèle peut lire d'un coup. Tout ce qui s'appuie sur ce catalogue doit
/// donc le réduire avant de le montrer. C'est la contrainte structurante, pas un détail
/// d'optimisation.
/// </para>
/// </remarks>
public sealed class Catalogue
{
    private readonly Dictionary<string, NoeudSchema> _noeuds;

    private Catalogue(Dictionary<string, NoeudSchema> noeuds) => _noeuds = noeuds;

    /// <summary>Combien de nœuds le générateur déclare.</summary>
    public int Compte => _noeuds.Count;

    /// <summary>Tous les noms déclarés.</summary>
    public IEnumerable<string> Noms => _noeuds.Keys;

    /// <summary>Le schéma d'un nœud, ou null s'il n'existe pas ici.</summary>
    public NoeudSchema? Noeud(string nom)
        => nom.Length > 0 && _noeuds.TryGetValue(nom, out var trouve) ? trouve : null;

    /// <summary>
    /// Le nom réellement installé qui ressemble le plus à celui-ci, s'il y en a un.
    /// </summary>
    /// <remarks>
    /// Un modèle se trompe surtout de casse ou de séparateur — <c>ksampler</c>, <c>KSampler</c>,
    /// <c>K_Sampler</c>. Lui rendre « nœud inconnu » sans plus le laisse recommencer au hasard ;
    /// lui rendre le nom voisin le remet sur les rails en un tour.
    /// </remarks>
    public string? Voisin(string nom)
    {
        if (nom.Length == 0)
        {
            return null;
        }

        var nu = Nu(nom);

        foreach (var connu in _noeuds.Keys)
        {
            if (Nu(connu) == nu)
            {
                return connu;
            }
        }

        return null;

        static string Nu(string texte)
            => string.Concat(texte.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    /// <summary>
    /// Les valeurs admises pour une entrée désignée <c>NomDuNoeud.nom_de_l_entree</c>.
    /// </summary>
    /// <remarks>
    /// C'est par là qu'un manifeste cesse de geler des noms de fichiers. Vide si le nœud n'est pas
    /// installé, si l'entrée n'existe pas, ou si elle n'est pas un choix — dans les trois cas
    /// l'appelant garde ce que le manifeste proposait, plutôt que de rendre une liste vide.
    /// </remarks>
    public IReadOnlyList<string> Options(string designation)
    {
        var point = (designation ?? "").IndexOf('.', StringComparison.Ordinal);

        if (point <= 0 || point == designation!.Length - 1)
        {
            return [];
        }

        return Noeud(designation[..point])?.Entree(designation[(point + 1)..])?.Valeurs ?? [];
    }

    /// <summary>Lit un document <c>/object_info</c>.</summary>
    public static Catalogue Lire(string json)
    {
        var noeuds = new Dictionary<string, NoeudSchema>(StringComparer.Ordinal);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new Catalogue(noeuds);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Catalogue(noeuds);
            }

            foreach (var propriete in document.RootElement.EnumerateObject())
            {
                if (propriete.Value.ValueKind == JsonValueKind.Object)
                {
                    noeuds[propriete.Name] = Noeud(propriete.Name, propriete.Value);
                }
            }
        }

        return new Catalogue(noeuds);
    }

    private static NoeudSchema Noeud(string nom, JsonElement corps)
    {
        var entrees = corps.TryGetProperty("input", out var bloc)
            && bloc.ValueKind == JsonValueKind.Object
                ? bloc
                : default;

        return new NoeudSchema(
            nom,
            Entrees(entrees, "required"),
            Entrees(entrees, "optional"),
            Sorties(corps),
            corps.TryGetProperty("output_node", out var sortie)
                && sortie.ValueKind == JsonValueKind.True,
            Texte(corps, "description"),
            Texte(corps, "category"),
            Liste(corps, "search_aliases"));
    }

    private static IReadOnlyList<string> Liste(JsonElement corps, string nom)
    {
        var mots = new List<string>();

        if (corps.TryGetProperty(nom, out var tableau) && tableau.ValueKind == JsonValueKind.Array)
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

    private static Dictionary<string, EntreeSchema> Entrees(JsonElement bloc, string quelles)
    {
        var lues = new Dictionary<string, EntreeSchema>(StringComparer.Ordinal);

        if (bloc.ValueKind != JsonValueKind.Object
            || !bloc.TryGetProperty(quelles, out var groupe)
            || groupe.ValueKind != JsonValueKind.Object)
        {
            return lues;
        }

        foreach (var entree in groupe.EnumerateObject())
        {
            if (Entree(entree.Value) is { } schema)
            {
                lues[entree.Name] = schema;
            }
        }

        return lues;
    }

    /// <summary>
    /// Une entrée s'écrit <c>[type, {contraintes}]</c>, et le type est parfois la liste elle-même.
    /// </summary>
    /// <remarks>
    /// C'est ainsi que ComfyUI exprime un choix : à la place d'un nom de type, il met le tableau des
    /// valeurs admises. C'est de là que sort la liste des modèles réellement installés, et c'est ce
    /// qui permet de refuser un nom de modèle absent du disque sans rien connaître du disque.
    /// </remarks>
    private static EntreeSchema? Entree(JsonElement declaration)
    {
        if (declaration.ValueKind != JsonValueKind.Array || declaration.GetArrayLength() == 0)
        {
            return null;
        }

        var premier = declaration[0];
        var valeurs = new List<string>();
        var type = "";

        if (premier.ValueKind == JsonValueKind.Array)
        {
            type = "COMBO";

            foreach (var valeur in premier.EnumerateArray())
            {
                valeurs.Add(valeur.ValueKind == JsonValueKind.String
                    ? valeur.GetString() ?? ""
                    : valeur.ToString());
            }
        }
        else if (premier.ValueKind == JsonValueKind.String)
        {
            type = premier.GetString() ?? "";
        }
        else
        {
            return null;
        }

        double? min = null;
        double? max = null;

        if (declaration.GetArrayLength() > 1 && declaration[1].ValueKind == JsonValueKind.Object)
        {
            min = Nombre(declaration[1], "min");
            max = Nombre(declaration[1], "max");

            // La seconde écriture d'un choix, celle vers laquelle ComfyUI migre ses nœuds.
            //
            // L'ancienne mettait les valeurs en première position, à la place du nom de type ;
            // la nouvelle écrit « ["COMBO", { "options": [...] }] ». Le nœud qui charge un dossier
            // d'images est déjà passé à cette forme, et nous n'y lisions rien : le panneau
            // n'offrait aucun dossier, « regler_option » répondait « Valeurs possibles : . », et le
            // journal disait « ne rend rien sur cette machine » — ce qui accuse la machine d'un
            // défaut de lecture qui est le nôtre.
            //
            // C'est la panne que le mécanisme d'options dynamiques était censé empêcher : un outil
            // qui se fige parce que la plateforme a bougé. Elle serait revenue nœud par nœud, au
            // rythme de leur migration, sans jamais ressembler à autre chose qu'une installation
            // incomplète.
            if (valeurs.Count == 0
                && declaration[1].TryGetProperty("options", out var choix)
                && choix.ValueKind == JsonValueKind.Array)
            {
                foreach (var valeur in choix.EnumerateArray())
                {
                    valeurs.Add(valeur.ValueKind == JsonValueKind.String
                        ? valeur.GetString() ?? ""
                        : valeur.ToString());
                }
            }
        }

        return new EntreeSchema(type, valeurs, min, max);
    }

    private static IReadOnlyList<string> Sorties(JsonElement corps)
    {
        var types = new List<string>();

        if (corps.TryGetProperty("output", out var sorties)
            && sorties.ValueKind == JsonValueKind.Array)
        {
            foreach (var type in sorties.EnumerateArray())
            {
                types.Add(type.ValueKind == JsonValueKind.String ? type.GetString() ?? "" : "");
            }
        }

        return types;
    }

    private static double? Nombre(JsonElement bloc, string nom)
        => bloc.TryGetProperty(nom, out var valeur)
            && valeur.ValueKind == JsonValueKind.Number
            && valeur.TryGetDouble(out var lu)
                ? lu
                : null;

    private static string Texte(JsonElement bloc, string nom)
        => bloc.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? ""
            : "";

    /// <summary>Demande son catalogue au générateur qui tourne.</summary>
    /// <remarks>
    /// Gardé en mémoire pour la vie du processus : le document pèse plus d'un mégaoctet et ne change
    /// pas tant que le serveur tourne. Il change en revanche entre deux démarrages — un modèle
    /// ajouté sur le disque apparaît dans les valeurs admises — donc rien n'est écrit sur disque.
    /// </remarks>
    /// <param name="frais">
    /// Vrai pour redemander le catalogue au lieu de rendre celui qu'on garde. Nécessaire dès qu'un
    /// fichier a été déposé : les valeurs admises d'une entrée sont la liste des fichiers présents,
    /// et une image tout juste copiée serait jugée inexistante par un catalogue d'avant la copie.
    /// </param>
    public static Catalogue? Demander(int port, Action<string>? journal, bool frais = false)
    {
        if (_connu is not null && !frais)
        {
            return _connu;
        }

        var adresse = "http://127.0.0.1:"
            + port.ToString(CultureInfo.InvariantCulture) + "/object_info";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var reponse = client.GetAsync(adresse).GetAwaiter().GetResult();

            if (!reponse.IsSuccessStatusCode)
            {
                journal?.Invoke($"catalogue refusé : {(int)reponse.StatusCode}");

                return null;
            }

            var lu = Lire(reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            if (lu.Compte == 0)
            {
                journal?.Invoke("catalogue vide ou illisible.");

                return null;
            }

            journal?.Invoke($"catalogue lu : {lu.Compte.ToString(CultureInfo.InvariantCulture)} nœuds.");
            _connu = lu;

            return lu;
        }
        catch (HttpRequestException exception)
        {
            journal?.Invoke($"catalogue injoignable : {exception.Message}");

            return null;
        }
        catch (TaskCanceledException)
        {
            journal?.Invoke("le générateur n'a pas rendu son catalogue à temps.");

            return null;
        }
    }

    /// <summary>Oublie le catalogue gardé. À appeler quand le serveur s'arrête.</summary>
    public static void Oublier() => _connu = null;

    private static Catalogue? _connu;
}
