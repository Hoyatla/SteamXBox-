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

public static class Listes
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "liste_filtrer", "liste_filtrer",
            "Filtre une liste (JSON) selon un predicat. Format du predicat : ==valeur, !=valeur, >N, <N, contient(sub) (sans guillemets, juste la substring).",
            Espace.Codage, "Listes",
            new List<Port>
            {
                new("liste", TypePort.Texte, true),
                new("predicat", TypePort.Texte, true),
            },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("mode", "Mode", "liste", "garder", new List<string> { "garder", "retirer" }),
            },
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                var predRaw = ctx.Entree("predicat") ?? "";
                if (string.IsNullOrEmpty(predRaw))
                    return ResultatExecution.Fail("liste_filtrer: predicat vide");
                var mode = ctx.Ch("mode", "garder");
                JsonArray arr;
                try { arr = JsonNode.Parse(raw)?.AsArray() ?? new JsonArray(); }
                catch (Exception ex) { return ResultatExecution.Fail("liste_filtrer: JSON invalide : " + ex.Message); }
                var pred = ParsePredicat(predRaw);
                if (pred is null)
                    return ResultatExecution.Fail("liste_filtrer: predicat invalide : " + predRaw);
                var filtered = new JsonArray();
                foreach (var item in arr)
                {
                    var v = item?.ToJsonString().Trim('"') ?? "";
                    var match = EvalPredicat(pred, v);
                    if ((mode == "garder" && match) || (mode == "retirer" && !match))
                        filtered.Add(item?.DeepClone());
                }
                return ResultatExecution.Ok(new() { ["resultat"] = filtered.ToJsonString() });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "liste_trier", "liste_trier",
            "Trie une liste de dicts (JSON) par une cle. L'ordre peut etre ascendant ou descendant.",
            Espace.Codage, "Listes",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", ""),
                new("ordre", "Ordre", "liste", "ascendant", new List<string> { "ascendant", "descendant" }),
            },
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                var cle = ctx.Ch("cle", "");
                if (string.IsNullOrEmpty(cle))
                    return ResultatExecution.Fail("liste_trier: cle vide");
                var ordre = ctx.Ch("ordre", "ascendant");
                JsonArray arr;
                try { arr = JsonNode.Parse(raw)?.AsArray() ?? new JsonArray(); }
                catch (Exception ex) { return ResultatExecution.Fail("liste_trier: JSON invalide : " + ex.Message); }
                bool numeric = false;
                foreach (var n in arr)
                {
                    if (n is JsonObject o && o[cle] is JsonValue v && v.TryGetValue<double>(out _))
                    { numeric = true; break; }
                }
                var items = arr.ToList();
                items.Sort((a, b) =>
                {
                    var aV = a is JsonObject oa ? oa[cle] : a;
                    var bV = b is JsonObject ob ? ob[cle] : b;
                    int cmp;
                    if (numeric)
                    {
                        var an = aV?.GetValue<double>() ?? 0;
                        var bn = bV?.GetValue<double>() ?? 0;
                        cmp = an.CompareTo(bn);
                    }
                    else
                    {
                        cmp = string.Compare(aV?.ToJsonString() ?? "", bV?.ToJsonString() ?? "", StringComparison.Ordinal);
                    }
                    return ordre == "descendant" ? -cmp : cmp;
                });
                var result = new JsonArray();
                foreach (var it in items) result.Add(it);
                return ResultatExecution.Ok(new() { ["resultat"] = result.ToJsonString() });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "liste_unique", "liste_unique",
            "Dedoublonne une liste (JSON). L'ordre de la premiere occurrence est preserve.",
            Espace.Codage, "Listes",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                JsonArray arr;
                try { arr = JsonNode.Parse(raw)?.AsArray() ?? new JsonArray(); }
                catch (Exception ex) { return ResultatExecution.Fail("liste_unique: JSON invalide : " + ex.Message); }
                var seen = new HashSet<string>();
                var uniq = new JsonArray();
                foreach (var n in arr)
                {
                    var key = n?.ToJsonString() ?? "";
                    if (seen.Add(key)) uniq.Add(n?.DeepClone());
                }
                return ResultatExecution.Ok(new() { ["resultat"] = uniq.ToJsonString() });
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "liste_grouper", "liste_grouper",
            "Groupe une liste de dicts (JSON) par la valeur d'une cle. Sortie : dict de listes.",
            Espace.Codage, "Listes",
            new List<Port> { new("liste", TypePort.Texte, true) },
            new List<Port> { new("groupes", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", ""),
            },
            async ctx =>
            {
                var raw = ctx.Entree("liste") ?? "[]";
                var cle = ctx.Ch("cle", "");
                if (string.IsNullOrEmpty(cle))
                    return ResultatExecution.Fail("liste_grouper: cle vide");
                JsonArray arr;
                try { arr = JsonNode.Parse(raw)?.AsArray() ?? new JsonArray(); }
                catch (Exception ex) { return ResultatExecution.Fail("liste_grouper: JSON invalide : " + ex.Message); }
                var groups = new Dictionary<string, JsonArray>();
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    var key = o[cle]?.ToJsonString().Trim('"') ?? "";
                    if (!groups.TryGetValue(key, out var list)) { list = new JsonArray(); groups[key] = list; }
                    list.Add(n.DeepClone());
                }
                return ResultatExecution.Ok(new() { ["groupes"] = JsonSerializer.Serialize(groups) });
            }
        ));
    }

    // Mini-DSL predicat. Format supporte :
    // ==valeur, !=valeur, >N, <N, >=N, <=N, contient(substring) (sans guillemets)
    private record Predicat(string Op, string Value, bool Numeric);
    private static Predicat? ParsePredicat(string s)
    {
        s = s.Trim();
        if (s.StartsWith("==")) return new Predicat("==", s[2..].Trim(), false);
        if (s.StartsWith("!=")) return new Predicat("!=", s[2..].Trim(), false);
        if (s.StartsWith(">=")) return new Predicat(">=", s[2..].Trim(), true);
        if (s.StartsWith("<=")) return new Predicat("<=", s[2..].Trim(), true);
        if (s.StartsWith(">")) return new Predicat(">", s[1..].Trim(), true);
        if (s.StartsWith("<")) return new Predicat("<", s[1..].Trim(), true);
        // Verbatim string : contient(sub) -> on cherche "contient(" puis on prend le reste
        // jusqu'a la fin comme substring (pas de guillemets echappes)
        if (s.StartsWith("contient(", StringComparison.Ordinal))
        {
            var inner = s.Substring("contient(".Length);
            if (inner.EndsWith(")")) inner = inner.Substring(0, inner.Length - 1);
            return new Predicat("contient", inner, false);
        }
        return null;
    }
    private static bool EvalPredicat(Predicat p, string v)
    {
        if (p.Op == "contient") return v.Contains(p.Value, StringComparison.OrdinalIgnoreCase);
        if (p.Numeric && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var vn)
                   && double.TryParse(p.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var vt))
        {
            return p.Op switch
            {
                ">" => vn > vt,
                "<" => vn < vt,
                ">=" => vn >= vt,
                "<=" => vn <= vt,
                "==" => vn == vt,
                "!=" => vn != vt,
                _ => false,
            };
        }
        return p.Op switch
        {
            "==" => v == p.Value,
            "!=" => v != p.Value,
            _ => false,
        };
    }
}
