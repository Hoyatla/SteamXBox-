using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamXBox.Tools.Assistant;

/// <summary>Une chose à faire, et si elle l'est.</summary>
public sealed class Tache
{
    [JsonPropertyName("texte")]
    public string Texte { get; set; } = "";

    [JsonPropertyName("faite")]
    public bool Faite { get; set; }
}

/// <summary>Ce que l'assistant s'est noté pour une demande.</summary>
public sealed class Travail
{
    [JsonPropertyName("titre")]
    public string Titre { get; set; } = "";

    [JsonPropertyName("cree")]
    public DateTime Cree { get; set; }

    /// <summary>La dernière fois qu'on l'a ouvert, lu ou modifié.</summary>
    [JsonPropertyName("touche")]
    public DateTime Touche { get; set; }

    /// <summary>
    /// L'utilisateur a-t-il donné son accord au plan ?
    /// </summary>
    /// <remarks>
    /// <b>Un plan se montre avant de s'exécuter.</b> Lancer six générations de cinq minutes sur une
    /// intention mal comprise coûte une demi-heure et se découvre à la fin. La consigne demande donc
    /// à l'assistant de présenter son plan et d'attendre ; mais une consigne est un espoir, pas une
    /// garantie, sur un modèle de quatre milliards de paramètres.
    ///
    /// <para>
    /// D'où ce drapeau, qui rend l'espoir vérifiable : tant qu'il est faux, le carnet s'affiche
    /// « en attente de votre accord » dans la fenêtre. Si l'assistant se lance quand même,
    /// l'utilisateur le voit — un écart visible plutôt qu'un écart silencieux.
    /// </para>
    /// </remarks>
    [JsonPropertyName("accepte")]
    public bool Accepte { get; set; }

    [JsonPropertyName("taches")]
    public List<Tache> Taches { get; set; } = [];

    /// <summary>Tout est-il coché ?</summary>
    public bool Fini => Taches.Count > 0 && Taches.TrueForAll(t => t.Faite);
}

/// <summary>
/// Le carnet de l'assistant : ce qu'il a à faire, écrit ailleurs que dans sa tête.
/// </summary>
/// <remarks>
/// <b>Pourquoi un fichier plutôt qu'une mémoire.</b> Le modèle travaille sur huit mille jetons,
/// partagés avec la conversation, la déclaration de tous les outils et son propre raisonnement —
/// et une seule image lui en coûte mille. Une demande en dix étapes ne tient pas dedans : au
/// huitième tour, le début a disparu. Écrire le plan et le relire coûte quelques dizaines de
/// jetons au lieu de tout garder.
///
/// <para>
/// <b>Un seul objet, trois usages.</b> Le même fichier est la mémoire de travail de l'assistant, la
/// trace que l'utilisateur peut lire pendant qu'il travaille, et ce qui disparaît une fois la chose
/// faite. Trois mécanismes séparés auraient divergé — celui qu'on regarde et celui qui sert — et
/// c'est toujours celui qu'on ne regarde pas qui ment.
/// </para>
///
/// <para>
/// <b>Ce n'est pas l'assistant qui décide que c'est fini.</b> Un modèle qui déclare sa tâche
/// accomplie se trompe avec aplomb : celui-ci a déjà annoncé un générateur lancé qui ne l'était
/// pas. L'effacement demande donc la confirmation de l'utilisateur — ou, à défaut, un mois sans
/// que personne ne rouvre le carnet, ce qui est la seule preuve d'abandon qu'une machine puisse
/// constater seule.
/// </para>
/// </remarks>
public static class FichierTravail
{
    /// <summary>
    /// Au bout de combien de temps sans ouverture un carnet est considéré comme abandonné.
    /// </summary>
    /// <remarks>
    /// Un mois, et compté depuis la dernière ouverture et non depuis la création : un travail qu'on
    /// reprend chaque semaine ne vieillit jamais, un travail oublié disparaît de lui-même. Effacer
    /// sur l'âge de création aurait emporté des carnets encore vivants.
    /// </remarks>
    public static readonly TimeSpan Oubli = TimeSpan.FromDays(30);

    /// <summary>
    /// Le dossier parent des carnets. Null pour celui du produit ; sert aux épreuves.
    /// </summary>
    /// <remarks>
    /// Une variable modifiable pour l'épreuve n'est pas élégante, et l'alternative l'était moins :
    /// passer le dossier à chacune des sept méthodes aurait encombré tous les appels du produit
    /// pour le seul confort des tests. Les épreuves écrivent ainsi dans un bac temporaire au lieu
    /// de semer des carnets à côté de leurs binaires.
    /// </remarks>
    public static string? Racine { get; set; }

    /// <summary>Où vivent les carnets, à côté du produit — donc portables avec lui.</summary>
    public static string Dossier
        => Path.Combine(Racine ?? AppContext.BaseDirectory, "Travaux");

    private static readonly JsonSerializerOptions Ecriture = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Le fichier d'un carnet, nommé d'après son titre.</summary>
    /// <remarks>
    /// Le titre porte le nom du fichier pour qu'on retrouve un carnet en regardant le dossier, sans
    /// ouvrir quoi que ce soit. Réduit aux lettres, chiffres et tirets : un titre est écrit par un
    /// modèle de langage, et rien n'empêcherait un deux-points ou une barre oblique d'y arriver.
    /// </remarks>
    public static string Fichier(string titre) => Path.Combine(Dossier, Nom(titre) + ".json");

    private static string Nom(string titre)
    {
        var propre = new StringBuilder();

        foreach (var c in (titre ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                propre.Append(c);
            }
            else if (propre.Length > 0 && propre[^1] != '-')
            {
                propre.Append('-');
            }
        }

        var nom = propre.ToString().Trim('-');

        // Un titre vide, ou fait uniquement de ponctuation, donnerait un fichier « .json » caché.
        return nom.Length == 0 ? "travail" : nom[..Math.Min(nom.Length, 60)];
    }

    /// <summary>Écrit un carnet : le titre, et ce qu'il y a à faire.</summary>
    /// <remarks>
    /// Réécrire un carnet existant garde ce qui était déjà coché : l'assistant qui reprécise son
    /// plan ne doit pas effacer le travail accompli, sans quoi une demande révisée recommencerait
    /// tout.
    /// </remarks>
    public static Travail Noter(string titre, IEnumerable<string> taches, Action<string>? journal)
    {
        var ancien = Lire(titre, journal);
        var maintenant = DateTime.UtcNow;

        var travail = new Travail
        {
            Titre = (titre ?? "").Trim(),
            Cree = ancien?.Cree ?? maintenant,
            Touche = maintenant,

            // Un accord déjà donné survit à une révision du plan : l'assistant qui ajoute une étape
            // en cours de route ne doit pas redemander la permission de continuer. Un carnet neuf,
            // lui, attend.
            Accepte = ancien?.Accepte ?? false,
        };

        foreach (var texte in taches.Select(t => (t ?? "").Trim()).Where(t => t.Length > 0))
        {
            var deja = ancien?.Taches.Find(t =>
                t.Texte.Equals(texte, StringComparison.OrdinalIgnoreCase));

            travail.Taches.Add(new Tache { Texte = texte, Faite = deja?.Faite ?? false });
        }

        Ecrire(travail, journal);

        return travail;
    }

    /// <summary>Coche une tâche. Rend null si le carnet ou la tâche n'existe pas.</summary>
    public static Travail? Cocher(string titre, string tache, Action<string>? journal)
    {
        if (Lire(titre, journal) is not { } travail)
        {
            return null;
        }

        var cherche = (tache ?? "").Trim();

        // Par le début du texte, pas par égalité stricte : l'assistant reformule en recopiant, et
        // exiger le mot à mot ferait échouer un cochage parfaitement clair.
        var trouvee = travail.Taches.Find(t =>
            t.Texte.Equals(cherche, StringComparison.OrdinalIgnoreCase))
            ?? travail.Taches.Find(t =>
                t.Texte.StartsWith(cherche, StringComparison.OrdinalIgnoreCase)
                || cherche.StartsWith(t.Texte, StringComparison.OrdinalIgnoreCase))

            // Sur le tronc, une fois retirés le numéro de liste et la parenthèse finale.
            //
            // Ce sont les deux formes que le modèle produit réellement. Il énonce le plan à
            // l'utilisateur en numérotant — « 1. Choisir le type de vidéo à créer » — puis coche
            // avec sa propre énumération ; et il réécrit les parenthèses en les reformulant, si bien
            // que « (image fixe -> vidéo, montage photos, etc.) » revient en « (animer une image
            // fixe, assembler une séquence de photos) ». Ni l'un ni l'autre n'est un préfixe, et le
            // cochage échouait sur une étape que tout lecteur humain aurait reconnue.
            ?? travail.Taches.Find(t =>
            {
                var gauche = Tronc(t.Texte);
                var droite = Tronc(cherche);

                return gauche.Length > 0
                       && droite.Length > 0
                       && (gauche.StartsWith(droite, StringComparison.OrdinalIgnoreCase)
                           || droite.StartsWith(gauche, StringComparison.OrdinalIgnoreCase));
            });

        if (trouvee is null)
        {
            return null;
        }

        trouvee.Faite = true;
        travail.Touche = DateTime.UtcNow;
        Ecrire(travail, journal);

        return travail;
    }

    /// <summary>
    /// Le tronc d'une étape : sans son numéro de liste, sans sa parenthèse d'exemples.
    /// </summary>
    /// <remarks>
    /// Deux retraits, et pas un de plus. Chacun correspond à une forme observée dans une session
    /// réelle ; une normalisation plus large — retirer la ponctuation, replier les accents,
    /// rapprocher par mots communs — cocherait un jour la mauvaise étape, et un carnet qui déclare
    /// fait ce qui ne l'est pas vaut moins que pas de carnet du tout.
    /// </remarks>
    private static string Tronc(string texte)
    {
        // Les trois tirets, pas seulement celui du clavier : le modèle écrit en typographie
        // française et produit des cadratins. Ne retirer que le trait d'union laissait échouer la
        // forme la plus courante de ses énumérations.
        var reste = texte.TrimStart(' ', '\t', '-', '–', '—', '*', '•', '.', ')');

        // « 12. » ou « 3) » en tête : le numéro qu'ajoute une énumération.
        var chiffres = 0;

        while (chiffres < reste.Length && char.IsDigit(reste[chiffres]))
        {
            chiffres++;
        }

        if (chiffres > 0)
        {
            reste = reste[chiffres..].TrimStart(' ', '.', ')', '-', '–', '—', ':');
        }

        var parenthese = reste.IndexOf('(', StringComparison.Ordinal);

        if (parenthese > 0)
        {
            reste = reste[..parenthese];
        }

        return reste.Trim();
    }

    /// <summary>Enregistre l'accord de l'utilisateur sur un plan.</summary>
    /// <remarks>
    /// Appelé par l'assistant quand l'utilisateur a dit oui, ou par la fenêtre quand il presse le
    /// bouton. Les deux chemins mènent au même drapeau : l'accord donné à l'oral et l'accord donné
    /// au clic sont le même accord, et en tenir deux traces ferait diverger ce que le modèle croit
    /// de ce que l'utilisateur voit.
    /// </remarks>
    public static Travail? Accepter(string titre, Action<string>? journal)
    {
        if (Lire(titre, journal) is not { } travail)
        {
            return null;
        }

        travail.Accepte = true;
        travail.Touche = DateTime.UtcNow;
        Ecrire(travail, journal);

        return travail;
    }

    /// <summary>Relit un carnet, et note qu'on l'a ouvert.</summary>
    public static Travail? Lire(string titre, Action<string>? journal, bool toucher = false)
    {
        var fichier = Fichier(titre);

        if (!File.Exists(fichier))
        {
            return null;
        }

        try
        {
            var travail = JsonSerializer.Deserialize<Travail>(File.ReadAllText(fichier));

            if (travail is null)
            {
                return null;
            }

            if (toucher)
            {
                travail.Touche = DateTime.UtcNow;
                Ecrire(travail, journal);
            }

            return travail;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            journal?.Invoke($"carnet illisible : {exception.Message}");

            return null;
        }
    }

    /// <summary>Tous les carnets en cours, du plus récemment ouvert au plus ancien.</summary>
    public static IReadOnlyList<Travail> Lister(Action<string>? journal)
    {
        if (!Directory.Exists(Dossier))
        {
            return [];
        }

        var carnets = new List<Travail>();

        foreach (var fichier in Directory.GetFiles(Dossier, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Travail>(File.ReadAllText(fichier)) is { } travail)
                {
                    carnets.Add(travail);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                journal?.Invoke($"carnet illisible, ignoré : {Path.GetFileName(fichier)}");
            }
        }

        return [.. carnets.OrderByDescending(t => t.Touche)];
    }

    /// <summary>Efface un carnet. C'est la confirmation de l'utilisateur qui l'autorise.</summary>
    public static bool Effacer(string titre, Action<string>? journal)
    {
        var fichier = Fichier(titre);

        try
        {
            if (!File.Exists(fichier))
            {
                return false;
            }

            File.Delete(fichier);
            journal?.Invoke($"carnet effacé : {Path.GetFileName(fichier)}");

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal?.Invoke($"carnet non effacé : {exception.Message}");

            return false;
        }
    }

    /// <summary>
    /// Efface les carnets que personne n'a rouverts depuis un mois.
    /// </summary>
    /// <remarks>
    /// La seule preuve d'abandon qu'une machine puisse constater seule. Sans cela, un dossier de
    /// carnets à moitié faits s'accumulerait indéfiniment — et un produit qui laisse des traces
    /// qu'il ne nettoie pas finit par en être jugé.
    /// </remarks>
    public static int Purger(Action<string>? journal)
    {
        if (!Directory.Exists(Dossier))
        {
            return 0;
        }

        var limite = DateTime.UtcNow - Oubli;
        var effaces = 0;

        foreach (var fichier in Directory.GetFiles(Dossier, "*.json"))
        {
            try
            {
                var travail = JsonSerializer.Deserialize<Travail>(File.ReadAllText(fichier));

                if (travail is null || travail.Touche > limite)
                {
                    continue;
                }

                File.Delete(fichier);
                effaces++;

                var jours = (DateTime.UtcNow - travail.Touche).TotalDays
                    .ToString("F0", CultureInfo.InvariantCulture);

                journal?.Invoke($"carnet abandonné depuis {jours} jours, effacé : « {travail.Titre} »");
            }
            catch (Exception exception)
                when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                journal?.Invoke($"carnet non purgé : {Path.GetFileName(fichier)}");
            }
        }

        return effaces;
    }

    /// <summary>Un carnet, écrit pour être lu — par le modèle comme par l'utilisateur.</summary>
    public static string Resumer(Travail travail)
    {
        var texte = new StringBuilder()
            .Append(travail.Titre)
            .Append("  (")
            .Append(travail.Taches.Count(t => t.Faite).ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(travail.Taches.Count.ToString(CultureInfo.InvariantCulture))
            .Append(')')
            .AppendLine(travail.Accepte ? "" : "  — EN ATTENTE DE L'ACCORD DE L'UTILISATEUR");

        foreach (var tache in travail.Taches)
        {
            texte.Append(tache.Faite ? "  [x] " : "  [ ] ").AppendLine(tache.Texte);
        }

        return texte.ToString();
    }

    private static void Ecrire(Travail travail, Action<string>? journal)
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            File.WriteAllText(Fichier(travail.Titre), JsonSerializer.Serialize(travail, Ecriture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal?.Invoke($"carnet non écrit : {exception.Message}");
        }
    }
}
