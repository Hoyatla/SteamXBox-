using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de pilotage de l'Atelier, branchees sur le subprocess HTTP
/// demarre par <c>SenSÉ.Desktop</c> au boot (cf. <c>ServeurAtelier.Demarrer</c>).
/// </summary>
/// <remarks>
/// <b>Le pont : SenSÉ.Atelier.exe + HTTP loopback.</b> L'Atelier est un
/// subprocess WPF en mode headless, expose sur <c>http://127.0.0.1:8770</c>.
/// Cette classe lui parle via HttpClient. L'URL est fixee par
/// <see cref="Demarrer"/>, et l'env var <c>SENSE_ATELIER_URL</c> est posee
/// par le lanceur pour les clients qui preferent lire la config.
///
/// <para><b>Pas d'auth.</b> L'Atelier n'authentifie pas les requetes venant
/// de 127.0.0.1. Loopback only, comme mcp-saisie et mcp-cdp.</para>
///
/// <para><b>Mappage verbe -> Capacite.</b> L'Assistant manipule des verbes
/// en francais (<c>atelier_creer_graphe</c>, <c>atelier_executer_graphe</c>,
/// etc.). Cette classe les traduit en appels HTTP vers l'Atelier, qui repond
/// en JSON selon la convention {ok, data} / {ok:false, error}. Le mapping
/// suit l'API existante de l'Atelier (cf. <c>SenSÉ.Atelier.Mcp.Verbes</c>).</para>
/// </remarks>
public static class AssistantAtelier
{
    private static HttpClient? _http;
    private static string _urlBase = "";

    /// <summary>
    /// Initialise le client HTTP. Appele par <c>ServeurAtelier.Demarrer</c>
    /// apres que l'Atelier a ete lance. Idempotent.
    /// </summary>
    public static void Demarrer(string urlBase, string? token = null)
    {
        if (_http is not null && _urlBase == urlBase) return;
        _urlBase = urlBase.TrimEnd('/');
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }
    }

    /// <summary>L'URL de base du serveur HTTP de l'Atelier. Vide tant que <see cref="Demarrer"/> n'a pas ete appele.</summary>
    public static string UrlBase => _urlBase;

    /// <summary>
    /// Construit la liste des Capacite de l'Atelier que l'Assistant peut appeler.
    /// A ajouter a la liste passee a <see cref="AssistantLocal.Repondre"/>.
    /// </summary>
    /// <remarks>
    /// Le verbe HTTP reel est un segment apres <c>/atelier/</c> dans l'URL.
    /// Les Capacite ici sont les operations de haut niveau que l'utilisateur
    /// peut demander : lister les graphes, en creer un, y ajouter des noeuds,
    /// executer le tout, lire le resultat. Pour les verbes bas-niveau
    /// (lien/creer, noeud/ajouter, etc.), voir <c>SenSÉ.Atelier.Mcp.Verbes</c>.
    /// </remarks>
    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return new AssistantLocal.Capacite[]
        {
            new(
                "atelier_lister_graphes",
                "Liste les graphes de l'Atelier avec leur id, nom, espace, nombre de noeuds, date de modification. Utilise pour decouvrir ce qui existe deja avant d'en creer un nouveau ou d'en modifier un.",
                Array.Empty<AssistantLocal.Parametre>(),
                args => AppelerAtelier("graphe/lister", null, query: new Dictionary<string, string>())),

            new(
                "atelier_creer_graphe",
                "Cree un nouveau graphe dans l'Atelier. Retourne son id. Le graphe est vide (aucun noeud, aucun lien) : il faut ensuite y ajouter des noeuds avec atelier_noeud_ajouter et les relier avec atelier_lien_creer. Espace: 'codage' (defaut) ou 'multimedia'.",
                new[]
                {
                    new AssistantLocal.Parametre("espace", "Espace de travail : 'codage' ou 'multimedia'. Defaut 'codage'.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("nom", "Nom affiche dans l'onglet du graphe.", Array.Empty<string>()),
                },
                args =>
                {
                    var body = new Dictionary<string, object?>
                    {
                        ["espace"] = args.GetValueOrDefault("espace") ?? "codage",
                        ["nom"] = args.GetValueOrDefault("nom") ?? "Sans nom",
                    };
                    return AppelerAtelier("graphe/nouveau", body);
                }),

            new(
                "atelier_ouvrir_graphe",
                "Charge un graphe par son id. Retourne ses noeuds, ses liens, ses ports. Utilise apres atelier_lister_graphes pour voir la structure complete, ou pour verifier qu'un graphe a ete cree comme attendu.",
                new[]
                {
                    new AssistantLocal.Parametre("graphe_id", "L'id du graphe (GUID renvoye par atelier_creer_graphe ou atelier_lister_graphes).", Array.Empty<string>()),
                },
                args =>
                {
                    var body = new Dictionary<string, object?>
                    {
                        ["graphe_id"] = args.GetValueOrDefault("graphe_id") ?? "",
                    };
                    return AppelerAtelier("graphe/charger", body);
                }),

            new(
                "atelier_sauvegarder_graphe",
                "Confirme la sauvegarde du graphe. Cote Atelier, chaque modification (ajout de noeud, de lien, edition de parametre) est ecrite sur disque immediatement : ce verbe sert surtout d'acquittement et a verifier qu'un graphe existe. Retourne un statut simple.",
                new[]
                {
                    new AssistantLocal.Parametre("graphe_id", "L'id du graphe a verifier (sauvegarde implicite).", Array.Empty<string>()),
                },
                args =>
                {
                    var id = args.GetValueOrDefault("graphe_id") ?? "";
                    if (string.IsNullOrEmpty(id)) return "atelier_sauvegarder_graphe: graphe_id manquant";
                    var r = AppelerAtelier("graphe/charger", new Dictionary<string, object?> { ["graphe_id"] = id });
                    return "atelier_sauvegarder_graphe: graphe " + id + " sauvegarde (auto-save a chaque modification). " + r;
                }),

            new(
                "atelier_noeud_ajouter",
                "Ajoute un noeud a un graphe existant. Le type determine ce que fait le noeud (utilise atelier_catalogue_types pour voir la liste). x/y sont les coordonnees sur le canvas (en pixels, origine en haut a gauche). params est une string JSON dont les cles dependent du type (ex: langage, code, prompt, chemin).",
                new[]
                {
                    new AssistantLocal.Parametre("graphe_id", "L'id du graphe cible.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("type", "L'id du type de noeud (ex: 'texte', 'executer_code', 'llm_generer_code', 'texte_vers_image'). Voir atelier_catalogue_types.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("x", "Position X sur le canvas (pixels). Defaut 100.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("y", "Position Y sur le canvas (pixels). Defaut 100.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("params", "Parametres du noeud en JSON (string serialisee). Optionnel. Voir atelier_catalogue_types pour la liste par type.", Array.Empty<string>()),
                },
                args =>
                {
                    double x = 100, y = 100;
                    double.TryParse(args.GetValueOrDefault("x"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out x);
                    double.TryParse(args.GetValueOrDefault("y"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out y);
                    var body = new Dictionary<string, object?>
                    {
                        ["graphe_id"] = args.GetValueOrDefault("graphe_id") ?? "",
                        ["type"] = args.GetValueOrDefault("type") ?? "",
                        ["x"] = x,
                        ["y"] = y,
                    };
                    var pStr = args.GetValueOrDefault("params");
                    if (!string.IsNullOrEmpty(pStr))
                    {
                        try
                        {
                            var parsed = JsonNode.Parse(pStr);
                            if (parsed is JsonObject jo) body["params"] = jo;
                        }
                        catch (Exception ex)
                        {
                            return "atelier_noeud_ajouter: params invalides: " + ex.Message;
                        }
                    }
                    return AppelerAtelier("noeud/ajouter", body);
                }),

            new(
                "atelier_lien_creer",
                "Cree un lien entre la sortie d'un noeud et l'entree d'un autre. noeud_source_id/port_source et noeud_cible_id/port_cible identifient les extremites. Le moteur refuse les liens entre types incompatibles (cf. atelier_catalogue_types pour la liste des types de ports).",
                new[]
                {
                    new AssistantLocal.Parametre("graphe_id", "L'id du graphe cible.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("noeud_source_id", "L'id du noeud source.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("port_source", "Le nom du port de sortie sur le noeud source (ex: 'valeur', 'code', 'image').", Array.Empty<string>()),
                    new AssistantLocal.Parametre("noeud_cible_id", "L'id du noeud cible.", Array.Empty<string>()),
                    new AssistantLocal.Parametre("port_cible", "Le nom du port d'entree sur le noeud cible (ex: 'description', 'code', 'image').", Array.Empty<string>()),
                },
                args => AppelerAtelier("lien/creer", new Dictionary<string, object?>
                {
                    ["graphe_id"] = args.GetValueOrDefault("graphe_id") ?? "",
                    ["port_source"] = new Dictionary<string, object?>
                    {
                        ["noeud_id"] = args.GetValueOrDefault("noeud_source_id") ?? "",
                        ["port_nom"] = args.GetValueOrDefault("port_source") ?? "",
                    },
                    ["port_cible"] = new Dictionary<string, object?>
                    {
                        ["noeud_id"] = args.GetValueOrDefault("noeud_cible_id") ?? "",
                        ["port_nom"] = args.GetValueOrDefault("port_cible") ?? "",
                    },
                })),

            new(
                "atelier_executer_graphe",
                "Execute un graphe de maniere asynchrone. Retourne immediatement un execution_id. Utilise ensuite atelier_etat_execution pour suivre la progression et lire les sorties. L'execution reelle peut prendre de quelques secondes (LLM) a plusieurs minutes (generation video).",
                new[]
                {
                    new AssistantLocal.Parametre("graphe_id", "L'id du graphe a executer.", Array.Empty<string>()),
                },
                args => AppelerAtelier("executer", new Dictionary<string, object?>
                {
                    ["graphe_id"] = args.GetValueOrDefault("graphe_id") ?? "",
                })),

            new(
                "atelier_etat_execution",
                "Retourne l'etat d'une execution : statut global (EnCours/Reussi/Echec/Annule), etat de chaque noeud, sorties finales, duree, erreur eventuelle. A appeler en boucle tant que statut == 'EnCours', ou une seule fois pour lire le resultat final.",
                new[]
                {
                    new AssistantLocal.Parametre("execution_id", "L'id d'execution renvoye par atelier_executer_graphe.", Array.Empty<string>()),
                },
                args => AppelerAtelier("execution/etat", null, query: new Dictionary<string, string>
                {
                    ["execution_id"] = args.GetValueOrDefault("execution_id") ?? "",
                })),

            new(
                "atelier_annuler_execution",
                "Annule une execution en cours. Le moteur arrete le noeud courant et marque l'execution comme Annule. Utilise si l'utilisateur change d'avis ou si l'execution est bloquee.",
                new[]
                {
                    new AssistantLocal.Parametre("execution_id", "L'id d'execution a annuler.", Array.Empty<string>()),
                },
                args => AppelerAtelier("execution/annuler", new Dictionary<string, object?>
                {
                    ["execution_id"] = args.GetValueOrDefault("execution_id") ?? "",
                })),

            new(
                "atelier_catalogue_espaces",
                "Liste les espaces disponibles (codage, multimedia) avec le nombre de types de noeuds dans chaque. Premiere chose a appeler pour decouvrir ce que l'Atelier sait faire.",
                Array.Empty<AssistantLocal.Parametre>(),
                args => AppelerAtelier("catalogue/espaces", null)),

            // Le catalogue entier, tel que l'Atelier le redige lui-meme.
            //
            // C'est la seule connaissance de l'Atelier qui ne vieillit pas : elle est generee a
            // partir du catalogue reel au moment ou on la demande. Recopier cette description dans
            // la consigne aurait coute des jetons a chaque tour ET menti des le premier noeud
            // ajoute — ce qui est arrive a d'autres descriptions figees de ce produit.
            //
            // A n'appeler que pour decouvrir : c'est un texte long, et atelier_catalogue_types
            // suffit quand on sait deja quel espace on vise.
            new(
                "atelier_aide",
                "Rend le catalogue complet de l'Atelier, redige par l'Atelier lui-meme : tous les "
                + "espaces, tous les types de noeuds, leurs ports et leurs parametres. A appeler "
                + "quand tu ne sais pas ce que l'Atelier sait faire, ou pour composer un graphe que "
                + "tu n'as jamais fait. C'est long : une fois suffit, ensuite sers-toi de "
                + "atelier_catalogue_types qui est plus court.",
                Array.Empty<AssistantLocal.Parametre>(),
                args => AppelerAtelier("catalogue/aide", null)),

            new(
                "atelier_type",
                "Decrit UN type de noeud : ce qu'il fait, ses ports d'entree et de sortie, ses "
                + "parametres et leurs valeurs possibles. A preferer a atelier_catalogue_types "
                + "quand tu sais deja quel noeud tu veux et qu'il te manque seulement comment le "
                + "parametrer.",
                new[]
                {
                    new AssistantLocal.Parametre(
                        "type_id",
                        "L'id du type (ex: 'executer_code', 'llm_generer_code', 'texte_vers_image').",
                        Array.Empty<string>()),
                },
                args => AppelerAtelier(
                    "catalogue/type",
                    null,
                    new Dictionary<string, string>
                    {
                        ["type_id"] = args.GetValueOrDefault("type_id") ?? "",
                    })),

            new(
                "atelier_catalogue_types",
                "Liste les types de noeuds disponibles dans un espace, avec leurs ports d'entree/sortie et leurs parametres. Utilise pour decouvrir comment parametrer un noeud avant de l'ajouter au graphe. Chaque type a un id, un nom affiche, une description, une categorie, et la liste de ses params (nom, libelle, type, defaut, valeurs possibles).",
                new[]
                {
                    new AssistantLocal.Parametre("espace", "Espace : 'codage' (defaut) ou 'multimedia'.", Array.Empty<string>()),
                },
                args => AppelerAtelier("catalogue/types", null, query: new Dictionary<string, string>
                {
                    ["espace"] = args.GetValueOrDefault("espace") ?? "codage",
                })),

            // CE QUI ALLAIT AU GENERATEUR MULTIMEDIA VIENT ICI.
            //
            // Le generateur offrait cinq verbes a l'assistant — flux_modeles, flux_prets,
            // flux_catalogue, flux_noeud, flux_lancer — et il n'existe plus. Les trois derniers
            // avaient deja leur equivalent (atelier_catalogue_types, atelier_type,
            // atelier_executer_graphe) ; les deux premiers non, alors que les routes qui les
            // servent existaient deja cote Atelier. Elles etaient ecrites et injoignables.
            //
            // Ce n'est donc pas un ajout de fonction : c'est le raccordement de ce qui etait deja
            // la. Un verbe qui n'est pas declare n'existe pas pour le modele.

            new(
                "atelier_modeles",
                "Liste les modeles installes que l'Atelier peut employer : images, video, texte, "
                + "audio. A appeler AVANT de composer un graphe multimedia — un graphe bati sur un "
                + "modele absent echoue a l'execution, plusieurs minutes plus tard.",
                Array.Empty<AssistantLocal.Parametre>(),
                _ => AppelerAtelier("modeles", null)),

            new(
                "atelier_gabarits",
                "Liste les graphes tout prets, publies comme gabarits. A preferer a la composition "
                + "quand la demande ressemble a quelque chose de deja fait : reprendre un gabarit "
                + "coute un appel, le recomposer en coute dix et se trompe davantage.",
                Array.Empty<AssistantLocal.Parametre>(),
                _ => AppelerAtelier("templates/lister", null)),

            new(
                "atelier_gabarit_charger",
                "Charge un gabarit dans un nouveau graphe, pret a etre ajuste puis execute.",
                new[]
                {
                    new AssistantLocal.Parametre(
                        "template_id", "L'id du gabarit, rendu par atelier_gabarits.", Array.Empty<string>()),
                },
                args => AppelerAtelier(
                    "templates/charger",
                    new Dictionary<string, object?>
                    {
                        ["template_id"] = args.GetValueOrDefault("template_id") ?? "",
                    })),

            new(
                "atelier_essayer_noeud",
                "Execute UN SEUL noeud, sans le reste du graphe. C'est la façon d'eprouver un "
                + "reglage avant de lancer une generation qui prendra des minutes : le generateur "
                + "avait « flux_verifier » pour cela, et c'est mieux qu'une verification puisque "
                + "le noeud tourne vraiment.",
                new[]
                {
                    new AssistantLocal.Parametre(
                        "graphe_id", "Le graphe qui contient le noeud.", Array.Empty<string>()),
                    new AssistantLocal.Parametre(
                        "noeud_id", "Le noeud a executer seul.", Array.Empty<string>()),
                },
                args => AppelerAtelier(
                    "executer_noeud",
                    new Dictionary<string, object?>
                    {
                        ["graphe_id"] = args.GetValueOrDefault("graphe_id") ?? "",
                        ["noeud_id"] = args.GetValueOrDefault("noeud_id") ?? "",
                    })),
        };
    }

    /// <summary>
    /// Appelle l'Atelier par verbe HTTP. Le verbe est place apres <c>/atelier/</c>
    /// dans l'URL. Renvoie la string retournee par l'outil, ou un message d'erreur
    /// formate si le serveur n'est pas joignable.
    /// </summary>
    private static string AppelerAtelier(string verbe, object? body, IReadOnlyDictionary<string, string>? query = null)
    {
        if (_http is null || string.IsNullOrEmpty(_urlBase))
        {
            return "atelier: client non initialise (AssistantAtelier.Demarrer pas appele)";
        }

        // Retry sur HttpRequestException : le serveur coupe la connexion apres
        // chaque requete, donc la 1ere requete apres un idle long peut echouer.
        for (int essai = 0; essai < 3; essai++)
        {
            try
            {
                var qs = "";
                if (query is not null && query.Count > 0)
                {
                    qs = "?" + string.Join("&", query.Select(kv =>
                        Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
                }
                var url = _urlBase + "/atelier/" + verbe + qs;

                // StringContent, et surtout PAS PostAsJsonAsync. Voir AssistantSaisie.cs
                // pour la justification detaillee (Content-Length vs Transfer-Encoding).
                var charge = body is null ? "{}" : JsonSerializer.Serialize(body);
                var contenu = new StringContent(charge, Encoding.UTF8, "application/json");
                using var resp = _http.PostAsync(url, contenu).GetAwaiter().GetResult();
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    return "atelier: HTTP " + (int)resp.StatusCode + ": " + json;
                }
                try
                {
                    var node = JsonNode.Parse(json);
                    if (node is JsonObject obj)
                    {
                        if (obj["ok"]?.GetValue<bool>() == false)
                        {
                            return "atelier: " + (obj["error"]?.GetValue<string>() ?? json);
                        }
                        var data = obj["data"];
                        if (data is not null) return data.ToJsonString();
                    }
                }
                catch
                {
                    // pas du JSON, on retourne le body brut
                }
                return json;
            }
            catch (HttpRequestException) when (essai < 2)
            {
                System.Threading.Thread.Sleep(150);
            }
            catch (TaskCanceledException) when (essai < 2)
            {
                System.Threading.Thread.Sleep(150);
            }
            catch (Exception ex) when (essai >= 2)
            {
                return "erreur atelier: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
        return "erreur atelier: connexion perdue apres 3 essais";
    }
}
