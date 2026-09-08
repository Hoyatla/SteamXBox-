using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Atelier.Webhook;

/// <summary>
/// Mode d'authentification pour un webhook.
/// </summary>
public enum WebhookAuthMode
{
    /// <summary>Pas d'auth (utile en dev local). Toute requete est acceptee.</summary>
    Aucun,
    /// <summary>Token Bearer dans Authorization header.</summary>
    Token,
    /// <summary>Signature HMAC-SHA256 dans X-Signature: sha256=<hex>.</summary>
    Hmac,
}

/// <summary>
/// Configuration d'un webhook : id unique, graphe cible, chemin HTTP,
/// mode d'auth, et (selon le mode) token ou secret HMAC.
/// </summary>
public sealed class WebhookConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GrapheId { get; set; } = "";
    /// <summary>Chemin HTTP sur le serveur webhook (ex: "/webhook/myhook").</summary>
    public string Chemin { get; set; } = "";
    public WebhookAuthMode Mode { get; set; } = WebhookAuthMode.Aucun;
    public string? Token { get; set; }
    public string? SecretHmac { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persistance de la liste des webhooks configures dans
/// Outils/Atelier/Webhook/config.json.
/// </summary>
public sealed class WebhookConfigStore
{
    private readonly string _fichier;
    private List<WebhookConfig> _configs = new();
    private readonly object _lock = new();

    public WebhookConfigStore(string racine)
    {
        var dir = Path.Combine(racine, "Webhook");
        Directory.CreateDirectory(dir);
        _fichier = Path.Combine(dir, "config.json");
    }

    public void Charger()
    {
        try
        {
            if (!File.Exists(_fichier)) { _configs = new(); return; }
            var json = File.ReadAllText(_fichier);
            var n = JsonNode.Parse(json);
            if (n is not JsonObject root) { _configs = new(); return; }
            var arr = root["webhooks"] as JsonArray;
            if (arr is null) { _configs = new(); return; }
            _configs = new List<WebhookConfig>();
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                var c = new WebhookConfig
                {
                    Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    GrapheId = o["graphe_id"]?.GetValue<string>() ?? "",
                    Chemin = o["chemin"]?.GetValue<string>() ?? "",
                };
                var modeStr = o["mode"]?.GetValue<string>() ?? "aucun";
                c.Mode = modeStr switch
                {
                    "token" => WebhookAuthMode.Token,
                    "hmac" => WebhookAuthMode.Hmac,
                    _ => WebhookAuthMode.Aucun,
                };
                c.Token = o["token"]?.GetValue<string>();
                c.SecretHmac = o["secret_hmac"]?.GetValue<string>();
                _configs.Add(c);
            }
        }
        catch { _configs = new(); }
    }

    public void Sauvegarder()
    {
        try
        {
            var arr = new JsonArray();
            lock (_lock)
            {
                foreach (var c in _configs)
                {
                    var o = new JsonObject
                    {
                        ["id"] = c.Id,
                        ["graphe_id"] = c.GrapheId,
                        ["chemin"] = c.Chemin,
                        ["mode"] = c.Mode switch
                        {
                            WebhookAuthMode.Token => "token",
                            WebhookAuthMode.Hmac => "hmac",
                            _ => "aucun",
                        },
                    };
                    if (c.Token is not null) o["token"] = c.Token;
                    if (c.SecretHmac is not null) o["secret_hmac"] = c.SecretHmac;
                    arr.Add(o);
                }
            }
            var root = new JsonObject { ["webhooks"] = arr };
            File.WriteAllText(_fichier, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public List<WebhookConfig> Lister()
    {
        lock (_lock) return _configs.ToList();
    }

    public WebhookConfig? TrouverParChemin(string chemin)
    {
        lock (_lock) return _configs.FirstOrDefault(c => string.Equals(c.Chemin, chemin, StringComparison.OrdinalIgnoreCase));
    }

    public WebhookConfig? TrouverParId(string id)
    {
        lock (_lock) return _configs.FirstOrDefault(c => c.Id == id);
    }

    public WebhookConfig Ajouter(WebhookConfig c)
    {
        lock (_lock) _configs.Add(c);
        Sauvegarder();
        return c;
    }

    public bool Supprimer(string id)
    {
        lock (_lock)
        {
            var n = _configs.RemoveAll(c => c.Id == id);
            if (n > 0) Sauvegarder();
            return n > 0;
        }
    }

    public WebhookAuthMode ParseMode(string s) => s switch
    {
        "token" => WebhookAuthMode.Token,
        "hmac" => WebhookAuthMode.Hmac,
        _ => WebhookAuthMode.Aucun,
    };
}
