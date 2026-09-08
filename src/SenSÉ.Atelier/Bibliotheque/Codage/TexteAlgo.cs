using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

public static class TexteAlgo
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_regex", "texte_regex",
            "Cherche (ou remplace) un motif regex dans un texte. Vide le champ remplacement pour juste matcher.",
            Espace.Codage, "Texte",
            new List<Port>
            {
                new("texte", TypePort.Texte, true),
                new("pattern", TypePort.Texte, true),
            },
            new List<Port>
            {
                new("matches", TypePort.Texte, false),
                new("remplace", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("remplacement", "Remplacement (vide = juste matcher)", "texte", ""),
                new("flags", "Options", "liste", "aucun", new List<string> { "aucun", "ignore_case", "multiline" }),
            },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? "";
                var pattern = ctx.Entree("pattern") ?? "";
                if (string.IsNullOrEmpty(pattern))
                    return ResultatExecution.Fail("texte_regex: pattern vide");
                var options = RegexOptions.None;
                var flags = ctx.Ch("flags", "aucun");
                if (flags == "ignore_case") options |= RegexOptions.IgnoreCase;
                if (flags == "multiline") options |= RegexOptions.Multiline;
                Regex re;
                try { re = new Regex(pattern, options); }
                catch (Exception ex) { return ResultatExecution.Fail("texte_regex: pattern invalide : " + ex.Message); }
                var remplacement = ctx.Ch("remplacement", "");
                if (string.IsNullOrEmpty(remplacement))
                {
                    var matches = re.Matches(texte);
                    var arr = new JsonArray();
                    foreach (Match m in matches) arr.Add(m.Value);
                    return ResultatExecution.Ok(new()
                    {
                        ["matches"] = arr.ToJsonString(),
                        ["remplace"] = "",
                    });
                }
                return ResultatExecution.Ok(new()
                {
                    ["matches"] = "[]",
                    ["remplace"] = re.Replace(texte, remplacement),
                });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_split", "texte_split",
            "Decoupe un texte en liste autour d'un separateur. La sortie est une string JSON (ex: [\"a\",\"b\"]); branche-la sur liste_filtrer ou join.",
            Espace.Codage, "Texte",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("parties", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("separateur", "Separateur", "texte", ","),
            },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? "";
                var sep = ctx.Ch("separateur", ",");
                if (string.IsNullOrEmpty(sep))
                    return ResultatExecution.Fail("texte_split: separateur vide");
                var parties = texte.Split(new[] { sep }, StringSplitOptions.None);
                var arr = new JsonArray();
                foreach (var p in parties) arr.Add(p);
                return ResultatExecution.Ok(new() { ["parties"] = arr.ToJsonString() });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_join", "texte_join",
            "Joint une liste (JSON) en un texte avec un separateur.",
            Espace.Codage, "Texte",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port> { new("texte", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("separateur", "Separateur", "texte", ", "),
            },
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                var sep = ctx.Ch("separateur", ", ");
                List<string> parties;
                try
                {
                    var arr = JsonNode.Parse(raw)?.AsArray();
                    parties = arr is null
                        ? new List<string>()
                        : arr.Select(n => n?.GetValue<string>() ?? "").ToList();
                }
                catch (Exception ex) { return ResultatExecution.Fail("texte_join: JSON invalide : " + ex.Message); }
                return ResultatExecution.Ok(new() { ["texte"] = string.Join(sep, parties) });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_formater", "texte_formater",
            "Remplace les placeholders {nom}, {age}, etc. dans un modele par les valeurs passees en entree (JSON dict).",
            Espace.Codage, "Texte",
            new List<Port> { new("variables", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("modele", "Modele", "multiligne", "Bonjour {nom}, tu as {age} ans"),
            },
            async ctx =>
            {
                var modele = ctx.Ch("modele", "Bonjour {nom}, tu as {age} ans");
                var raw = ctx.Entree("variables") ?? "{}";
                Dictionary<string, string> vars;
                try
                {
                    var obj = JsonNode.Parse(raw)?.AsObject();
                    vars = obj is null
                        ? new Dictionary<string, string>()
                        : obj.ToDictionary(kv => kv.Key, kv => kv.Value?.ToJsonString().Trim('"') ?? "");
                }
                catch (Exception ex) { return ResultatExecution.Fail("texte_formater: JSON invalide : " + ex.Message); }
                // Verbatim string : \\w est \w en regex (1 backslash + w = word char)
                var resultat = Regex.Replace(modele, @"\\{(\\w+)\\}", m =>
                {
                    var key = m.Groups[1].Value;
                    return vars.TryGetValue(key, out var v) ? v : m.Value;
                });
                return ResultatExecution.Ok(new() { ["resultat"] = resultat });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_casse", "texte_casse",
            "Change la casse d'un texte (MAJUSCULES, minuscules, Title Case, Sentence case, Capitalize).",
            Espace.Codage, "Texte",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("casse", "Casse", "liste", "lower", new List<string> { "upper", "lower", "title_case", "sentence_case", "capitalize" }),
            },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? "";
                var casse = ctx.Ch("casse", "lower");
                string r = casse switch
                {
                    "upper" => texte.ToUpperInvariant(),
                    "lower" => texte.ToLowerInvariant(),
                    "title_case" => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(texte.ToLower()),
                    "sentence_case" => Regex.Replace(texte.ToLower(), @"(^|[.!?\s]+)([a-z])", m => m.Groups[1].Value + m.Groups[2].Value.ToUpper()),
                    "capitalize" => texte.Length == 0 ? "" : char.ToUpper(texte[0]) + texte.Substring(1),
                    _ => texte,
                };
                return ResultatExecution.Ok(new() { ["resultat"] = r });
            }
        ));
    }
}
