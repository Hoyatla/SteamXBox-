using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de pilotage en mode souris/clavier, branchees sur mcp-saisie
/// via HTTP loopback. Pour les applications qui ne sont pas UIA-friendly
/// (LibreOffice Writer, vieux logiciels, apps qui utilisent UNO/AT-SPI
/// au lieu de UI Automation), UIA ne voit que la coquille, pas le contenu.
/// Il faut alors passer en mode souris + clavier.
/// </summary>
/// <remarks>
/// <b>Le pont : mcp-saisie.exe + HTTP loopback.</b> mcp-saisie est un
/// serveur HTTP sur 127.0.0.1:port (defaut 8766). Cette classe lui parle
/// via HttpClient. Le binaire doit etre dans
/// <c>Outils\McpSaisie\SenSÉ.Mcp.Saisie.exe</c> a cote de
/// <c>SenSÉ.Desktop.exe</c>, et SenSÉ.Desktop le lance au boot avec
/// <c>--port 8766</c>.
///
/// <para><b>Pourquoi HTTP, pas stdio.</b> Stdio sous un binaire
/// self-contained single-file avec UseWindowsForms=true ne marche pas
/// fiable quand on lance le binaire via Process.Start avec redirection :
/// le pipe ne recoit pas les donnees du parent. Un socket TCP loopback
/// n'a pas ce probleme.</para>
///
/// <para><b>Loopback = pas d'auth.</b> mcp-saisie n'authentifie pas les
/// requetes venant de 127.0.0.1. Si tu veux ajouter un token, mets-le
/// dans l'env var SENSE_SAISIE_TOKEN et ajoute-le ici en Bearer header.</para>
///
/// <para><b>Mode Exclusif visuel.</b> Les actions souris/clavier doivent
/// etre encadrees par <c>saisie_mode_exclusif_ouvrir</c> et
/// <c>saisie_mode_exclusif_fermer</c> : un bandeau "L'ASSISTANT PILOTE"
/// informe l'utilisateur que la souris va bouger, et un Echap interrompt
/// la sequence. Voir <see cref="saisie_mode_exclusif_ouvrir"/> dans la
/// consigne pour le detail du workflow.</para>
/// </remarks>
public static class AssistantSaisie
{
    private static HttpClient? _http;
    private static string _urlBase = "";

    /// <summary>
    /// Initialise le client HTTP. Appele par <c>App.OnStartup</c> apres
    /// que mcp-saisie a ete lance. Idempotent.
    /// </summary>
    public static void Demarrer(string urlBase, string? token = null)
    {
        if (_http is not null && _urlBase == urlBase) return;
        _urlBase = urlBase.TrimEnd('/');
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }
    }

    /// <summary>
    /// Construit la liste des Capacite de pilotage souris/clavier que
    /// l'Assistant peut appeler. A ajouter a la liste passee a
    /// <see cref="AssistantLocal.Repondre"/>.
    /// </summary>
    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return
        [
            new AssistantLocal.Capacite(
                "saisie_screenshot_ecran",
                "Screenshot de l'ecran entier (PNG dans Outils\\Captures). Utilise quand UIA ne marche pas (ex: LibreOffice Writer) et que tu as besoin de voir l'ecran entier pour reperer ou cliquer.",
                [],
                args => AppelerSaisie("screenshot_ecran", null)),

            new AssistantLocal.Capacite(
                "saisie_screenshot_fenetre",
                "Screenshot d'une fenetre specifique identifiee par son titre (PNG). Utilise apres un focus_window pour confirmer ce que tu vois dans cette fenetre.",
                [new AssistantLocal.Parametre("titre", "Le titre (ou partie) de la fenetre", [])],
                args => AppelerSaisie("screenshot_fenetre", new { titre = args.GetValueOrDefault("titre") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_lister_fenetres",
                "Liste les fenetres visibles (titre + HWND). Utilise quand debug_uia_list_windows ne voit pas la cible : apps non-UIA comme LibreOffice.",
                [],
                args => AppelerSaisie("lister_fenetres", null)),

            new AssistantLocal.Capacite(
                "saisie_souris_deplacer",
                "Deplace le curseur a une position absolue (coords ecran en pixels). Mode Exclusif visuel obligatoire (saisie_mode_exclusif_ouvrir avant).",
                [
                    new AssistantLocal.Parametre("x", "Coordonnee X en pixels", []),
                    new AssistantLocal.Parametre("y", "Coordonnee Y en pixels", []),
                ],
                args => AppelerSaisie("souris_deplacer", new
                {
                    x = int.Parse(args.GetValueOrDefault("x") ?? "0"),
                    y = int.Parse(args.GetValueOrDefault("y") ?? "0"),
                })),

            new AssistantLocal.Capacite(
                "saisie_souris_cliquer",
                "Clic souris (gauche par defaut). Mode Exclusif visuel obligatoire. Tu peux passer x/y pour cliquer a des coords precises, sinon le clic part au curseur actuel.",
                [
                    new AssistantLocal.Parametre("bouton", "gauche, droit ou milieu", ["gauche", "droit", "milieu"]),
                    new AssistantLocal.Parametre("doubles", "true pour double-clic (defaut false)", []),
                    new AssistantLocal.Parametre("x", "Coord X optionnel", []),
                    new AssistantLocal.Parametre("y", "Coord Y optionnel", []),
                ],
                args =>
                {
                    var body = new Dictionary<string, object>
                    {
                        ["bouton"] = args.GetValueOrDefault("bouton") ?? "gauche",
                    };
                    if (bool.TryParse(args.GetValueOrDefault("doubles") ?? "false", out var d) && d)
                    {
                        body["doubles"] = true;
                    }
                    if (int.TryParse(args.GetValueOrDefault("x"), out var x))
                    {
                        body["x"] = x;
                    }
                    if (int.TryParse(args.GetValueOrDefault("y"), out var y))
                    {
                        body["y"] = y;
                    }
                    return AppelerSaisie("souris_cliquer", body);
                }),

            new AssistantLocal.Capacite(
                "saisie_clavier_taper",
                "Tape une chaine de caracteres (un caractere a la fois). Mode Exclusif visuel obligatoire.",
                [new AssistantLocal.Parametre("texte", "Le texte a taper", [])],
                args => AppelerSaisie("clavier_taper", new { texte = args.GetValueOrDefault("texte") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_clavier_touche",
                "Appuie sur une touche speciale ou combinaison. Alias francais OK. Ex: 'Ctrl+S', 'Return', 'Echap', 'Impr', 'F5', 'Tab'.",
                [new AssistantLocal.Parametre("touche", "Le nom de la touche ou combinaison (Ctrl+S, Return, Echap, F1..F12, fleches)", [])],
                args => AppelerSaisie("clavier_touche", new { touche = args.GetValueOrDefault("touche") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_mode_exclusif_ouvrir",
                "Ouvre le bandeau 'L'ASSISTANT PILOTE' (Mode Exclusif visuel). A appeler AVANT toute action souris/clavier. L'utilisateur peut interrompre immediatement avec Echap. Une seule sequence par session d'edition.",
                [new AssistantLocal.Parametre("sequence", "Description courte de ce que tu vas faire, ex: 'clic dans Writer'", [])],
                args => AppelerSaisie("mode_exclusif_ouvrir", new { sequence = args.GetValueOrDefault("sequence") ?? "action Assistant" })),

            new AssistantLocal.Capacite(
                "saisie_mode_exclusif_fermer",
                "Ferme le bandeau Mode Exclusif. A appeler APRES la derniere action souris/clavier (sans condition, en finally logique).",
                [],
                args => AppelerSaisie("mode_exclusif_fermer", null)),
        ];
    }

    /// <summary>
    /// Appelle le tool mcp-saisie par son nom (le segment apres /saisie/
    /// dans l'URL). Renvoie la string retournee par l'outil, ou un
    /// message d'erreur formate si le serveur n'est pas joignable.
    /// </summary>
    private static string AppelerSaisie(string tool, object? body)
    {
        if (_http is null || string.IsNullOrEmpty(_urlBase))
        {
            return "mcp-saisie: client non initialise (AssistantSaisie.Demarrer pas appele)";
        }
        // Retry sur HttpRequestException : le serveur coupe la connexion
        // apres chaque requete (Connection: close), donc la 1ere requete
        // apres un idle long peut echouer. On reessaie 2 fois de plus.
        for (int essai = 0; essai < 3; essai++)
        {
            try
            {
                var url = _urlBase + "/saisie/" + tool;
                var task = body is null
                    ? _http.GetAsync(url)
                    : _http.PostAsJsonAsync(url, body);
                using var resp = task.GetAwaiter().GetResult();
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    return $"mcp-saisie: HTTP {(int)resp.StatusCode}: {json}";
                }
                // Parse la reponse JSON : { "ok": true, "data": "..." } ou { "ok": false, "error": "..." }
                try
                {
                    var node = JsonNode.Parse(json);
                    if (node is JsonObject obj)
                    {
                        if (obj["ok"]?.GetValue<bool>() == false)
                        {
                            return $"mcp-saisie: {obj["error"]?.GetValue<string>() ?? json}";
                        }
                        var data = obj["data"];
                        if (data is not null)
                        {
                            return data.GetValue<string>() ?? data.ToJsonString();
                        }
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
                // Connexion coupee par le serveur (idle trop long, ou
                // restart du serveur). On attend un peu et on reessaie.
                System.Threading.Thread.Sleep(150);
            }
            catch (TaskCanceledException) when (essai < 2)
            {
                // Timeout. On reessaie.
                System.Threading.Thread.Sleep(150);
            }
            catch (Exception ex) when (essai >= 2)
            {
                // Dernier essai : on remonte l'erreur.
                return $"erreur mcp-saisie: {ex.GetType().Name}: {ex.Message}";
            }
        }
        return "erreur mcp-saisie: connexion perdue apres 3 essais";
    }
}
