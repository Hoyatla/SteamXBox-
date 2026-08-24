using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteamXBox.Tools.Generation;

/// <summary>Un réglage à poser dans un flux avant de le lancer.</summary>
/// <param name="Noeud">Le numéro du nœud, tel qu'il est écrit dans le flux.</param>
/// <param name="Entree">Le nom de l'entrée à changer dans ce nœud.</param>
/// <param name="Valeur">Ce qu'il faut y mettre. Vide laisse la valeur du flux en place.</param>
/// <param name="Obligatoire">Si vrai, une valeur vide arrête tout au lieu de laisser passer.</param>
public readonly record struct ReglageFlux(string Noeud, string Entree, string Valeur, bool Obligatoire);

/// <summary>Ce qu'une cible de manifeste voulait dire.</summary>
/// <param name="Chemin">Le fichier du flux.</param>
/// <param name="Reglages">Les réglages à poser, dans l'ordre du manifeste.</param>
/// <param name="Faute">Une phrase si la cible est illisible, vide sinon.</param>
public readonly record struct LectureFlux(
    string Chemin, IReadOnlyList<ReglageFlux> Reglages, string Faute);

/// <summary>
/// Exécute un flux de travail de génération, réglé par un manifeste.
/// </summary>
/// <remarks>
/// <b>Pourquoi choisir un flux plutôt que le composer.</b> Assembler un graphe de génération —
/// choisir les nœuds, les câbler, prendre l'échantillonneur qui va avec le modèle — est de la
/// génération structurée à long horizon, exactement ce qu'un petit modèle de langage rate en
/// produisant du JSON vraisemblable qui ne s'exécute pas. Lire une déclaration et remplir des
/// champs, il le fait très bien : c'est déjà ce qu'il fait avec tous les autres manifestes. Le
/// flux est donc éprouvé une fois, par une personne, et devient ensuite un outil comme les autres.
///
/// <para>
/// <b>Le format attendu est le format API</b>, celui qu'accepte le point d'entrée <c>/prompt</c> :
/// des nœuds numérotés portant chacun un <c>class_type</c> et ses <c>inputs</c>. C'est ce
/// qu'exporte « Save (API format) », et non le format de l'éditeur, qui porte en plus les
/// positions et les liens de l'interface et que le serveur refuse.
/// </para>
///
/// <para>
/// <b>Le type d'un réglage vient du flux, jamais d'une devinette sur la valeur.</b> Un manifeste
/// n'a que des chaînes à offrir ; le flux, lui, sait déjà que <c>steps</c> est un entier,
/// <c>cfg</c> un décimal et <c>ckpt_name</c> un nom. Deviner d'après la valeur ferait de « 42 » un
/// nombre là où un fichier s'appelle 42, et l'erreur ne se verrait qu'au bout de plusieurs minutes
/// de chargement.
/// </para>
/// </remarks>
public static class FluxTravail
{
    /// <summary>Le port d'origine de ComfyUI ; ses flux et ses extensions l'attendent.</summary>
    private const int Port = 8188;

    /// <summary>
    /// Une génération est longue, et l'attendre est le travail du produit.
    /// </summary>
    /// <remarks>
    /// Une image passe en dizaines de secondes, une vidéo SVD de quatorze images en plusieurs
    /// minutes, et le tout premier lancement ajoute le chargement du modèle sur la carte. Une borne
    /// courte rendrait « échec » à un travail qui se déroule normalement — le défaut le plus coûteux
    /// à diagnostiquer, parce qu'il ressemble à une panne.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    /// <summary>Une seule pour la vie du processus : une par appel épuise les sockets.</summary>
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "SteamXBox" } },
    };

    private static string Adresse
        => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    private static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI");

    /// <summary>Là où le serveur va chercher les fichiers qu'un flux nomme.</summary>
    private static string Entrees => Path.Combine(Racine, "input");

    /// <summary>Là où il dépose ce qu'il a produit.</summary>
    private static string Sorties => Path.Combine(Racine, "output");

    /// <summary>
    /// Découpe la cible d'un manifeste : <c>chemin\du\flux.json|nœud.entrée=valeur|…</c>
    /// </summary>
    /// <remarks>
    /// Un segment illisible arrête la lecture au lieu d'être ignoré. Un réglage qu'on laisse tomber
    /// en silence donne une génération qui aboutit avec les valeurs d'origine : le fichier arrive,
    /// il a l'air correct, et rien ne dit que le choix de l'utilisateur n'a pas été appliqué. Une
    /// faute annoncée coûte une seconde ; celle-là coûte une enquête.
    /// </remarks>
    public static LectureFlux Lire(string cible)
    {
        // Découpé sur les barres NON échappées : ce que l'utilisateur a tapé dans un champ de texte
        // peut en contenir une — « un chat roux | style aquarelle » — et elle ne doit pas ouvrir un
        // réglage que personne n'a écrit. Voir SteamXBox.Plugins.Segments.
        var morceaux = SteamXBox.Plugins.Segments.Decouper(cible);
        var chemin = morceaux.Count > 0 ? morceaux[0].Trim() : "";
        var reglages = new List<ReglageFlux>();

        for (var i = 1; i < morceaux.Count; i++)
        {
            var morceau = morceaux[i].Trim();

            if (morceau.Length == 0)
            {
                continue;
            }

            var obligatoire = morceau.StartsWith('!');

            if (obligatoire)
            {
                morceau = morceau[1..];
            }

            var egal = morceau.IndexOf('=', StringComparison.Ordinal);

            if (egal <= 0)
            {
                return new LectureFlux(chemin, [], $"Réglage illisible, « = » attendu : « {morceau} »");
            }

            var adresse = morceau[..egal].Trim();
            var valeur = morceau[(egal + 1)..].Trim();
            var point = adresse.IndexOf('.', StringComparison.Ordinal);

            if (point <= 0 || point == adresse.Length - 1)
            {
                return new LectureFlux(
                    chemin, [], $"Réglage illisible, « nœud.entrée » attendu : « {adresse} »");
            }

            reglages.Add(new ReglageFlux(
                adresse[..point].Trim(), adresse[(point + 1)..].Trim(), valeur, obligatoire));
        }

        return new LectureFlux(chemin, reglages, "");
    }

    /// <summary>Pose les réglages dans le flux et rend le document à envoyer.</summary>
    /// <param name="json">Le flux au format API.</param>
    /// <param name="reglages">Ce qu'il faut y changer.</param>
    /// <param name="faute">Une phrase si quelque chose ne colle pas, null sinon.</param>
    public static string Appliquer(
        string json, IReadOnlyList<ReglageFlux> reglages, out string? faute)
    {
        faute = null;

        JsonNode? racine;

        try
        {
            racine = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            faute = $"Le flux n'est pas un JSON valide : {exception.Message}";

            return json;
        }

        if (racine is not JsonObject flux)
        {
            faute = "Le flux n'est pas un ensemble de nœuds.";

            return json;
        }

        // Le format de l'éditeur porte « nodes » et « links », et se reconnaît à cela. Le serveur le
        // refuse avec un message qui parle de validation, jamais de format : le dire ici évite une
        // demi-heure passée à chercher une faute dans un flux qui n'a que le tort d'avoir été
        // exporté par le mauvais bouton.
        if (flux.ContainsKey("nodes") && flux.ContainsKey("links"))
        {
            faute = "Ce flux est au format de l'éditeur. Le serveur attend le format API, "
                + "celui qu'exporte « Save (API format) ».";

            return json;
        }

        foreach (var reglage in reglages)
        {
            if (reglage.Valeur.Length == 0)
            {
                if (reglage.Obligatoire)
                {
                    faute = $"Il manque une valeur pour {reglage.Noeud}.{reglage.Entree}.";

                    return json;
                }

                // Un champ laissé vide garde la valeur du flux : c'est ainsi que le flux fournit
                // lui-même ses valeurs par défaut, sans que le manifeste ait à les recopier — et à
                // les laisser diverger.
                continue;
            }

            if (flux[reglage.Noeud] is not JsonObject noeud
                || noeud["inputs"] is not JsonObject entrees)
            {
                faute = $"Le flux n'a pas de nœud « {reglage.Noeud} ».";

                return json;
            }

            if (!entrees.TryGetPropertyValue(reglage.Entree, out var actuelle) || actuelle is null)
            {
                faute = $"Le nœud {reglage.Noeud} n'a pas d'entrée « {reglage.Entree} ».";

                return json;
            }

            if (Convertir(actuelle, reglage, out var pose) is { } souci)
            {
                faute = souci;

                return json;
            }

            entrees[reglage.Entree] = pose;
        }

        return flux.ToJsonString();
    }

    /// <summary>Traduit la chaîne d'un manifeste dans le type que le flux attend.</summary>
    private static string? Convertir(JsonNode actuelle, ReglageFlux reglage, out JsonNode pose)
    {
        var ou = $"{reglage.Noeud}.{reglage.Entree}";

        switch (actuelle.GetValueKind())
        {
            case JsonValueKind.Number:
                if (!double.TryParse(
                        reglage.Valeur,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var nombre))
                {
                    pose = JsonValue.Create(0);

                    return $"{ou} attend un nombre, et « {reglage.Valeur} » n'en est pas un.";
                }

                // Entier ou décimal selon ce que le flux porte déjà, et non selon ce que
                // l'utilisateur a tapé. L'asymétrie est le point : ComfyUI refuse 20.0 là où il
                // attend un nombre de pas, alors qu'il accepte 6 là où il attend un décimal et le
                // convertit lui-même. C'est donc la première direction qu'il faut garder — un champ
                // décimal qui reçoit un compte rond ressortira écrit « 6 », et cela ne gêne
                // personne.
                pose = actuelle.ToJsonString().Contains('.', StringComparison.Ordinal)
                    ? JsonValue.Create(nombre)
                    : JsonValue.Create((long)Math.Round(nombre));

                return null;

            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!bool.TryParse(reglage.Valeur, out var oui))
                {
                    // Un panneau écrit « vrai » ou « oui » aussi bien que « true » : les trois
                    // veulent dire la même chose à qui lit l'écran.
                    oui = reglage.Valeur.Equals("vrai", StringComparison.OrdinalIgnoreCase)
                        || reglage.Valeur.Equals("oui", StringComparison.OrdinalIgnoreCase)
                        || reglage.Valeur == "1";
                }

                pose = JsonValue.Create(oui);

                return null;

            case JsonValueKind.Array:
                // Un tableau, ici, est un câble : ["4", 0] veut dire « la sortie 0 du nœud 4 ». Y
                // poser une valeur débrancherait le graphe, et l'erreur qui suivrait parlerait d'un
                // type manquant à l'autre bout — très loin de la vraie cause.
                pose = JsonValue.Create(0);

                return $"{ou} est un câble entre deux nœuds, pas un réglage.";

            default:
                pose = JsonValue.Create(reglage.Valeur);

                return null;
        }
    }

    /// <summary>
    /// Recopie dans le dossier du serveur les fichiers qu'un réglage désigne.
    /// </summary>
    /// <remarks>
    /// Un nœud <c>LoadImage</c> ne connaît pas les chemins : il nomme un fichier, et le serveur le
    /// cherche dans son propre dossier <c>input</c>. Lui passer le chemin choisi par l'utilisateur
    /// donne une erreur de fichier introuvable qui désigne un fichier qui, lui, existe — le genre de
    /// message qui fait douter de la machine.
    ///
    /// <para>
    /// Le nom déposé est préfixé. Le dossier <c>input</c> appartient au serveur et l'utilisateur y a
    /// peut-être les siens ; écraser son <c>example.png</c> parce qu'il a choisi un fichier du même
    /// nom serait une perte silencieuse.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ReglageFlux> Deposer(
        IReadOnlyList<ReglageFlux> reglages,
        string dossier,
        Action<string>? journal,
        out string? faute)
    {
        faute = null;

        var sortis = new List<ReglageFlux>(reglages.Count);

        foreach (var reglage in reglages)
        {
            if (reglage.Valeur.Length == 0 || !File.Exists(reglage.Valeur))
            {
                sortis.Add(reglage);

                continue;
            }

            var court = Path.GetFileName(reglage.Valeur);
            var nom = "steamxbox-" + court;
            var vers = Path.Combine(dossier, nom);

            // Déjà déposé, et c'est bien le même fichier : on ne recopie pas.
            //
            // Constaté à l'usage : relancer sur la même image pendant qu'une génération tourne
            // faisait échouer la copie — le serveur tient le fichier ouvert, et Windows refuse
            // l'écrasement. Recopier n'apportait rien puisque le fichier était déjà là et
            // identique ; la seule chose que la copie produisait, c'était l'échec.
            if (Identiques(reglage.Valeur, vers))
            {
                sortis.Add(reglage with { Valeur = nom });

                continue;
            }

            try
            {
                Directory.CreateDirectory(dossier);
                File.Copy(reglage.Valeur, vers, overwrite: true);
                journal?.Invoke($"{court} déposé pour le serveur.");

                sortis.Add(reglage with { Valeur = nom });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // On renonce, au lieu de laisser passer le chemin local.
                //
                // La version d'avant gardait la valeur d'origine et soumettait quand même. Le
                // serveur recevait alors un chemin Windows complet, qu'un nœud de chargement ne
                // sait pas résoudre : il répondait « Invalid image file » en nommant un fichier qui
                // existe pourtant, et rien ne reliait ce refus à une copie manquée trois secondes
                // plus tôt. Mieux vaut ne rien lancer et dire pourquoi.
                journal?.Invoke($"dépôt de {court} impossible : {exception.Message}");

                faute = $"« {court} » n'a pas pu être déposé pour le générateur : "
                    + $"{exception.Message} Le fichier est peut-être en cours d'utilisation ; "
                    + "réessayez quand la génération en cours sera finie.";

                return sortis;
            }
        }

        return sortis;
    }

    /// <summary>Le fichier déposé est-il déjà celui-ci ?</summary>
    /// <remarks>
    /// La taille et la date de dernière écriture, les deux : <see cref="File.Copy(string,string,bool)"/>
    /// conserve la date de la source, si bien que le dépôt porte celle du fichier d'origine. Deux
    /// fichiers de même nom, même taille et même date à la milliseconde près ne sont pas
    /// distinguables autrement qu'en les relisant en entier, ce qui coûterait plus cher que la
    /// copie qu'on cherche à éviter.
    /// </remarks>
    private static bool Identiques(string source, string depose)
    {
        try
        {
            if (!File.Exists(depose))
            {
                return false;
            }

            var un = new FileInfo(source);
            var deux = new FileInfo(depose);

            return un.Length == deux.Length && un.LastWriteTimeUtc == deux.LastWriteTimeUtc;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Lit la cible, prépare le flux, le soumet, et attend le résultat.</summary>
    /// <returns>Une phrase à montrer : où est le fichier, ou pourquoi il n'y en a pas.</returns>
    public static string Lancer(string cible, Action<string>? journal)
    {
        var lecture = Lire(cible);

        if (lecture.Faute.Length > 0)
        {
            return lecture.Faute;
        }

        if (lecture.Chemin.Length == 0)
        {
            return "Aucun flux désigné.";
        }

        if (!File.Exists(lecture.Chemin))
        {
            return $"Le flux est absent : {lecture.Chemin}";
        }

        // Les réglages d'abord, le serveur ensuite : une faute dans la cible se voit tout de suite,
        // au lieu d'attendre les deux minutes de démarrage pour être annoncée.
        string document;

        try
        {
            document = File.ReadAllText(lecture.Chemin);
        }
        catch (IOException exception)
        {
            return $"Flux illisible : {exception.Message}";
        }

        var reglages = Deposer(lecture.Reglages, Entrees, journal, out var depot);

        if (depot is not null)
        {
            return depot;
        }

        var envoi = Appliquer(document, reglages, out var faute);

        if (faute is not null)
        {
            return faute;
        }

        // Soumettre à un serveur éteint donne « connexion refusée », ce qui ne dit pas à
        // l'utilisateur qu'il suffisait de le démarrer et d'attendre.
        if (ComfyServer.Preparer(journal) is { } absent)
        {
            return absent;
        }

        journal?.Invoke("Flux soumis au générateur.");

        var identifiant = Soumettre(envoi, out var refus);

        return identifiant is null ? refus : Attendre(identifiant, journal);
    }

    /// <summary>
    /// Juge un graphe composé, et le lance s'il tient.
    /// </summary>
    /// <param name="graphe">Le flux au format API, tel que le modèle l'a écrit.</param>
    /// <param name="soumettre">Faux pour se contenter du verdict, sans occuper la carte.</param>
    /// <param name="journal">Reçoit l'avancement.</param>
    /// <remarks>
    /// <b>L'ordre des trois gestes n'est pas indifférent.</b> Les fichiers désignés sont déposés
    /// d'abord, le catalogue relu ensuite, la vérification en dernier. C'est que l'entrée
    /// <c>image</c> d'un <c>LoadImage</c> n'est pas un texte libre : c'est un choix, dont les
    /// options sont les fichiers réellement présents dans le dossier du serveur. Vérifier avant de
    /// déposer ferait donc refuser une image que l'on s'apprête à mettre en place — et le reproche
    /// nommerait un fichier qui existe, ce qui est le genre de message qui fait douter de la
    /// machine.
    ///
    /// <para>
    /// C'est aussi ce qui referme la boucle de l'enchaînement : le chemin qu'un outil vient de
    /// produire peut être écrit tel quel dans un graphe, sans que le modèle ait à savoir où le
    /// serveur range ses entrées.
    /// </para>
    /// </remarks>
    public static string Composer(string graphe, bool soumettre, Action<string>? journal)
    {
        if (ComfyServer.Preparer(journal) is { } absent)
        {
            return absent;
        }

        JsonNode? racine;

        try
        {
            racine = JsonNode.Parse(graphe);
        }
        catch (JsonException exception)
        {
            return $"Le flux n'est pas un JSON valide : {exception.Message}";
        }

        var depose = false;

        if (racine is JsonObject objet)
        {
            depose = DeposerDansGraphe(objet, journal, out var empeche);

            if (empeche is not null)
            {
                return empeche;
            }
        }

        if (Catalogue.Demander(Port, journal, frais: depose) is not { } catalogue)
        {
            return "Le générateur n'a pas rendu son catalogue : impossible de juger ce flux.";
        }

        var envoi = racine?.ToJsonString() ?? graphe;
        var fautes = FluxVerificateur.Verifier(envoi, catalogue);
        var verdict = FluxVerificateur.Verdict(fautes);

        if (fautes.Count > 0 || !soumettre)
        {
            return verdict;
        }

        journal?.Invoke("Flux vérifié, soumis au générateur.");

        var identifiant = Soumettre(envoi, out var refus);

        return identifiant is null ? refus : Attendre(identifiant, journal);
    }

    /// <summary>
    /// Recopie dans le dossier du serveur les fichiers qu'un graphe désigne par leur chemin.
    /// </summary>
    /// <returns>Vrai si quelque chose a été déposé, donc si le catalogue a changé.</returns>
    private static bool DeposerDansGraphe(
        JsonObject graphe, Action<string>? journal, out string? faute)
    {
        faute = null;

        var depose = false;

        foreach (var (_, corps) in graphe)
        {
            if (corps is not JsonObject noeud || noeud["inputs"] is not JsonObject entrees)
            {
                continue;
            }

            foreach (var nom in entrees.Select(e => e.Key).ToList())
            {
                if (entrees[nom] is not JsonValue valeur
                    || valeur.GetValueKind() != JsonValueKind.String)
                {
                    continue;
                }

                var ecrit = valeur.ToString();

                if (ecrit.Length == 0 || !File.Exists(ecrit))
                {
                    continue;
                }

                var reglages = Deposer(
                    [new ReglageFlux("", nom, ecrit, false)], Entrees, journal, out var refus);

                if (refus is not null)
                {
                    faute = refus;

                    return depose;
                }

                if (reglages[0].Valeur != ecrit)
                {
                    entrees[nom] = JsonValue.Create(reglages[0].Valeur);
                    depose = true;
                }
            }
        }

        return depose;
    }

    /// <summary>Envoie le flux et rend l'identifiant que le serveur lui donne.</summary>
    /// <remarks>
    /// Le serveur valide le graphe avant de commencer, et répond alors <c>node_errors</c> : c'est la
    /// panne utile, celle qui arrive en une seconde au lieu des minutes de chargement qu'aurait
    /// coûtées la même faute découverte en cours de route. Elle est donc rendue telle quelle, avec
    /// le nœud fautif.
    /// </remarks>
    private static string? Soumettre(string flux, out string refus)
    {
        refus = "";

        try
        {
            var corps = new JsonObject
            {
                ["prompt"] = JsonNode.Parse(flux),
                ["client_id"] = "steamxbox",
            }.ToJsonString();

            using var contenu = new StringContent(corps, Encoding.UTF8, "application/json");
            using var reponse = Client.PostAsync(Adresse + "/prompt", contenu)
                .GetAwaiter().GetResult();

            var lu = reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!reponse.IsSuccessStatusCode)
            {
                refus = "Le générateur a refusé le flux : " + Refus(lu, (int)reponse.StatusCode);

                return null;
            }

            using var document = JsonDocument.Parse(lu);

            if (!document.RootElement.TryGetProperty("prompt_id", out var identifiant)
                || identifiant.ValueKind != JsonValueKind.String)
            {
                refus = "Le générateur n'a pas rendu d'identifiant pour ce flux.";

                return null;
            }

            return identifiant.GetString();
        }
        catch (HttpRequestException exception)
        {
            refus = $"Générateur injoignable : {exception.Message}";

            return null;
        }
        catch (TaskCanceledException)
        {
            refus = "Le générateur n'a pas répondu à la soumission.";

            return null;
        }
        catch (JsonException)
        {
            refus = "Le générateur a répondu quelque chose d'illisible.";

            return null;
        }
    }

    /// <summary>La raison lisible dans un refus, ou le code à défaut.</summary>
    /// <summary>
    /// Pourquoi le générateur a refusé, dans ses propres mots.
    /// </summary>
    /// <remarks>
    /// <b>Le détail était lu, puis jeté.</b> Le bloc <c>error</c> était pris en premier, et comme il
    /// porte toujours quelque chose — « Prompt outputs failed validation », qui ne dit rien de plus
    /// que « non » — la lecture s'arrêtait là. Le bloc <c>node_errors</c>, où ComfyUI nomme l'entrée
    /// fautive et les valeurs qu'il accepterait, n'était jamais atteint.
    ///
    /// <para>
    /// Le 23 août, l'assistant a donc reçu « Le générateur a refusé le flux : Prompt outputs failed
    /// validation » et l'a répété à l'utilisateur, faute d'avoir la moindre idée de ce qu'il fallait
    /// corriger. Le générateur, lui, savait : il aurait dit quel nœud, quelle entrée, et quelles
    /// valeurs sont admises.
    /// </para>
    ///
    /// <para>
    /// Les deux blocs sont donc réunis, le générique d'abord, le précis ensuite. C'est la même règle
    /// qu'ailleurs dans ce produit : un échec qui ne dit pas quoi changer ne change rien.
    /// </para>
    /// </remarks>
    public static string Refus(string corps, int code)
    {
        var dits = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(corps);

            if (document.RootElement.TryGetProperty("error", out var erreur)
                && erreur.ValueKind == JsonValueKind.Object)
            {
                dits.AddRange(
                    new[] { Texte(erreur, "message"), Texte(erreur, "details") }
                        .Where(t => t.Length > 0));
            }

            if (document.RootElement.TryGetProperty("node_errors", out var noeuds)
                && noeuds.ValueKind == JsonValueKind.Object)
            {
                foreach (var noeud in noeuds.EnumerateObject().Take(3))
                {
                    dits.Add(Noeud(noeud));
                }
            }
        }
        catch (JsonException)
        {
            // Une page HTML plutôt qu'un document : le code seul reste vrai.
        }

        dits.RemoveAll(d => d.Length == 0);

        return dits.Count > 0
            ? string.Join(" — ", dits)
            : $"réponse {code.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Ce qu'un nœud fautif reproche, entrée par entrée.</summary>
    private static string Noeud(JsonProperty noeud)
    {
        if (noeud.Value.ValueKind != JsonValueKind.Object
            || !noeud.Value.TryGetProperty("errors", out var fautes)
            || fautes.ValueKind != JsonValueKind.Array)
        {
            return $"nœud {noeud.Name}";
        }

        var precisions = new List<string>();

        foreach (var faute in fautes.EnumerateArray().Take(3))
        {
            if (faute.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dit = string.Join(
                " : ",
                new[] { Texte(faute, "message"), Texte(faute, "details") }
                    .Where(t => t.Length > 0));

            if (dit.Length > 0)
            {
                precisions.Add(dit);
            }
        }

        return precisions.Count == 0
            ? $"nœud {noeud.Name}"
            : $"nœud {noeud.Name} : {string.Join(" ; ", precisions)}";
    }

    /// <summary>Attend la fin du travail et dit où est le résultat.</summary>
    /// <remarks>
    /// L'avancement est annoncé toutes les trente secondes. Sans cela une génération de plusieurs
    /// minutes est indiscernable d'un blocage, et l'utilisateur relance — ce qui met deux travaux
    /// sur une carte qui n'en tient qu'un.
    /// </remarks>
    private static string Attendre(string identifiant, Action<string>? journal)
    {
        var montre = System.Diagnostics.Stopwatch.StartNew();
        var dit = 0;

        while (montre.Elapsed < Patience)
        {
            Thread.Sleep(2000);

            var fini = Histoire(identifiant, out var fichiers, out var echec);

            if (echec.Length > 0)
            {
                return $"Le générateur s'est arrêté : {echec}";
            }

            if (fini)
            {
                var duree = montre.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);

                if (fichiers.Count == 0)
                {
                    return $"Flux terminé en {duree} s, mais aucun fichier n'a été produit. "
                        + "Le flux n'a peut-être pas de nœud d'enregistrement.";
                }

                return fichiers.Count == 1
                    ? $"Terminé en {duree} s : {Path.Combine(Sorties, fichiers[0])}"
                    : $"Terminé en {duree} s : {fichiers.Count} fichiers dans {Sorties}";
            }

            var tranches = (int)(montre.Elapsed.TotalSeconds / 30);

            if (tranches > dit)
            {
                dit = tranches;

                var passe = montre.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
                journal?.Invoke($"Génération en cours, {passe} s écoulées...");
            }
        }

        var borne = Patience.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture);

        return $"Le flux n'a pas abouti en {borne} minutes. Le générateur travaille peut-être "
            + $"encore : son interface est sur {Adresse}.";
    }

    /// <summary>Ce travail est-il fini, et qu'a-t-il produit ?</summary>
    /// <remarks>
    /// Les fichiers sont cherchés par la forme plutôt que par le nom du champ. Selon le nœud
    /// d'enregistrement, ComfyUI les range sous <c>images</c>, <c>gifs</c>, <c>videos</c> ou
    /// <c>audio</c>, et la liste s'allonge à chaque version. Chercher « un tableau d'objets qui
    /// portent un <c>filename</c> » survit à ces changements ; une liste de noms de champs ne
    /// survivrait pas à la prochaine mise à jour.
    /// </remarks>
    private static bool Histoire(
        string identifiant, out IReadOnlyList<string> fichiers, out string echec)
    {
        var trouves = new List<string>();
        fichiers = trouves;
        echec = "";

        try
        {
            using var reponse = Client.GetAsync(Adresse + "/history/" + identifiant)
                .GetAwaiter().GetResult();

            if (!reponse.IsSuccessStatusCode)
            {
                return false;
            }

            var lu = reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var document = JsonDocument.Parse(lu);

            // Pas encore dans l'histoire : le travail est en file, ou en cours.
            if (!document.RootElement.TryGetProperty(identifiant, out var travail))
            {
                return false;
            }

            if (travail.TryGetProperty("status", out var etat)
                && etat.ValueKind == JsonValueKind.Object
                && string.Equals(Texte(etat, "status_str"), "error", StringComparison.Ordinal))
            {
                echec = Panne(etat);

                return true;
            }

            if (travail.TryGetProperty("outputs", out var sorties)
                && sorties.ValueKind == JsonValueKind.Object)
            {
                Recolter(sorties, trouves);
            }

            return true;
        }
        catch (HttpRequestException)
        {
            // Le serveur a eu un hoquet. La ronde suivante le dira aussi : inutile d'abandonner
            // une génération de plusieurs minutes sur une connexion manquée.
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Ramasse tout ce qui ressemble à un fichier produit.</summary>
    /// <summary>Les fichiers réellement écrits par un flux, d'après le bloc « outputs ».</summary>
    /// <param name="sorties">Le bloc <c>outputs</c> d'une entrée d'histoire.</param>
    /// <returns>Les chemins relatifs au dossier de sortie du générateur.</returns>
    /// <remarks>
    /// Publique pour l'épreuve, et pour une raison précise : ce qu'elle sépare — un fichier écrit
    /// d'un aperçu affiché — ne se voit dans aucun résultat visible. Un flux qui rend sept aperçus
    /// au lieu d'une vidéo produit un message faux, pas une erreur.
    /// </remarks>
    public static IReadOnlyList<string> Recolter(JsonElement sorties)
    {
        var trouves = new List<string>();

        Recolter(sorties, trouves);

        return trouves;
    }

    private static void Recolter(JsonElement sorties, List<string> trouves)
    {
        foreach (var noeud in sorties.EnumerateObject())
        {
            if (noeud.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var champ in noeud.Value.EnumerateObject())
            {
                if (champ.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in champ.Value.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var nom = Texte(element, "filename");

                    if (nom.Length == 0)
                    {
                        continue;
                    }

                    // Seuls les fichiers de sortie comptent, pas les aperçus.
                    //
                    // ComfyUI note dans l'histoire tout ce qu'un nœud a montré, et marque chaque
                    // entrée de son « type » : « output » pour ce qu'il a écrit, « input » ou
                    // « temp » pour ce qu'il a seulement affiché. Nous ramassions les trois.
                    //
                    // Le 23 août, un montage de sept photos a donc annoncé « Terminé en 2 s :
                    // 7 fichiers dans output » : les sept aperçus du chargeur, alors que le flux
                    // n'a qu'un seul nœud d'enregistrement et n'avait produit qu'une vidéo. Le
                    // compte était faux, et pire, la vidéo n'était plus nommée — le message ne
                    // nomme le fichier que lorsqu'il n'y en a qu'un. L'assistant, privé du chemin,
                    // a inventé « montage_1.mp4 », qui n'existait nulle part.
                    var genre = Texte(element, "type");

                    if (genre.Length > 0
                        && !genre.Equals("output", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sous = Texte(element, "subfolder");

                    trouves.Add(sous.Length > 0 ? Path.Combine(sous, nom) : nom);
                }
            }
        }
    }

    /// <summary>La première explication lisible dans un état en erreur.</summary>
    private static string Panne(JsonElement etat)
    {
        if (!etat.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            return "raison non précisée";
        }

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in message.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var dit = Texte(part, "exception_message");

                if (dit.Length > 0)
                {
                    return dit;
                }
            }
        }

        return "raison non précisée";
    }

    /// <summary>Un champ texte, ou vide s'il n'y en a pas.</summary>
    private static string Texte(JsonElement element, string nom)
        => element.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? ""
            : "";
}
