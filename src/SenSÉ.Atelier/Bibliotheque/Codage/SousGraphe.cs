using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Noeud sous_graphe_appel : execute un autre graphe de l'Atelier comme
/// sous-programme. Resultat = sorties du dernier noeud (terminaux) au format JSON.
/// </summary>
public static class SousGraphe
{
    private static Persistance? _persistance;

    /// <summary>Le serveur initialise la persistance partagee pour que les
    /// noeuds sous_graphe_appel puissent charger d'autres graphes.</summary>
    public static void Initialiser(string racine)
    {
        _persistance = new Persistance(racine);
    }

    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "sous_graphe_appel", "sous_graphe_appel",
            "Appelle un autre graphe de l'Atelier. Mode 'copier_entrees' : les entrees du contexte sont passees au sous-graphe. Mode 'isole' : contexte vide.",
            Espace.Codage, "Composite",
            new List<Port>(),
            new List<Port>
            {
                new("resultat", TypePort.Texte, false),
                new("execution_id", TypePort.Texte, false),
                new("statut", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "ID du graphe a appeler", "texte", ""),
                new("mode", "Mode", "liste", "copier_entrees", new List<string> { "copier_entrees", "isole" }),
                new("entrees_json", "Entrees (JSON dict, optionnel)", "texte", ""),
            },
            async ctx =>
            {
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid))
                    return ResultatExecution.Fail("sous_graphe_appel: graphe_id vide");
                if (_persistance is null)
                    return ResultatExecution.Fail("sous_graphe_appel: persistance non initialisee");
                var sub = _persistance.ChargerGraphe(gid);
                if (sub is null)
                    return ResultatExecution.Fail("sous_graphe_appel: graphe introuvable : " + gid);
                var mode = ctx.Ch("mode", "copier_entrees");
                Dictionary<string, object?>? entree = null;
                if (mode == "copier_entrees")
                {
                    var raw = ctx.Ch("entrees_json", "");
                    if (!string.IsNullOrEmpty(raw))
                    {
                        try
                        {
                            var n = System.Text.Json.Nodes.JsonNode.Parse(raw);
                            if (n is System.Text.Json.Nodes.JsonObject obj)
                            {
                                entree = new Dictionary<string, object?>();
                                foreach (var kv in obj) entree[kv.Key] = kv.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            return ResultatExecution.Fail("sous_graphe_appel: entrees_json invalide : " + ex.Message);
                        }
                    }
                }
                var exec = Moteur.Instance.LancerSync(sub, entree);
                var sorties = new System.Text.Json.Nodes.JsonObject();
                foreach (var kv in exec.SortiesFinales) sorties[kv.Key] = kv.Value is System.Text.Json.Nodes.JsonNode jn ? jn.DeepClone() : System.Text.Json.Nodes.JsonValue.Create(kv.Value?.ToString() ?? "");
                return ResultatExecution.Ok(new()
                {
                    ["resultat"] = sorties.ToJsonString(),
                    ["execution_id"] = exec.ExecutionId,
                    ["statut"] = exec.Statut.ToString(),
                });
            }
        ));
    }
}
