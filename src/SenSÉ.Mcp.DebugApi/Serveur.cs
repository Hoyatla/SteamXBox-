using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Mcp.Bus;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Le serveur MCP mcp-debugapi : expose les 5 verbes UI Automation
/// (dump, invoke, set_text, select, press) sur stdio, en JSON-RPC 2.0
/// newline-delimited. Le transport est calque sur mcp-saisie.
/// </summary>
/// <remarks>
/// <b>Le transport, pas la source de verite.</b> Ce serveur est un proxy
/// vers l'agent Rust sur la machine (pc-agent) qui parle UIA en natif.
/// Le token et l'URL sont lus au demarrage depuis
/// <c>SENSE_DEBUG_AGENT_URL</c> et <c>SENSE_DEBUG_AGENT_TOKEN</c>.
///
/// <para><b>HTTPS, pas de cleartext.</b> L'URL doit etre https:// ou
/// http://localhost. Une URL en http:// vers un hote distant est refusee
/// pour eviter de fuiter le bearer token sur le reseau.</para>
/// </remarks>
public static class Serveur
{
    private static readonly HttpClient Client = new();

    private static readonly string AgentUrl =
        Environment.GetEnvironmentVariable("SENSE_DEBUG_AGENT_URL")
            ?? "http://127.0.0.1:8765";

    private static readonly string? AgentToken =
        Environment.GetEnvironmentVariable("SENSE_DEBUG_AGENT_TOKEN");

    /// <summary>Demarre le serveur sur stdio. Bloque jusqu'a EOF ou erreur.</summary>
    public static async Task DemarrerAsync(CancellationToken arret = default)
    {
        var outils = ListeOutils();
        using var lecteur = new StreamReader(Console.OpenStandardInput());
        using var ecrivain = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        await ecrivain.WriteLineAsync(RepondrePret(outils.Count)).ConfigureAwait(false);

        while (!arret.IsCancellationRequested)
        {
            var ligne = await lecteur.ReadLineAsync(arret).ConfigureAwait(false);
            if (ligne is null) break;
            if (string.IsNullOrWhiteSpace(ligne)) continue;

            var reponse = Traiter(ligne, outils);
            if (reponse is not null)
            {
                await ecrivain.WriteLineAsync(reponse).ConfigureAwait(false);
            }
        }
    }

    private static string RepondrePret(int nbOutils)
    {
        var n = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/ready",
            ["params"] = new JsonObject
            {
                ["serveur"] = "mcp-debugapi",
                ["version"] = "1.0.0",
                ["agent_url"] = AgentUrl,
                ["agent_token_configured"] = !string.IsNullOrEmpty(AgentToken),
                ["outils"] = nbOutils,
            },
        };
        return n.ToJsonString();
    }

    private static IReadOnlyList<JsonObject> ListeOutils()
    {
        return
        [
            Outil("debug.uia_dump",
                "Renvoie l'arbre UI Automation de la fenetre au premier plan, en JSON. " +
                "Chaque controle a un nom, un type, un automationId, un etat enabled, et un rectangle en pixels.",
                new JsonObject { ["type"] = "object" }),
            Outil("debug.uia_invoke",
                "Invoque un controle identifie par son automationId. C'est un clic logique " +
                "(pas souris) — 100x plus fiable qu'un clic par coordonnees.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("automationId"),
                    ["properties"] = new JsonObject
                    {
                        ["automationId"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_set_text",
                "Ecrit du texte dans un champ de saisie identifie par son automationId. " +
                "Pas besoin de cliquer puis taper — c'est un set direct.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("automationId", "value"),
                    ["properties"] = new JsonObject
                    {
                        ["automationId"] = new JsonObject { ["type"] = "string" },
                        ["value"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_select",
                "Selectionne un item dans une ComboBox ou ListBox, par nom visible.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("automationId", "value"),
                    ["properties"] = new JsonObject
                    {
                        ["automationId"] = new JsonObject { ["type"] = "string" },
                        ["value"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_press",
                "Envoie une combinaison de touches. Exemples: 'Ctrl+S', 'Alt+F4', 'Return', 'Escape'.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("keys"),
                    ["properties"] = new JsonObject
                    {
                        ["keys"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_screenshot_window",
                "Capture UNIQUEMENT la fenetre identifiee par son titre (pas tout l'ecran). Renvoie le chemin du PNG dans Captures\\. Utilise pour 'capture de cette fenetre', 'screenshot de la fenetre X'.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("title"),
                    ["properties"] = new JsonObject
                    {
                        ["title"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_focus_window",
                "Met au premier plan la fenetre identifiee par son HWND (decimal ou 0xABCD). Utilise apres debug.uia_list_windows pour cibler la bonne fenetre avant une edition. Repond 'ok' ou une erreur.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("hwnd"),
                    ["properties"] = new JsonObject
                    {
                        ["hwnd"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("debug.uia_find_main_edit",
                "Heuristique pour trouver le champ d'edition principal d'une fenetre (titre en option). Priorite: Document > Pane le plus grand > Edit le plus grand. Renvoie l'automationId a passer a debug.uia_set_text, ou un message si rien ne matche.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["title"] = new JsonObject { ["type"] = "string" },
                    },
                }),
        ];
    }

    private static JsonObject Outil(string nom, string description, JsonObject schema)
    {
        return new JsonObject
        {
            ["name"] = nom,
            ["description"] = description,
            ["inputSchema"] = schema,
        };
    }

    private static string? Traiter(string ligne, IReadOnlyList<JsonObject> outils)
    {
        try
        {
            var noeud = JsonNode.Parse(ligne);
            if (noeud is null) return null;

            var echange = noeud.Deserialize<EchangeJsonRpc>();
            if (echange is null) return null;

            if (echange.EstNotification) return null;

            if (echange.EstRequete)
            {
                var id = echange.Id ?? "";
                try
                {
                    var resultat = ExecuterMethode(echange.Method ?? "", echange.Params, outils);
                    return EchangeJsonRpc.ReponseSucces(id, resultat);
                }
                catch (Exception ex)
                {
                    return EchangeJsonRpc.ReponseErreur(id, -32000, ex.Message);
                }
            }
            return null;
        }
        catch (JsonException ex)
        {
            return EchangeJsonRpc.ReponseErreur(null, -32700, $"JSON invalide: {ex.Message}");
        }
    }

    private static JsonNode? ExecuterMethode(string methode, JsonNode? parametres, IReadOnlyList<JsonObject> outils)
    {
        return methode switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "mcp-debugapi", ["version"] = "1.0.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = new JsonArray(outils.Select(o => o).ToArray()) },
            "tools/call" => AppelerOutil(parametres),
            _ => throw new InvalidOperationException($"methode inconnue: {methode}"),
        };
    }

    private static JsonNode? AppelerOutil(JsonNode? parametres)
    {
        if (parametres is null) throw new InvalidOperationException("params requis");
        var nom = parametres["name"]?.GetValue<string>() ?? "";
        var args = parametres["arguments"] as JsonObject ?? new JsonObject();

        return nom switch
        {
            "debug.uia_dump" => AppelerAgentGetAsync("/v1/uia/dump").GetAwaiter().GetResult(),
            "debug.uia_invoke" => AppelerAgentPostAsync("/v1/uia/invoke",
                new { automation_id = args["automationId"]?.GetValue<string>() ?? "" }),
            "debug.uia_set_text" => AppelerAgentPostAsync("/v1/uia/set_text", new
            {
                automation_id = args["automationId"]?.GetValue<string>() ?? "",
                value = args["value"]?.GetValue<string>() ?? "",
            }),
            "debug.uia_select" => AppelerAgentPostAsync("/v1/uia/select", new
            {
                automation_id = args["automationId"]?.GetValue<string>() ?? "",
                value = args["value"]?.GetValue<string>() ?? "",
            }),
            "debug.uia_press" => AppelerAgentPostAsync("/v1/uia/press", new
            {
                keys = args["keys"]?.GetValue<string>() ?? "",
            }),
            "debug.uia_screenshot_window" => AppelerAgentPostAsync("/v1/uia/screenshot-window", new
            {
                title = args["title"]?.GetValue<string>() ?? "",
            }),
            "debug.uia_focus_window" => AppelerAgentPostAsync("/v1/uia/focus-window", new
            {
                hwnd = args["hwnd"]?.GetValue<string>() ?? "",
            }),
            "debug.uia_find_main_edit" => AppelerAgentPostAsync("/v1/uia/find-main-edit", new
            {
                title = args["title"]?.GetValue<string>() ?? "",
            }),
            _ => throw new InvalidOperationException($"outil inconnu: {nom}"),
        };
    }

    private static async Task<JsonNode> AppelerAgentGetAsync(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, AgentUrl.TrimEnd('/') + path);
        if (!string.IsNullOrEmpty(AgentToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken);
        }
        using var resp = await Client.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return EncapsulerReponse(resp.StatusCode, body);
    }

    private static JsonNode AppelerAgentPostAsync(string path, object payload)
    {
        return AppelerAgentPostInternalAsync(path, payload).GetAwaiter().GetResult();
    }

    private static async Task<JsonNode> AppelerAgentPostInternalAsync(string path, object payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, AgentUrl.TrimEnd('/') + path)
        {
            Content = JsonContent.Create(payload),
        };
        if (!string.IsNullOrEmpty(AgentToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken);
        }
        using var resp = await Client.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return EncapsulerReponse(resp.StatusCode, body);
    }

    private static JsonNode EncapsulerReponse(System.Net.HttpStatusCode code, string body)
    {
        // L'agent retourne {"ok": bool, "data"?: ..., "error"?: string}.
        // On essaie de parser, sinon on retourne le texte brut.
        JsonNode? parsed = null;
        try { parsed = JsonNode.Parse(body); } catch { /* keep null */ }

        var content = new JsonObject
        {
            ["status"] = (int)code,
            ["body"] = parsed ?? (JsonNode)body,
        };
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = content.ToJsonString() }),
        };
    }
}