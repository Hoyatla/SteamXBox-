using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Planificateur;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 2 noeuds : controle_si (skip conditionnel) + planificateur_ajouter (cron).
/// </summary>
public static class Controle
{
    public static void Enregistrer()
    {
        // controle_si : executer si...
        // Le moteur (Moteur.ExecuterInterneAsync) regarde si un controle_si en
        // amont a emis 'skip' (= !condition en mode executer_si_vrai) et
        // marque tous les noeuds en aval comme desactives. Ces noeuds
        // retournent un ResultatExecution vide mais Reussi.
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "controle_si", "controle_si",
            "Routeur conditionnel. Si la condition matche, propage l'entree vers 'alors'. Sinon, marque les noeuds en aval comme desactives (le moteur les skip).",
            Espace.Codage, "Controle",
            new List<Port>
            {
                new("condition", TypePort.Booleen, true),
                new("entree", TypePort.Texte, true),
            },
            new List<Port>
            {
                new("alors", TypePort.Texte, false),
                new("skip_downstream", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("mode", "Mode", "liste", "executer_si_vrai", new List<string> { "executer_si_vrai", "executer_si_faux" }),
            },
            ctx =>
            {
                var cond = ctx.ChBool("condition", false);
                var entree = ctx.Entree("entree") ?? "";
                var mode = ctx.Ch("mode", "executer_si_vrai");
                bool doitExecuter = (mode == "executer_si_vrai") ? cond : !cond;
                // Bascule le flag dans le contexte : le moteur va propager
                // aux noeuds en aval et skip si false.
                if (!doitExecuter) ctx.DesactiveAval = true;
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["alors"] = doitExecuter ? entree : "",
                    ["skip_downstream"] = !doitExecuter,
                }));
            }
        ));

        // planificateur_ajouter : ajoute une tache cron pour ce graphe
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "planificateur_ajouter", "planificateur_ajouter",
            "Ajoute ce graphe (ou un graphe specifie) au planificateur avec une expression cron. Format cron : 'minute heure jour mois jour-semaine' (5 champs).",
            Espace.Codage, "Controle",
            new List<Port>(),
            new List<Port>
            {
                new("tache_id", TypePort.Texte, false),
                new("prochain", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "Graphe cible (vide = ce graphe)", "texte", ""),
                new("cron", "Expression cron", "texte", "0 9 * * *"),
            },
            ctx =>
            {
                var cron = ctx.Ch("cron", "0 9 * * *");
                var grapheId = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(grapheId))
                    grapheId = ctx.NoeudId; // fallback : l'ID du noeud (sera traite par le moteur)
                if (string.IsNullOrEmpty(cron))
                    return Task.FromResult(ResultatExecution.Fail("planificateur_ajouter: cron vide"));
                if (!SenSÉ.Atelier.Planificateur.Planificateur.Instance.Ajouter(grapheId, cron, out var prochain, out var err))
                    return Task.FromResult(ResultatExecution.Fail("planificateur_ajouter: " + err));
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["tache_id"] = grapheId + " @ " + cron,
                    ["prochain"] = prochain.ToString("o"),
                }));
            }
        ));
    }
}
