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

        // Genere le README.md au premier demarrage. Fire-and-forget : un
        // fichier absent n est pas un bloquant, et le catalogue peut etre
        // vide au premier appel (donc le faire ici, en parallele du HTTP).
        _ = Task.Run(() => ProposerReadmeSiAbsent());

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

    /// <summary>
    /// Ecrit Outils/Atelier/README.md si le fichier est absent. Le contenu
    /// est le meme Markdown que le verbe /atelier/catalogue/aide, plus une
    /// entete qui documente les verbes HTTP de l Atelier.
    /// </summary>
    private void ProposerReadmeSiAbsent()
    {
        try
        {
            var chemin = System.IO.Path.Combine(_verbes.Racine, "README.md");
            if (System.IO.File.Exists(chemin)) return;

            // AppelerVerbeSync enveloppe la reponse : {ok:true, data: <verbe>}.
            // Le verbe catalogue/aide rend lui-meme {ok:true, data:{markdown:...}},
            // donc on doit descendre deux niveaux.
            var aide = this.AppelerVerbeSync("catalogue/aide", null, null);
            if (aide is not System.Text.Json.Nodes.JsonObject obj
                || obj["ok"]?.GetValue<bool>() != true) return;
            var outer = obj["data"] as System.Text.Json.Nodes.JsonObject;
            var inner = outer?["data"] as System.Text.Json.Nodes.JsonObject;
            var mdCatalogue = inner?["markdown"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mdCatalogue)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Atelier SenSE");
            sb.AppendLine();
            sb.AppendLine("> Fichier genere automatiquement au premier demarrage. Il documente le catalogue");
            sb.AppendLine("> des types de noeuds et les verbes HTTP exposes. Pour le regenerer, supprimer ce fichier");
            sb.AppendLine("> et relancer l Atelier (ou taper POST /atelier/catalogue/aide).");
            sb.AppendLine();
            sb.AppendLine("## Demarrage");
            sb.AppendLine();
            sb.AppendLine("L Atelier est lance en mode headless par SenSE.Desktop (cf. ServeurAtelier.cs).");
            sb.AppendLine("Il ecoute sur http://127.0.0.1:8770 (variable d env SENSE_ATELIER_URL).");
            sb.AppendLine();
            sb.AppendLine("## Verbes HTTP");
            sb.AppendLine();
            sb.AppendLine("Tous les verbes sont sous /atelier/<verbe>, POST avec body JSON. Format de reponse : {ok:true,data:...} ou {ok:false,error:...}.");
            sb.AppendLine();
            sb.AppendLine("### Catalogue");
            sb.AppendLine("- POST /atelier/catalogue/espaces : liste des espaces");
            sb.AppendLine("- POST /atelier/catalogue/types?espace=codage : types d un espace");
            sb.AppendLine("- POST /atelier/catalogue/type?type_id=executer_code : detail d un type");
            sb.AppendLine("- POST /atelier/catalogue/aide : ce fichier en JSON (champ markdown)");
            sb.AppendLine();
            sb.AppendLine("### Graphes");
            sb.AppendLine("- POST /atelier/graphe/lister");
            sb.AppendLine("- POST /atelier/graphe/nouveau body {espace, nom}");
            sb.AppendLine("- POST /atelier/graphe/charger body {graphe_id}");
            sb.AppendLine("- POST /atelier/graphe/renommer body {graphe_id, nom}");
            sb.AppendLine("- POST /atelier/graphe/dupliquer body {graphe_id}");
            sb.AppendLine("- POST /atelier/graphe/supprimer body {graphe_id, espace}");
            sb.AppendLine("- POST /atelier/graphe/exporter body {graphe_id, chemin}");
            sb.AppendLine("- POST /atelier/graphe/importer body {chemin}");
            sb.AppendLine();
            sb.AppendLine("### Noeuds et liens");
            sb.AppendLine("- POST /atelier/noeud/ajouter body {graphe_id, type, x, y, params?}");
            sb.AppendLine("- POST /atelier/noeud/supprimer body {noeud_id}");
            sb.AppendLine("- POST /atelier/noeud/deplacer body {noeud_id, x, y}");
            sb.AppendLine("- POST /atelier/noeud/modifier body {noeud_id, params}");
            sb.AppendLine("- POST /atelier/noeud/dupliquer body {noeud_id}");
            sb.AppendLine("- POST /atelier/lien/creer body {graphe_id, port_source:{noeud_id,port_nom}, port_cible:{noeud_id,port_nom}}");
            sb.AppendLine("- POST /atelier/lien/supprimer body {lien_id}");
            sb.AppendLine();
            sb.AppendLine("### Execution");
            sb.AppendLine("- POST /atelier/executer body {graphe_id, entree?} -> {execution_id}");
            sb.AppendLine("- POST /atelier/executer_noeud body {noeud_id, entree?}");
            sb.AppendLine("- GET  /atelier/execution/etat?execution_id=... : statut, noeuds, sorties");
            sb.AppendLine("- POST /atelier/execution/annuler body {execution_id}");
            sb.AppendLine();
            sb.AppendLine("### Exemples");
            sb.AppendLine("- POST /atelier/exemples/lister : index des exemples (cf. Exemples/index.json)");
            sb.AppendLine("- POST /atelier/exemples/charger body {exemple_id} : charge un exemple comme nouveau graphe");
            sb.AppendLine();
            sb.AppendLine("### Fenetre (headless)");
            sb.AppendLine("- POST /atelier/fenetre/etat : {headless, fenetre_visible}");
            sb.AppendLine("- POST /atelier/fenetre/ouvrir : affiche la fenetre WPF (no-op si deja visible)");
            sb.AppendLine();
            sb.AppendLine("### Undo/redo");
            sb.AppendLine("- POST /atelier/annuler body {graphe_id}");
            sb.AppendLine("- POST /atelier/refaire body {graphe_id}");
            sb.AppendLine();
            sb.AppendLine("### Custom");
            sb.AppendLine("- POST /atelier/custom/lister : noeuds custom charges depuis NoeudsCustom/");
            sb.AppendLine();
            sb.AppendLine("## Catalogue des types de noeuds");
            sb.AppendLine();
            sb.Append(mdCatalogue);

            System.IO.File.WriteAllText(chemin, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine("[atelier] README.md non genere : " + ex.GetType().Name + ": " + ex.Message);
        }
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
