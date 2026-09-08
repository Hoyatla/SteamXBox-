using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SenSÉ.Atelier.Collab;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Templates;
using SenSÉ.Atelier.Webhook;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 6 noeuds Phase 4 :
/// - template_lister, template_charger, template_publier (3.2 marketplace)
/// - collab_connect, collab_diff, collab_resoudre (3.3 collab)
/// </summary>
public static class Phase4
{
    public static void Enregistrer()
    {
        EnregistrerTemplates();
        EnregistrerCollab();
    }

    static void EnregistrerTemplates()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "template_lister", "template_lister",
            "Liste les templates disponibles dans le marketplace local (Outils/Atelier/Templates/).",
            Espace.Codage, "Templates",
            new List<Port>(),
            new List<Port>
            {
                new("liste", TypePort.Texte, false),
                new("nb", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>(),
            ctx =>
            {
                var tpls = Templates.Templates.Lister(AppContext.BaseDirectory);
                var arr = new JsonArray();
                foreach (var t in tpls) arr.Add(new JsonObject
                {
                    ["id"] = t.Id,
                    ["nom"] = t.Nom,
                    ["description"] = t.Description,
                    ["auteur"] = t.Auteur,
                    ["tags"] = new JsonArray(t.Tags.Select(x => JsonValue.Create(x)).ToArray()),
                });
                return Task.FromResult(ResultatExecution.Ok(new() { ["liste"] = arr.ToJsonString(), ["nb"] = tpls.Count }));
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "template_charger", "template_charger",
            "Charge un template comme nouveau graphe. Renvoie le graphe serialise en JSON (a sauvegarder via le verbe graphe/nouveau).",
            Espace.Codage, "Templates",
            new List<Port>(),
            new List<Port>
            {
                new("graphe_json", TypePort.Texte, false),
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("template_id", "ID du template", "texte", ""),
            },
            ctx =>
            {
                var tid = ctx.Ch("template_id", "");
                if (string.IsNullOrEmpty(tid)) return Task.FromResult(ResultatExecution.Fail("template_charger: template_id vide"));
                var g = Templates.Templates.Charger(AppContext.BaseDirectory, tid);
                if (g is null) return Task.FromResult(ResultatExecution.Fail("template_charger: template introuvable : " + tid));
                return Task.FromResult(ResultatExecution.Ok(new() { ["graphe_json"] = g.Nom, ["ok"] = true }));
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "template_publier", "template_publier",
            "Publie le graphe actuel comme template (sauvegarde dans Outils/Atelier/Templates/).",
            Espace.Codage, "Templates",
            new List<Port>(),
            new List<Port>
            {
                new("template_id", TypePort.Texte, false),
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "Graphe a publier", "texte", ""),
                new("nom", "Nom du template", "texte", "Mon template"),
                new("description", "Description", "texte", ""),
                new("tags_json", "Tags (JSON array de strings)", "texte", "[]"),
            },
            ctx =>
            {
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid)) return Task.FromResult(ResultatExecution.Fail("template_publier: graphe_id vide"));
                var g = new Persistance(AppContext.BaseDirectory).ChargerGraphe(gid);
                if (g is null) return Task.FromResult(ResultatExecution.Fail("template_publier: graphe introuvable"));
                var nom = ctx.Ch("nom", "Mon template");
                var desc = ctx.Ch("description", "");
                var tags = new List<string>();
                try
                {
                    var n = JsonNode.Parse(ctx.Ch("tags_json", "[]"));
                    if (n is JsonArray arr) foreach (var t in arr) if (t is JsonValue jv) tags.Add(jv.GetValue<string>() ?? "");
                }
                catch { /* ignore */ }
                if (!Templates.Templates.Publier(AppContext.BaseDirectory, g, nom, desc, tags, out var err))
                    return Task.FromResult(ResultatExecution.Fail("template_publier: " + err));
                return Task.FromResult(ResultatExecution.Ok(new() { ["template_id"] = nom, ["ok"] = true }));
            }
        ));
    }

    static void EnregistrerCollab()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "collab_connect", "collab_connect",
            "Genere un identifiant de connexion unique pour la session collaborative. MVP : sans diffusion temps reel, juste un UUID a passer au verbe /atelier/sync/push.",
            Espace.Codage, "Collab",
            new List<Port>(),
            new List<Port>
            {
                new("connexion_id", TypePort.Texte, false),
            },
            new List<ParametreNoeud>(),
            ctx =>
            {
                var cid = Guid.NewGuid().ToString("N");
                return Task.FromResult(ResultatExecution.Ok(new() { ["connexion_id"] = cid }));
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "collab_diff", "collab_diff",
            "Calcule un diff simple entre 2 graphes (nombre de modifs enregistrees pour chaque graphe).",
            Espace.Codage, "Collab",
            new List<Port>(),
            new List<Port>
            {
                new("diff", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id_a", "Graphe A", "texte", ""),
                new("graphe_id_b", "Graphe B", "texte", ""),
            },
            ctx =>
            {
                var a = ctx.Ch("graphe_id_a", "");
                var b = ctx.Ch("graphe_id_b", "");
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return Task.FromResult(ResultatExecution.Fail("collab_diff: graphe_id_a et _b requis"));
                var d = Collab.Collab.Diff(AppContext.BaseDirectory, a, b);
                return Task.FromResult(ResultatExecution.Ok(new() { ["diff"] = System.Text.Json.JsonSerializer.Serialize(d) }));
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "collab_resoudre", "collab_resoudre",
            "Resout un conflit (MVP : last-write-wins, on rejoue juste la modif winner).",
            Espace.Codage, "Collab",
            new List<Port>(),
            new List<Port>
            {
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "Graphe concerne", "texte", ""),
                new("resolution_json", "Resolution (JSON : {type, user, payload})", "texte", "{}"),
            },
            ctx =>
            {
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid)) return Task.FromResult(ResultatExecution.Fail("collab_resoudre: graphe_id requis"));
                try
                {
                    var n = JsonNode.Parse(ctx.Ch("resolution_json", "{}"));
                    if (n is not JsonObject resolution) return Task.FromResult(ResultatExecution.Fail("collab_resoudre: resolution_json pas un objet"));
                    if (!Collab.Collab.Resoudre(AppContext.BaseDirectory, gid, resolution, out var err))
                        return Task.FromResult(ResultatExecution.Fail("collab_resoudre: " + err));
                    return Task.FromResult(ResultatExecution.Ok(new() { ["ok"] = true }));
                }
                catch (Exception ex) { return Task.FromResult(ResultatExecution.Fail("collab_resoudre: " + ex.Message)); }
            }
        ));
    }
}
