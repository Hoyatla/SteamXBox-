using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Custom;

namespace SenSÉ.Atelier.Mcp;

/// <summary>
/// Serveur HTTP loopback sur 127.0.0.1:8770. Accepte les verbes
/// <c>/atelier/&lt;verbe&gt;</c> en GET/POST et repond en JSON.
/// </summary>
public sealed class ServeurHttp
{
    private readonly HttpListener _listener;
    private readonly Verbes _verbes;
    private readonly CancellationTokenSource _cts = new();
    public int Port { get; }

    public ServeurHttp(string racinePersistance, int port = 8770)
    {
        Port = port;
        _verbes = new Verbes(racinePersistance);
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:" + port + "/atelier/");
        _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
    }

    public Task DemarrerAsync()
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        CatalogueCustom.Recharger(_verbes.Racine);
        _listener.Start();
        return Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => TraiterAsync(ctx));
            }
        });
    }

    public void Arreter()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    /// <summary>
    /// Appelle synchrone en interne : permet a l'UI WPF (meme processus) de
    /// declencher un verbe HTTP sans passer par un socket. Utile pour les
    /// actions qui n'ont pas de sens en dehors du processus courant
    /// (exemples : l'UI a besoin de lister les exemples mais eviterait de
    /// se rappeler a elle-meme via 127.0.0.1:8770, ce qui ajoute des retries,
    /// un timeout, et un aller-retour inutile).
    /// </summary>
    public JsonObject? AppelerVerbeSync(string verbe, JsonObject? body, IReadOnlyDictionary<string, string>? query)
    {
        try
        {
            var data = _verbes.Appeler(verbe, body, query ?? new Dictionary<string, string>());
            if (data is JsonObject jo) return jo;
            if (data is null) return null;
            // Cas rare : un verbe qui rend autre chose qu'un JsonObject (aujourd'hui
            // tous les verbes rendent {ok, data} ou {ok:false, error}). On emballe.
            return new JsonObject
            {
                ["ok"] = true,
                ["data"] = JsonSerializer.SerializeToNode(data),
            };
        }
        catch (Exception ex)
        {
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }
    }

    private async Task TraiterAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Url?.AbsolutePath ?? "/";

            if (path == "/" || path == "")
            {
                var nbEsp = System.Enum.GetValues<SenSÉ.Atelier.Modele.Espace>().Length;
                await EcrireJson(ctx, 200, new
                {
                    ok = true, serveur = "atelier", version = "0.1.0", espaces = nbEsp,
                });
                return;
            }

            const string prefixe = "/atelier/";
            if (!path.StartsWith(prefixe))
            {
                await EcrireJson(ctx, 404, new { ok = false, error = "route inconnue: " + path });
                return;
            }
            var verbe = path.Substring(prefixe.Length);

            JsonObject? body = null;
            if (req.HasEntityBody)
            {
                using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                var text = await sr.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try { body = JsonNode.Parse(text)?.AsObject(); }
                    catch { }
                }
            }

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(req.Url?.Query))
            {
                foreach (var part in req.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2) query[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                    else if (kv.Length == 1) query[Uri.UnescapeDataString(kv[0])] = "";
                }
            }

            var data = _verbes.Appeler(verbe, body, query);
            await EcrireJson(ctx, 200, data ?? new { ok = false, error = "resultat null" });
        }
        catch (Exception ex)
        {
            await EcrireJson(ctx, 500, new { ok = false, error = ex.Message });
        }
    }

    private static async Task EcrireJson(HttpListenerContext ctx, int status, object data)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
}
