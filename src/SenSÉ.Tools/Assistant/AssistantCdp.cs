using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de pilotage de webapps via Chrome DevTools Protocol
/// (CDP), branchees sur mcp-cdp (HTTP loopback 127.0.0.1:9224).
/// </summary>
/// <remarks>
/// <b>Quand utiliser CDP plutot que UIA ou mcp-saisie.</b> Pour les
/// applications web (MiniMax Code, dashboards, sites web) :
///   - UIA ne voit pas le contenu HTML (que la coquille du navigateur)
///   - mcp-saisie (souris/clavier) marche mais est fragile (pixel coords,
///     drift de la fenetre, etc.)
/// CDP parle directement au moteur du navigateur via WebSocket :
/// il accede au DOM, execute du JS, prend des screenshots reels.
///
/// <para><b>Navigateur par defaut.</b> mcp-cdp detecte Edge au boot
/// (sinon Chrome/Chromium). Une seule instance du navigateur est
/// lancee et partagee entre tous les appels.</para>
///
/// <para><b>Limitation importante.</b> Les sites avec des iframes
/// cross-origin ou du shadow DOM peuvent etre partiellement invisibles.
/// Pour la majorite des webapps, navigate + wait + click + type
/// + screenshot suffisent.</para>
/// </remarks>
public static class AssistantCdp
{
    private static HttpClient? _http;
    private static string _url = "";

    /// <summary>Initialise le client HTTP. Appele par <c>App.OnStartup</c> apres mcp-cdp.</summary>
    public static void Demarrer(string urlBase)
    {
        if (_http is not null && _url == urlBase) return;
        _url = urlBase.TrimEnd('/');
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>Construit la liste des Capacite CDP que l'Assistant peut appeler.</summary>
    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return
        [
            new AssistantLocal.Capacite(
                "cdp_navigate",
                "Navigue le navigateur integre vers une URL. Utilise pour ouvrir une webapp (MiniMax Code, dashboards, etc.).",
                [new AssistantLocal.Parametre("url", "L'URL complete (https://...)", [])],
                args => AppelerAsync("cdp/navigate", new { url = args.GetValueOrDefault("url") ?? "" })),

            new AssistantLocal.Capacite(
                "cdp_eval",
                "Execute du JavaScript dans la page courante. Renvoie la valeur de retour. Utilise pour lire le DOM, cliquer programmatiquement, scroller, etc.",
                [new AssistantLocal.Parametre("expression", "Le code JavaScript a executer (string)", [])],
                args => AppelerAsync("cdp/eval", new { expression = args.GetValueOrDefault("expression") ?? "" })),

            new AssistantLocal.Capacite(
                "cdp_click",
                "Clique sur le premier element matchant un selecteur CSS. Equivaut a document.querySelector(sel).click().",
                [new AssistantLocal.Parametre("selector", "Le selecteur CSS, ex: 'button.submit' ou '#login-btn'", [])],
                args => AppelerAsync("cdp/click", new { selector = args.GetValueOrDefault("selector") ?? "" })),

            new AssistantLocal.Capacite(
                "cdp_type",
                "Tape du texte dans un champ identifie par un selecteur CSS. Equivaut a .focus() + .value = X + dispatch events input/change.",
                [
                    new AssistantLocal.Parametre("selector", "Le selecteur CSS du champ", []),
                    new AssistantLocal.Parametre("text", "Le texte a taper", []),
                ],
                args => AppelerAsync("cdp/type", new { selector = args.GetValueOrDefault("selector") ?? "", text = args.GetValueOrDefault("text") ?? "" })),

            new AssistantLocal.Capacite(
                "cdp_wait",
                "Attend qu'un selecteur CSS apparaisse dans le DOM (utile apres navigation ou un clic qui declenche un render async).",
                [
                    new AssistantLocal.Parametre("selector", "Le selecteur CSS a attendre", []),
                    new AssistantLocal.Parametre("timeout", "Timeout en ms (defaut 5000)", []),
                ],
                args =>
                {
                    int timeout = 5000;
                    if (int.TryParse(args.GetValueOrDefault("timeout"), out var t)) timeout = t;
                    return AppelerAsync("cdp/wait", new { selector = args.GetValueOrDefault("selector") ?? "", timeout });
                }),

            new AssistantLocal.Capacite(
                "cdp_screenshot",
                "Screenshot de la page courante. Renvoie le chemin du PNG. Combine avec afficher_image pour l'injecter dans la conversation.",
                [new AssistantLocal.Parametre("path", "Chemin de sortie (optionnel, defaut Captures/cdp-*.png)", [])],
                args => AppelerAsync("cdp/screenshot", new { path = args.GetValueOrDefault("path") ?? "" })),
        ];
    }

    private static string AppelerAsync(string chemin, object body)
    {
        if (_http is null || string.IsNullOrEmpty(_url))
        {
            return "mcp-cdp: client non initialise (AssistantCdp.Demarrer pas appele)";
        }
        try
        {
            var url = _url + "/" + chemin.TrimStart('/');
            var task = _http.PostAsJsonAsync(url, body);
            using var resp = task.GetAwaiter().GetResult();
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return $"mcp-cdp: HTTP {(int)resp.StatusCode}: {json}";
            }
            try
            {
                var noeud = JsonNode.Parse(json);
                if (noeud is JsonObject obj)
                {
                    if (obj["ok"]?.GetValue<bool>() == false)
                    {
                                            var err = obj["error"];
                    return $"mcp-cdp: {(err is not null ? err.GetValue<string>() : json)}";
                    }
                    var data = obj["data"];
                    if (data is not null)
                    {
                        return data.ToJsonString();
                    }
                }
            }
            catch
            {
                // pas du JSON, on retourne le body brut
            }
            return json;
        }
        catch (Exception ex)
        {
            return $"erreur mcp-cdp: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
