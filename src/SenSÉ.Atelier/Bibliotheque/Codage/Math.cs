using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using static System.Math;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

public static class Math
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "math_operation", "math_operation",
            "Une operation entre 2 nombres. Choisis +, -, *, /, %, ^.",
            Espace.Codage, "Math",
            new List<Port>
            {
                new("a", TypePort.Nombre, true),
                new("b", TypePort.Nombre, true),
            },
            new List<Port> { new("resultat", TypePort.Nombre, false) },
            new List<ParametreNoeud>
            {
                new("operation", "Operation", "liste", "+", new List<string> { "+", "-", "*", "/", "%", "^" }),
            },
            async ctx =>
            {
                if (!double.TryParse(ctx.Entree("a"), NumberStyles.Any, CultureInfo.InvariantCulture, out var a))
                    return ResultatExecution.Fail("math_operation: 'a' n'est pas un nombre");
                if (!double.TryParse(ctx.Entree("b"), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    return ResultatExecution.Fail("math_operation: 'b' n'est pas un nombre");
                var op = ctx.Ch("operation", "+");
                double r = op switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    "/" => b == 0 ? double.NaN : a / b,
                    "%" => b == 0 ? double.NaN : a % b,
                    "^" => Pow(a, b),
                    _ => double.NaN,
                };
                if (double.IsNaN(r) && (op == "/" || op == "%") && b == 0)
                    return ResultatExecution.Ok(new() { ["resultat"] = r },
                        "math_operation: division par zero -> NaN");
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "math_fonction", "math_fonction",
            "Une fonction mathematique unaire (sin, cos, sqrt, log, etc.).",
            Espace.Codage, "Math",
            new List<Port> { new("valeur", TypePort.Nombre, true) },
            new List<Port> { new("resultat", TypePort.Nombre, false) },
            new List<ParametreNoeud>
            {
                new("fonction", "Fonction", "liste", "sin", new List<string> { "sin", "cos", "tan", "sqrt", "log", "abs", "floor", "ceil", "round" }),
            },
            async ctx =>
            {
                if (!double.TryParse(ctx.Entree("valeur"), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    return ResultatExecution.Fail("math_fonction: 'valeur' n'est pas un nombre");
                var f = ctx.Ch("fonction", "sin");
                double r = f switch
                {
                    "sin" => Sin(v),
                    "cos" => Cos(v),
                    "tan" => Tan(v),
                    "sqrt" => v < 0 ? double.NaN : Sqrt(v),
                    "log" => v <= 0 ? double.NaN : Log(v),
                    "abs" => Abs(v),
                    "floor" => Floor(v),
                    "ceil" => Ceiling(v),
                    "round" => Round(v, MidpointRounding.AwayFromZero),
                    _ => double.NaN,
                };
                if (double.IsNaN(r) && (f == "sqrt" || f == "log"))
                    return ResultatExecution.Ok(new() { ["resultat"] = r },
                        $"math_fonction: {f}({v}) indefini -> NaN");
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "math_statistiques", "math_statistiques",
            "Calcule somme, moyenne, min, max, mediane d'une liste de nombres. La liste est passee en JSON, ex: [1, 2, 3, 4, 5].",
            Espace.Codage, "Math",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port>
            {
                new("somme", TypePort.Nombre, false),
                new("moyenne", TypePort.Nombre, false),
                new("min", TypePort.Nombre, false),
                new("max", TypePort.Nombre, false),
                new("mediane", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                List<double> vs;
                try
                {
                    var arr = JsonNode.Parse(raw)?.AsArray();
                    vs = arr is null
                        ? new List<double>()
                        : arr.Select(n => n?.GetValue<double>() ?? double.NaN)
                           .Where(v => !double.IsNaN(v)).ToList();
                }
                catch (Exception ex)
                {
                    return ResultatExecution.Fail("math_statistiques: JSON invalide : " + ex.Message);
                }
                if (vs.Count == 0)
                    return ResultatExecution.Fail("math_statistiques: liste vide ou aucun nombre valide");
                vs.Sort();
                var somme = vs.Sum();
                var moyenne = somme / vs.Count;
                var min = vs.First();
                var max = vs.Last();
                var mediane = (vs.Count % 2 == 1)
                    ? vs[vs.Count / 2]
                    : (vs[vs.Count / 2 - 1] + vs[vs.Count / 2]) / 2.0;
                return ResultatExecution.Ok(new()
                {
                    ["somme"] = somme,
                    ["moyenne"] = moyenne,
                    ["min"] = min,
                    ["max"] = max,
                    ["mediane"] = mediane,
                });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "math_aleatoire", "math_aleatoire",
            "Tire un nombre entier aleatoire entre min (inclus) et max (exclus). Thread-safe via Random.Shared.",
            Espace.Codage, "Math",
            new List<Port>(),
            new List<Port> { new("valeur", TypePort.Nombre, false) },
            new List<ParametreNoeud>
            {
                new("min", "Min (inclus)", "nombre", 0),
                new("max", "Max (exclus)", "nombre", 100),
            },
            async ctx =>
            {
                var min = ctx.ChInt("min", 0);
                var max = ctx.ChInt("max", 100);
                if (max <= min)
                    return ResultatExecution.Fail($"math_aleatoire: max ({max}) doit etre > min ({min})");
                return ResultatExecution.Ok(new() { ["valeur"] = Random.Shared.Next(min, max) });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "math_arrondir", "math_arrondir",
            "Arrondit un nombre. Choisis la methode (round, floor, ceil, truncate) et le nombre de decimales.",
            Espace.Codage, "Math",
            new List<Port> { new("valeur", TypePort.Nombre, true) },
            new List<Port> { new("resultat", TypePort.Nombre, false) },
            new List<ParametreNoeud>
            {
                new("methode", "Methode", "liste", "round", new List<string> { "round", "floor", "ceil", "truncate" }),
                new("decimales", "Decimales", "nombre", 2),
            },
            async ctx =>
            {
                if (!double.TryParse(ctx.Entree("valeur"), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    return ResultatExecution.Fail("math_arrondir: 'valeur' n'est pas un nombre");
                var methode = ctx.Ch("methode", "round");
                var d = ctx.ChInt("decimales", 2);
                double r = methode switch
                {
                    "round" => Round(v, d, MidpointRounding.AwayFromZero),
                    "floor" => Floor(v * Pow(10, d)) / Pow(10, d),
                    "ceil" => Ceiling(v * Pow(10, d)) / Pow(10, d),
                    "truncate" => Truncate(v * Pow(10, d)) / Pow(10, d),
                    _ => v,
                };
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));
    }
}
