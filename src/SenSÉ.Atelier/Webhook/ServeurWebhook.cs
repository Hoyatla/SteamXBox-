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
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Webhook;

/// <summary>
/// Serveur HTTP dedie aux webhooks entrants. Ecoute sur 127.0.0.1:8772
/// (8770 reste l'API MCP de l'Atelier).
/// </summary>
/// <remarks>
/// <b>POST /webhook/{graphe_id}</b> : declenche l'execution du graphe et
/// retourne {execution_id, statut: 'EnCours'}. Le body est ignore (c'est
/// juste un trigger), mais il est loggue.
///
/// <b>Logs</b> : chaque declenchement est append dans
/// Outils/Atelier/Webhook/logs/YYYY-MM-DD.log au format JSONL.
/// </remarks>
public sealed class ServeurWebhook : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _racine;
    private readonly Persistance _persistance;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _logsDir;
    private bool _disposed;

    public int Port { get; }
    public string Racine => _racine;

    public ServeurWebhook(string racine, int port = 8772)
    {
        _racine = racine;
        Port = port;
        _persistance = new Persistance(racine);
        _logsDir = Path.Combine(racine, "Webhook", "logs");
        Directory.CreateDirectory(_logsDir);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Demarrer()
    {
        try
        {
            _listener.Start();
            _ = Task.Run(() => BoucleAsync(_cts.Token));
        }
        catch (HttpListenerException ex)
        {
            Log("demarrage", null, "erreur", ex.Message);
            throw;
        }
    }

    public void Arreter()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    public void Dispose() => Arreter();

    private async Task BoucleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                Log("boucle", null, "erreur", ex.Message);
                continue;
            }
            _ = Task.Run(() => TraiterAsync(ctx));
        }
    }

    private async Task TraiterAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        string grapheId = "?";
        try
        {
            // /webhook/{graphe_id} ou /webhook/{graphe_id}/{...}
            var path = req.Url?.AbsolutePath?.Trim('/') ?? "";
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "webhook")
            {
                await Repondre(resp, 404, new { ok = false, error = "endpoint inconnu. Attendu: POST /webhook/{graphe_id}" });
                return;
            }
            grapheId = parts[1];

            string body = "";
            using (var sr = new StreamReader(req.InputStream, req.ContentEncoding!))
            {
                body = await sr.ReadToEndAsync();
            }

            if (req.HttpMethod != "POST")
            {
                await Repondre(resp, 405, new { ok = false, error = "methode " + req.HttpMethod + " non supportée" });
                return;
            }

            var graphe = _persistance.ChargerGraphe(grapheId);
            if (graphe is null)
            {
                Log(grapheId, body, "graphe_introuvable", "");
                await Repondre(resp, 404, new { ok = false, error = "graphe introuvable : " + grapheId });
                return;
            }

            Dictionary<string, object?>? entree = null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var n = JsonNode.Parse(body);
                    if (n is JsonObject obj)
                    {
                        entree = new Dictionary<string, object?>();
                        foreach (var kv in obj) entree[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    Log(grapheId, body, "body_invalide", ex.Message);
                    await Repondre(resp, 400, new { ok = false, error = "body JSON invalide : " + ex.Message });
                    return;
                }
            }

            var exec = Moteur.Instance.LancerAsync(graphe, entree);
            Log(grapheId, body, "declenche", exec.ExecutionId);
            await Repondre(resp, 200, new { ok = true, data = new { execution_id = exec.ExecutionId, statut = exec.Statut.ToString() } });
        }
        catch (Exception ex)
        {
            Log(grapheId, "", "exception", ex.Message);
            try { await Repondre(resp, 500, new { ok = false, error = ex.Message }); } catch { }
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    private static async Task Repondre(HttpListenerResponse resp, int code, object payload)
    {
        resp.StatusCode = code;
        resp.ContentType = "application/json; charset=utf-8";
        var body = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(body);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private void Log(string grapheId, string? body, string statut, string detail)
    {
        try
        {
            var fichier = Path.Combine(_logsDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            var ligne = JsonSerializer.Serialize(new
            {
                ts = DateTime.Now.ToString("o"),
                graphe = grapheId,
                body_len = body?.Length ?? 0,
                body_preview = body is { Length: > 500 } ? body.Substring(0, 500) + "..." : body,
                statut,
                detail,
            });
            File.AppendAllText(fichier, ligne + "\n", Encoding.UTF8);
        }
        catch { /* pas de crash si log impossible */ }
    }
}
