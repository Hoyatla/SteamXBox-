using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
/// <b>Phase 4.1</b> : auth configurable par webhook (token Bearer ou HMAC-SHA256).
/// La config est lue depuis Outils/Atelier/Webhook/config.json a chaque
/// requete (donc les changements de config sont pris en compte sans
/// redemarrage du serveur).
///
/// <b>Logs</b> : chaque declenchement est append dans
/// Outils/Atelier/Webhook/logs/YYYY-MM-DD.log au format JSONL.
/// </remarks>
public sealed class ServeurWebhook : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _racine;
    private readonly Persistance _persistance;
    private readonly WebhookConfigStore _store;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _logsDir;
    private bool _disposed;

    public int Port { get; }
    public string Racine => _racine;
    public WebhookConfigStore Store => _store;

    public ServeurWebhook(string racine, int port = 8772)
    {
        _racine = racine;
        Port = port;
        _persistance = new Persistance(racine);
        _store = new WebhookConfigStore(racine);
        _store.Charger();
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
            Log("?", null, "erreur_demarrage", ex.Message);
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
                Log("?", null, "erreur_boucle", ex.Message);
                continue;
            }
            _ = Task.Run(() => TraiterAsync(ctx));
        }
    }

    private async Task TraiterAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        var chemin = req.Url?.AbsolutePath?.Trim('/') ?? "";
        try
        {
            if (req.HttpMethod != "POST")
            {
                await Repondre(resp, 405, new { ok = false, error = "methode " + req.HttpMethod + " non supportée" });
                return;
            }

            // Lecture du body
            string body = "";
            using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
            {
                body = await sr.ReadToEndAsync();
            }

            // Lookup par chemin
            var cfg = _store.TrouverParChemin(chemin);
            if (cfg is null)
            {
                Log(chemin, body, "chemin_inconnu", "");
                await Repondre(resp, 404, new { ok = false, error = "chemin webhook inconnu : " + chemin });
                return;
            }

            // Verification auth
            if (!VerifierAuth(req, cfg, body, out var errAuth))
            {
                Log(chemin, body, "auth_ko", errAuth);
                await Repondre(resp, 401, new { ok = false, error = "auth: " + errAuth });
                return;
            }

            // Lookup graphe
            var graphe = _persistance.ChargerGraphe(cfg.GrapheId);
            if (graphe is null)
            {
                Log(chemin, body, "graphe_introuvable", cfg.GrapheId);
                await Repondre(resp, 404, new { ok = false, error = "graphe introuvable : " + cfg.GrapheId });
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
                    Log(chemin, body, "body_invalide", ex.Message);
                    await Repondre(resp, 400, new { ok = false, error = "body JSON invalide : " + ex.Message });
                    return;
                }
            }

            var exec = Moteur.Instance.LancerAsync(graphe, entree);
            Log(chemin, body, "declenche", $"{cfg.GrapheId} -> {exec.ExecutionId}");
            await Repondre(resp, 200, new { ok = true, data = new { execution_id = exec.ExecutionId, statut = exec.Statut.ToString() } });
        }
        catch (Exception ex)
        {
            Log(chemin, "", "exception", ex.Message);
            try { await Repondre(resp, 500, new { ok = false, error = ex.Message }); } catch { }
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    /// <summary>Verifie l'auth selon le mode du webhook.</summary>
    private static bool VerifierAuth(HttpListenerRequest req, WebhookConfig cfg, string body, out string err)
    {
        err = "";
        switch (cfg.Mode)
        {
            case WebhookAuthMode.Aucun:
                return true;
            case WebhookAuthMode.Token:
                var auth = req.Headers["Authorization"];
                if (string.IsNullOrEmpty(auth)) { err = "header Authorization manquant"; return false; }
                var expected = "Bearer " + cfg.Token;
                if (!string.Equals(auth, expected, StringComparison.Ordinal)) { err = "token invalide"; return false; }
                return true;
            case WebhookAuthMode.Hmac:
                var sig = req.Headers["X-Signature"];
                if (string.IsNullOrEmpty(sig)) { err = "header X-Signature manquant"; return false; }
                if (cfg.SecretHmac is null) { err = "secret_hmac non configure"; return false; }
                // Format attendu : "sha256=<hex>"
                var prefix = "sha256=";
                if (!sig.StartsWith(prefix)) { err = "format signature invalide (attendu: sha256=<hex>)"; return false; }
                var recuHex = sig.Substring(prefix.Length).Trim();
                var calcule = HmacSha256Hex(cfg.SecretHmac, body);
                if (!string.Equals(recuHex, calcule, StringComparison.OrdinalIgnoreCase)) { err = "signature HMAC invalide"; return false; }
                return true;
        }
        err = "mode auth inconnu";
        return false;
    }

    /// <summary>Calcule HMAC-SHA256(secret, body) en hex.</summary>
    public static string HmacSha256Hex(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
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

    private void Log(string chemin, string? body, string statut, string detail)
    {
        try
        {
            var fichier = Path.Combine(_logsDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            var ligne = JsonSerializer.Serialize(new
            {
                ts = DateTime.Now.ToString("o"),
                chemin,
                body_len = body?.Length ?? 0,
                body_preview = body is { Length: > 500 } ? body.Substring(0, 500) + "..." : body,
                statut,
                detail,
            });
            File.AppendAllText(fichier, ligne + "\n", Encoding.UTF8);
        }
        catch { }
    }
}
