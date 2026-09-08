using System;
using System.Collections.Generic;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

public static class Logique
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "logique_si", "logique_si",
            "Routeur conditionnel. Mode 'binaire' : prend l'entree et la sort vers 'alors' si Vrai, 'sinon' si Faux. 'garder_si_vrai'/'garder_si_faux' : laisse passer l'entree ou la remplace par une chaine vide.",
            Espace.Codage, "Logique",
            new List<Port>
            {
                new("condition", TypePort.Booleen, true),
                new("entree", TypePort.Texte, true),
            },
            new List<Port>
            {
                new("alors", TypePort.Texte, false),
                new("sinon", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("mode", "Mode", "liste", "binaire", new List<string> { "binaire", "garder_si_vrai", "garder_si_faux" }),
            },
            async ctx =>
            {
                var cond = ctx.ChBool("condition", false);
                var entree = ctx.Entree("entree") ?? "";
                var mode = ctx.Ch("mode", "binaire");
                if (mode == "garder_si_vrai")
                    return ResultatExecution.Ok(new() { ["alors"] = cond ? entree : "", ["sinon"] = "" });
                if (mode == "garder_si_faux")
                    return ResultatExecution.Ok(new() { ["alors"] = cond ? "" : entree, ["sinon"] = cond ? entree : "" });
                return ResultatExecution.Ok(new() { ["alors"] = cond ? entree : "", ["sinon"] = cond ? "" : entree });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "logique_comparer", "logique_comparer",
            "Compare 2 chaines selon l'operateur choisi (==, !=, <, >, contient, commence_par, finit_par).",
            Espace.Codage, "Logique",
            new List<Port>
            {
                new("a", TypePort.Texte, true),
                new("b", TypePort.Texte, true),
            },
            new List<Port> { new("resultat", TypePort.Booleen, false) },
            new List<ParametreNoeud>
            {
                new("operateur", "Operateur", "liste", "==", new List<string> { "==", "!=", "<", ">", "<=", ">=", "contient", "commence_par", "finit_par" }),
            },
            async ctx =>
            {
                var a = ctx.Entree("a") ?? "";
                var b = ctx.Entree("b") ?? "";
                var op = ctx.Ch("operateur", "==");
                bool r = op switch
                {
                    "==" => a == b,
                    "!=" => a != b,
                    "<" => string.Compare(a, b, StringComparison.Ordinal) < 0,
                    ">" => string.Compare(a, b, StringComparison.Ordinal) > 0,
                    "<=" => string.Compare(a, b, StringComparison.Ordinal) <= 0,
                    ">=" => string.Compare(a, b, StringComparison.Ordinal) >= 0,
                    "contient" => a.Contains(b, StringComparison.Ordinal),
                    "commence_par" => a.StartsWith(b, StringComparison.Ordinal),
                    "finit_par" => a.EndsWith(b, StringComparison.Ordinal),
                    _ => false,
                };
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "logique_et_ou", "logique_et_ou",
            "Applique ET ou OU sur les entrees booleennes non-nulles. Utile pour combiner des conditions.",
            Espace.Codage, "Logique",
            new List<Port>
            {
                new("a", TypePort.Booleen, true),
                new("b", TypePort.Booleen, true),
                new("c", TypePort.Booleen, true),
                new("d", TypePort.Booleen, true),
            },
            new List<Port> { new("resultat", TypePort.Booleen, false) },
            new List<ParametreNoeud>
            {
                new("operateur", "Operateur", "liste", "ET", new List<string> { "ET", "OU" }),
            },
            async ctx =>
            {
                var op = ctx.Ch("operateur", "ET");
                var vals = new List<bool>
                {
                    ctx.ChBool("a", false),
                    ctx.ChBool("b", false),
                };
                if (ctx.Entree("c") is not null) vals.Add(ctx.ChBool("c", false));
                if (ctx.Entree("d") is not null) vals.Add(ctx.ChBool("d", false));
                bool r = op == "ET" ? vals.All(v => v) : vals.Any(v => v);
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));
    }
}
