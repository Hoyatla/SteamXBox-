using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SenSÉ.Atelier.CanvasLayout;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Noeud layout_ranger : applique l'auto-layout (BFS en grille) au graphe.
/// </summary>
public static class LayoutRanger
{
    private static Persistance? _persistance;

    public static void Initialiser(string racine)
    {
        _persistance = new Persistance(racine);
    }

    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "layout_ranger", "layout_ranger",
            "Range automatiquement le graphe (BFS en grille, 250px entre rangees, 200px entre noeuds).",
            Espace.Codage, "Controle",
            new List<Port>(),
            new List<Port>
            {
                new("nb_deplaces", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "ID du graphe (vide = ce noeud)", "texte", ""),
                new("sens", "Sens", "liste", "horizontal", new List<string> { "horizontal", "vertical" }),
            },
            ctx =>
            {
                if (_persistance is null) return Task.FromResult(ResultatExecution.Fail("layout_ranger: persistance non initialisee"));
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid)) return Task.FromResult(ResultatExecution.Fail("layout_ranger: graphe_id vide"));
                var g = _persistance.ChargerGraphe(gid);
                if (g is null) return Task.FromResult(ResultatExecution.Fail("layout_ranger: graphe introuvable : " + gid));
                var sens = ctx.Ch("sens", "horizontal") == "vertical" ? AutoLayout.Sens.Vertical : AutoLayout.Sens.Horizontal;
                var nb = AutoLayout.Appliquer(g, sens);
                _persistance.SauvegarderGraphe(g);
                return Task.FromResult(ResultatExecution.Ok(new() { ["nb_deplaces"] = nb }));
            }
        ));
    }
}
