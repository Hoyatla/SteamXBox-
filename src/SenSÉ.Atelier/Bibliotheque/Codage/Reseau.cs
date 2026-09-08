using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 3 noeuds reseau : http_get, http_post, webhook (placeholder).
/// Utilisent HttpClient partage. Les timeouts respectent Annulation.
/// </summary>
public static class Reseau
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static void Enregistrer()
    {
        // 26. http_get
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "http_get", "http_get",
            "Envoie une requete HTTP GET a l'URL donnee et recupere le body + le code de statut. Les en-tetes sont au format JSON dict.",
            Espace.Codage, "Reseau",
            new List<Port> { new("url", TypePort.Texte, true) },
            new List<Port>
            {
                new("body", TypePort.Texte, false),
                new("statut", TypePort.Nombre, false),
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("headers", "En-tetes (JSON dict, optionnel)", "texte", ""),
            },
            async ctx =>
            {
                var url = ctx.Entree("url") ?? "";
                if (string.IsNullOrEmpty(url))
                    return ResultatExecution.Fail("http_get: url vide");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return ResultatExecution.Fail("http_get: url invalide : " + url);
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                    var headersRaw = ctx.Ch("headers", "");
                    if (!string.IsNullOrEmpty(headersRaw))
                    {
                        try
                        {
                            var hobj = JsonNode.Parse(headersRaw)?.AsObject();
                            if (hobj is not null)
                                foreach (var kv in hobj)
                                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value?.ToJsonString().Trim('"') ?? "");
                        }
                        catch (Exception ex)
                        {
                            return ResultatExecution.Fail("http_get: en-tetes JSON invalide : " + ex.Message);
                        }
                    }
                    using var resp = await _http.SendAsync(req, ctx.Annulation);
                    var body = await resp.Content.ReadAsStringAsync(ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["body"] = body,
                        ["statut"] = (int)resp.StatusCode,
                        ["ok"] = resp.IsSuccessStatusCode,
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return ResultatExecution.Fail("http_get: " + ex.Message); }
            }
        ));

        // 27. http_post
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "http_post", "http_post",
            "Envoie une requete HTTP POST (body texte + content-type) a l'URL donnee. Retourne body + statut.",
            Espace.Codage, "Reseau",
            new List<Port>
            {
                new("url", TypePort.Texte, true),
                new("body", TypePort.Texte, true),
            },
            new List<Port>
            {
                new("reponse", TypePort.Texte, false),
                new("statut", TypePort.Nombre, false),
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("content_type", "Content-Type", "texte", "application/json"),
            },
            async ctx =>
            {
                var url = ctx.Entree("url") ?? "";
                var body = ctx.Entree("body") ?? "";
                if (string.IsNullOrEmpty(url))
                    return ResultatExecution.Fail("http_post: url vide");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return ResultatExecution.Fail("http_post: url invalide : " + url);
                var contentType = ctx.Ch("content_type", "application/json");
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, uri)
                    {
                        Content = new StringContent(body, Encoding.UTF8, contentType),
                    };
                    using var resp = await _http.SendAsync(req, ctx.Annulation);
                    var respBody = await resp.Content.ReadAsStringAsync(ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["reponse"] = respBody,
                        ["statut"] = (int)resp.StatusCode,
                        ["ok"] = resp.IsSuccessStatusCode,
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return ResultatExecution.Fail("http_post: " + ex.Message); }
            }
        ));

        // 28. webhook (placeholder MVP)
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "webhook", "webhook",
            "Genere un ID de webhook unique. Le vrai point d'entree HTTP (POST /atelier/webhook/{id}) sera ajoute ulterieurement ; pour l'instant, le noeud sert a reserver un identifiant et stocker le payload pour inspection.",
            Espace.Codage, "Reseau",
            new List<Port> { new("payload", TypePort.Texte, true) },
            new List<Port>
            {
                new("webhook_id", TypePort.Texte, false),
                new("url", TypePort.Texte, false),
            },
            new List<ParametreNoeud>(),
            ctx =>
            {
                var payload = ctx.Entree("payload") ?? "";
                var id = Guid.NewGuid().ToString("N");
                // Stocker pour inspection future (en memoire seulement, reset au reboot de l'Atelier)
                WebhookStore.Inscrire(id, payload);
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["webhook_id"] = id,
                    ["url"] = "http://127.0.0.1:8770/atelier/webhook/" + id,
                }));
            }
        ));
    }
}

/// <summary>
/// Store en memoire pour les webhooks inscrits. Reset au reboot.
/// Singleton interne a Reseau.
/// </summary>
internal static class WebhookStore
{
    private static readonly Dictionary<string, string> _store = new();
    private static readonly object _lock = new();
    public static void Inscrire(string id, string payload)
    {
        lock (_lock) _store[id] = payload;
    }
    public static string? Lire(string id)
    {
        lock (_lock) return _store.TryGetValue(id, out var p) ? p : null;
    }
}
