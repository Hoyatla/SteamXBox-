using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SenSÉ.Tools.Assistant;

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

    // Le drapeau « accepte » a été retiré, et avec lui le bouton « Je suis d'accord ».
    //
    // Il protégeait d'un plan lancé sur une intention mal comprise. Le prix s'est révélé plus
    // élevé que le risque : une demande déjà formulée devait être approuvée une seconde fois,
    // et l'assistant s'arrêtait au milieu d'un travail que l'utilisateur venait de commander
    // pour redemander la permission de l'exécuter. Demander deux fois n'est pas plus sûr,
    // c'est seulement plus lent — et ça apprend à cliquer sans lire.
    //
    // Ce qui reste, et qui protège vraiment, ce sont les gardes sur les actes irréversibles :
    // rien ne s'installe, rien ne s'écrase, rien ne se ferme sans que l'utilisateur le dise.
    // Les anciens carnets portent encore ce champ dans leur JSON ; il est ignoré à la lecture.

    [JsonPropertyName("taches")]
    public List<Tache> Taches { get; set; } = [];

    /// <summary>
    /// Ce qui a été établi en chemin, et qu'il ne faut plus redemander.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Un carnet portait les étapes, jamais ce qu'on avait
    /// appris en les faisant. Mesuré le 24 août : l'utilisateur donne le sujet de sa vidéo, le
    /// contexte se remplit, l'historique s'élague — et à « reprends le travail » l'assistant
    /// redemande le sujet qu'on venait de lui donner. Les étapes avaient survécu, la matière non.
    ///
    /// <para>
    /// Ce que ça porte : ce que l'utilisateur a dit une fois, ce qu'un outil a répondu et qu'on ne
    /// veut pas relancer, le nom exact d'un nœud dont le nom se devine mal. Une ligne par fait,
    /// courte. C'est la moitié du carnet qui rend une reprise possible — les étapes disent quoi
    /// faire, les acquis disent avec quoi.
    /// </para>
    /// </remarks>
    [JsonPropertyName("acquis")]
    public List<string> Acquis { get; set; } = [];

    /// <summary>
    /// Ce que l'utilisateur a demandé, mot pour mot, quand ce travail est né.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Les acquis dépendent du modèle : c'est à lui d'appeler
    /// <c>travail_retenir</c>, et sur un modèle de quatre milliards de paramètres c'est un espoir,
    /// pas une garantie. Mesuré le 6 septembre
    /// 2026 : le contexte sature en plein travail, le fil repart neuf, et l'assistant rappelle
    /// <c>focus_and_type</c> avec <c>texte:""</c>. Le texte de deux cents caractères qu'on lui
    /// avait demandé d'écrire n'existait plus nulle part — ni dans le fil élagué, ni dans un
    /// acquis qu'il n'avait pas pensé à noter.
    ///
    /// <para>D'où un champ à part, rempli par le code au moment où le travail est créé, et jamais
    /// par le modèle. Les étapes disent quoi faire, les acquis disent avec quoi, la demande dit
    /// pourquoi — et elle est la seule des trois qu'on ne peut pas reconstituer après coup.</para>
    /// </remarks>
    [JsonPropertyName("demande")]
    public string Demande { get; set; } = "";

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

    /// <summary>
    /// La demande de l'utilisateur pour le tour en cours, telle qu'il l'a écrite.
    /// </summary>
    /// <remarks>
    /// Posée par <c>AssistantLocal.Repondre</c> au début de chaque tour, lue par
    /// <see cref="Noter"/> quand un travail naît. Un statique parce que le carnet est écrit
    /// depuis une Capacité, à laquelle le tour courant n'est pas passé — et que faire descendre
    /// la demande jusque-là traverserait quatre signatures pour un seul lecteur.
    ///
    /// <para>Tronquée : ce champ voyage dans chaque rappel, et une demande de plusieurs milliers
    /// de caractères mangerait le budget qu'elle est censée protéger.</para>
    /// </remarks>
    public static string DemandeCourante
    {
        get => _demandeCourante;
        set
        {
            var texte = (value ?? "").Trim();
            const int maximum = 1500;
            _demandeCourante = texte.Length <= maximum ? texte : texte[..maximum] + "…";
        }
    }

    private static string _demandeCourante = "";

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

    /// <summary>Une liste écrite par le modèle, découpée en étapes quoi qu'il ait choisi.</summary>
    /// <remarks>
    /// <b>La consigne demande des points-virgules ; le modèle écrit des listes numérotées.</b>
    /// Constaté en clair : « Ouvre LibreOffice, écris Bonjour et sauvegarde. Puis ouvre le
    /// dossier » a produit <c>"1. Ouvrir LibreOffice\n2. Écrire..\n3. Sauvegarder..\n4. Ouvrir.."</c>
    /// en un seul morceau. Le carnet affichait <c>0/1</c>, l'assistant cochait sa tâche unique après
    /// la première étape, et les trois autres n'existaient pour personne.
    ///
    /// <para>
    /// Exiger la bonne syntaxe d'un modèle de quatre milliards de paramètres est un vœu ; accepter
    /// les deux écritures est un correctif. Le point-virgule et le saut de ligne séparent tous
    /// deux, et la numérotation de tête est retirée — elle ferait échouer <see cref="Cocher"/>, qui
    /// compare le texte de l'étape mot pour mot.
    /// </para>
    ///
    /// <para>
    /// La puce n'est retirée que suivie d'une espace, ce qui laisse « 3.5 mm » entier.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Decouper(string? liste)
    {
        if (string.IsNullOrWhiteSpace(liste))
        {
            return [];
        }

        return [.. liste
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(morceau => Puce.Replace(morceau.Trim(), ""))
            .Select(morceau => morceau.Trim())
            .Where(morceau => morceau.Length > 0)];
    }

    /// <summary>Une numérotation ou un tiret de tête, et l'espace qui suit.</summary>
    private static readonly System.Text.RegularExpressions.Regex Puce = new(
        @"^(?:\d{1,2}[.)\]:]|[-*•–])\s+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

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

            // Les acquis survivent aussi, et pour une raison plus forte encore : réviser un plan ne
            // rend pas faux ce qu'on a appris en l'exécutant. Les perdre ici rendrait la reprise
            // impossible au moment précis où elle sert — quand le plan s'est révélé trop court.
            Acquis = ancien is null ? [] : [.. ancien.Acquis],

            // La demande d'origine ne se remplace pas. Reviser un plan, ajouter une etape,
            // reprendre apres un contexte plein : rien de tout cela ne change ce que
            // l'utilisateur a demande au depart, et c'est justement au moment de la reprise
            // que le tour courant ne porte plus qu'un « reprends le travail ».
            Demande = string.IsNullOrWhiteSpace(ancien?.Demande)
                ? DemandeCourante
                : ancien!.Demande,
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

    /// <summary>Les étapes qui restent à faire.</summary>
    /// <remarks>
    /// Ce que <see cref="Effacer"/> consulte avant de refermer, et ce que la fenêtre nomme dans sa
    /// demande de confirmation : « il reste deux étapes » vaut mieux que « êtes-vous sûr ».
    /// </remarks>
    public static IReadOnlyList<string> Restantes(Travail travail)
        => [.. travail.Taches.Where(tache => !tache.Faite).Select(tache => tache.Texte)];

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

            var soigne = Redecouper(travail, journal);

            if (toucher || soigne)
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

    /// <summary>Répare un carnet dont une étape en contient plusieurs. Vrai s'il a changé.</summary>
    /// <remarks>
    /// <b>Corriger l'écriture des carnets neufs ne suffisait pas : les anciens restaient piégés.</b>
    /// Celui de la session du 7 septembre 2026 portait une étape unique dont le texte était
    /// <c>"1. Ouvrir LibreOffice\n2. Écrire..\n3. Sauvegarder..\n4. Ouvrir le dossier"</c>. Les
    /// conséquences se lisent dans le journal : le carnet s'affichait <c>1/1</c>,
    /// <see cref="Cocher"/> reconnaissait cette étape sur son début — « 1. Ouvrir LibreOffice » —
    /// et la cochait entière, le travail passait pour fini alors que trois étapes n'avaient pas été
    /// faites, puis les trois appels suivants échouaient sur un carnet qui affirmait pourtant les
    /// contenir.
    ///
    /// <para>
    /// <b>Seul le premier morceau garde la coche.</b> C'est la lecture prudente et la seule
    /// défendable : l'assistant a coché cette étape après avoir fait la première chose qu'elle
    /// nommait, et ce geste ne dit rien des trois autres. Les rouvrir peut faire refaire une étape ;
    /// les fermer ferait perdre le travail sans que personne ne s'en aperçoive.
    /// </para>
    /// </remarks>
    private static bool Redecouper(Travail travail, Action<string>? journal)
    {
        if (!travail.Taches.Exists(t => t.Texte.Contains('\n') || t.Texte.Contains('\r')))
        {
            return false;
        }

        var soignees = new List<Tache>();

        foreach (var tache in travail.Taches)
        {
            var morceaux = Decouper(tache.Texte);

            if (morceaux.Count <= 1)
            {
                // Une étape d'une seule ligne est laissée telle quelle, coche comprise.
                soignees.Add(tache);
                continue;
            }

            for (var rang = 0; rang < morceaux.Count; rang++)
            {
                soignees.Add(new Tache { Texte = morceaux[rang], Faite = rang == 0 && tache.Faite });
            }
        }

        journal?.Invoke(
            $"carnet « {travail.Titre} » : {travail.Taches.Count} étape(s) mal écrite(s) redécoupée(s) en {soignees.Count}.");

        travail.Taches = soignees;

        return true;
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
                    // Réparé ici aussi, et pas seulement dans Lire : c'est par cette liste que
                    // passent le rappel de la consigne et la reprise après contexte plein. Un
                    // carnet soigné d'un côté et lu de travers de l'autre serait pire que pas de
                    // réparation du tout.
                    if (Redecouper(travail, journal))
                    {
                        Ecrire(travail, journal);
                    }

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

    /// <summary>Le dossier où sont rangés les carnets terminés.</summary>
    public static string DossierFinis => Path.Combine(Dossier, "Finis");

    /// <summary>
    /// Range un carnet terminé. C'est la confirmation de l'utilisateur qui l'autorise.
    /// </summary>
    /// <remarks>
    /// <b>Rangé, plus effacé.</b> Cette méthode supprimait le fichier. Un travail mené à son terme
    /// est pourtant la seule trace de ce que l'assistant a su faire et par quel chemin — la
    /// demande d'origine, les étapes, ce qui a été établi en route. C'est exactement la matière
    /// qu'on veut relire quand on se demande pourquoi une manœuvre a marché, ou qu'on cherche à
    /// refaire la même trois semaines plus tard.
    ///
    /// <para>Un sous-dossier plutôt qu'un drapeau dans le fichier : <see cref="Lister"/> et
    /// <see cref="Purger"/> énumèrent <c>Dossier</c> sans descendre, si bien que ce qui est rangé
    /// sort de leur vue sans qu'aucune des deux ait à connaître la notion de « fini ». Le carnet
    /// disparaît de la fenêtre, et reste sur le disque.</para>
    ///
    /// <para>L'horodatage dans le nom sert au deuxième passage : le même titre peut revenir —
    /// « Sauvegarde LibreOffice » reviendra — et le rangement ne doit pas écraser la fois
    /// précédente, qui est justement celle qu'on voudra comparer.</para>
    /// </remarks>
    public static bool Effacer(string titre, Action<string>? journal)
    {
        var fichier = Fichier(titre);

        try
        {
            if (!File.Exists(fichier))
            {
                return false;
            }

            Directory.CreateDirectory(DossierFinis);

            var horodatage = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
            var range = Path.Combine(
                DossierFinis, $"{Path.GetFileNameWithoutExtension(fichier)}-{horodatage}.json");

            File.Move(fichier, range, overwrite: false);
            journal?.Invoke($"carnet terminé, rangé dans Finis : {Path.GetFileName(range)}");

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal?.Invoke($"carnet non rangé : {exception.Message}");

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
            .AppendLine();

        // La demande avant les etapes : c'est la seule ligne qui dit ce qu'on cherche a
        // obtenir, et apres un contexte plein c'est la seule qui reste pour le dire.
        if (travail.Demande.Length > 0)
        {
            texte.Append("  DEMANDE DE L'UTILISATEUR, mot pour mot : ").AppendLine(travail.Demande);
        }

        foreach (var tache in travail.Taches)
        {
            texte.Append(tache.Faite ? "  [x] " : "  [ ] ").AppendLine(tache.Texte);
        }

        // Les acquis apres les etapes, et nommes « ACQUIS » en capitales : c'est la ligne que le
        // modele doit lire avant de poser une question, et il la lit dans un rappel qui compte
        // deja plusieurs dizaines de lignes.
        if (travail.Acquis.Count > 0)
        {
            texte.AppendLine("  ACQUIS — deja etabli, ne le redemande pas :");

            foreach (var fait in travail.Acquis)
            {
                texte.Append("  · ").AppendLine(fait);
            }
        }

        return texte.ToString();
    }

    /// <summary>Retient un fait etabli, pour qu'une reprise n'ait pas a le redemander.</summary>
    /// <remarks>
    /// Sans doublon, et sans limite haute : un fait deja retenu n'est pas reecrit — le modele
    /// repropose volontiers la meme phrase a chaque tour — et rien n'est jamais retire, un acquis
    /// ne cessant pas d'etre vrai parce que le carnet s'allonge.
    /// </remarks>
    /// <returns>Le carnet mis a jour, ou null s'il n'existe pas.</returns>
    public static Travail? Retenir(string titre, IEnumerable<string> faits, Action<string>? journal)
    {
        var travail = Lire(titre, journal);

        if (travail is null)
        {
            return null;
        }

        foreach (var fait in faits.Select(f => (f ?? "").Trim()).Where(f => f.Length > 0))
        {
            if (!travail.Acquis.Exists(deja => deja.Equals(fait, StringComparison.OrdinalIgnoreCase)))
            {
                travail.Acquis.Add(fait);
            }
        }

        travail.Touche = DateTime.UtcNow;
        Ecrire(travail, journal);

        return travail;
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
