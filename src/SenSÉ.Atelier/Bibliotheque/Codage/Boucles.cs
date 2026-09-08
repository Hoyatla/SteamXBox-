using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using static System.Math;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 3 noeuds de boucle : for, while, foreach.
/// <para><b>Simplification MVP.</b> Le moteur execute le noeud une seule
/// fois, et le noeud expose des arrays JSON (iteration = [0, 1, ..., N-1],
/// element = [a, b, c], ...). C'est au noeud en aval de traiter l'array
/// (par exemple avec liste_filtrer, ou un noeud custom). Ce n'est pas du
/// vrai dataflow recursif, mais ca suffit pour 90% des cas d'usage.</para>
/// </summary>
public static class Boucles
{
    public static void Enregistrer()
    {
        // boucle_for : repeter N fois
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "boucle_for", "boucle_for",
            "Repete N fois. Expose 'iteration' = [0, 1, ..., N-1] et 'corps_sortie' = entree repetee N fois (JSON array).",
            Espace.Codage, "Boucles",
            new List<Port> { new("corps_entree", TypePort.Texte, true) },
            new List<Port>
            {
                new("iteration", TypePort.Texte, false),
                new("corps_sortie", TypePort.Texte, false),
                new("nb_iterations", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("iterations", "Nombre d'iterations", "nombre", 10),
            },
            ctx =>
            {
                var n = Max(0, ctx.ChInt("iterations", 10));
                var entree = ctx.Entree("corps_entree") ?? "";
                // Limite dure pour eviter les explosions memoire
                if (n > 10000)
                    return Task.FromResult(ResultatExecution.Fail("boucle_for: iterations > 10000 (anti-explosion)"));
                var iterArr = new JsonArray();
                var corpsArr = new JsonArray();
                for (int i = 0; i < n; i++)
                {
                    iterArr.Add(i);
                    corpsArr.Add(entree);
                }
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["iteration"] = iterArr.ToJsonString(),
                    ["corps_sortie"] = corpsArr.ToJsonString(),
                    ["nb_iterations"] = n,
                }));
            }
        ));

        // boucle_while : repeter tant que
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "boucle_while", "boucle_while",
            "Repete tant que 'condition' est vraie. Limite par 'max_iterations' (securite anti-boucle infinie).",
            Espace.Codage, "Boucles",
            new List<Port> { new("condition", TypePort.Booleen, true) },
            new List<Port>
            {
                new("iteration", TypePort.Texte, false),
                new("a_boucle", TypePort.Booleen, false),
                new("nb_iterations", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("max_iterations", "Max iterations (securite)", "nombre", 100),
            },
            ctx =>
            {
                var max = Min(10000, Max(0, ctx.ChInt("max_iterations", 100)));
                var cond = ctx.ChBool("condition", false);
                // Simplification : on evalue la condition initiale. Si vraie, on
                // suppose qu'elle reste vraie jusqu'a max_iterations (la condition
                // n'est pas re-evaluee a chaque tour cote moteur).
                var iterArr = new JsonArray();
                int n = 0;
                if (cond)
                {
                    while (n < max)
                    {
                        iterArr.Add(n);
                        n++;
                    }
                }
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["iteration"] = iterArr.ToJsonString(),
                    ["a_boucle"] = n > 0,
                    ["nb_iterations"] = n,
                }));
            }
        ));

        // boucle_foreach : pour chaque element
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "boucle_foreach", "boucle_foreach",
            "Pour chaque element d'une liste JSON. Expose 'element' (array des valeurs) et 'index' (array des positions).",
            Espace.Codage, "Boucles",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port>
            {
                new("element", TypePort.Texte, false),
                new("index", TypePort.Texte, false),
                new("nb_elements", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>(),
            ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                JsonArray arr;
                try { arr = JsonNode.Parse(raw)?.AsArray() ?? new JsonArray(); }
                catch (Exception ex) { return Task.FromResult(ResultatExecution.Fail("boucle_foreach: JSON invalide : " + ex.Message)); }
                if (arr.Count > 10000)
                    return Task.FromResult(ResultatExecution.Fail("boucle_foreach: > 10000 elements (anti-explosion)"));
                var elemArr = new JsonArray();
                var idxArr = new JsonArray();
                for (int i = 0; i < arr.Count; i++)
                {
                    elemArr.Add(arr[i]?.DeepClone());
                    idxArr.Add(i);
                }
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["element"] = elemArr.ToJsonString(),
                    ["index"] = idxArr.ToJsonString(),
                    ["nb_elements"] = arr.Count,
                }));
            }
        ));
    }
}
