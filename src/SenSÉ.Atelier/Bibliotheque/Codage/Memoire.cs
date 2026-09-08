using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Memoire;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 3 noeuds pour lire/ecrire dans la memoire partagee de l'Atelier.
/// La memoire survit aux redemarrages (serialisee dans partagee.json).
/// </summary>
public static class Memoire
{
    public static void Enregistrer()
    {
        // memoire_set
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "memoire_set", "memoire_set",
            "Stocke une valeur dans la memoire partagee de l'Atelier. La memoire survit aux redemarrages.",
            Espace.Codage, "Memoire",
            new List<Port> { new("valeur", TypePort.Texte, true) },
            new List<Port>
            {
                new("cle", TypePort.Texte, false),
                new("ok", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", ""),
                new("duree", "Duree", "liste", "session", new List<string> { "session", "permanent" }),
            },
            ctx =>
            {
                var cle = ctx.Ch("cle", "");
                if (string.IsNullOrEmpty(cle))
                    return Task.FromResult(ResultatExecution.Fail("memoire_set: cle vide"));
                var valeur = ctx.Entree("valeur") ?? "";
                try
                {
                    MemoirePartagee.Instance.Set(cle, valeur);
                    return Task.FromResult(ResultatExecution.Ok(new() { ["cle"] = cle, ["ok"] = true }));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(ResultatExecution.Fail("memoire_set: " + ex.Message));
                }
            }
        ));

        // memoire_get
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "memoire_get", "memoire_get",
            "Recupere une valeur de la memoire partagee. Renvoie la valeur par defaut si la cle n'existe pas.",
            Espace.Codage, "Memoire",
            new List<Port>(),
            new List<Port>
            {
                new("valeur", TypePort.Texte, false),
                new("existe", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", ""),
                new("defaut", "Defaut si absent", "texte", ""),
            },
            ctx =>
            {
                var cle = ctx.Ch("cle", "");
                if (string.IsNullOrEmpty(cle))
                    return Task.FromResult(ResultatExecution.Fail("memoire_get: cle vide"));
                var defaut = ctx.Ch("defaut", "");
                var existe = MemoirePartagee.Instance.Existe(cle);
                var valeur = existe ? MemoirePartagee.Instance.Get(cle, defaut) : defaut;
                return Task.FromResult(ResultatExecution.Ok(new() { ["valeur"] = valeur, ["existe"] = existe }));
            }
        ));

        // memoire_lister
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "memoire_lister", "memoire_lister",
            "Liste toutes les cles de la memoire partagee, en JSON liste de strings.",
            Espace.Codage, "Memoire",
            new List<Port>(),
            new List<Port>
            {
                new("cles", TypePort.Texte, false),
                new("nb", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>(),
            ctx =>
            {
                var cles = MemoirePartagee.Instance.Lister();
                var arr = new JsonArray();
                foreach (var c in cles) arr.Add(c);
                return Task.FromResult(ResultatExecution.Ok(new() { ["cles"] = arr.ToJsonString(), ["nb"] = cles.Count }));
            }
        ));
    }
}
