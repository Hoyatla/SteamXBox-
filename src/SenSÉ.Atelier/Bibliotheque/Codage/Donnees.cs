using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

public static class Donnees
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "json_lire", "json_lire",
            "Extrait une valeur d'un JSON via un chemin. Exemple : json={\"data\":{\"user\":{\"name\":\"alice\"}}}, chemin='data.user.name' -> 'alice'. Sans chemin, retourne le JSON complet en string.",
            Espace.Codage, "Donnees",
            new List<Port>
            {
                new("json", TypePort.Texte, true),
                new("chemin", TypePort.Texte, true),
            },
            new List<Port> { new("valeur", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var raw = ctx.Entree("json") ?? "";
                var chemin = ctx.Entree("chemin") ?? "";
                if (string.IsNullOrEmpty(raw))
                    return ResultatExecution.Fail("json_lire: json vide");
                try
                {
                    var node = JsonNode.Parse(raw);
                    if (string.IsNullOrEmpty(chemin)) return ResultatExecution.Ok(new() { ["valeur"] = node?.ToJsonString() ?? "" });
                    var cur = node;
                    foreach (var seg in chemin.Split('.'))
                    {
                        if (cur is JsonObject o) cur = o[seg];
                        else if (cur is JsonArray a && int.TryParse(seg, out var idx)) cur = a[idx];
                        else return ResultatExecution.Fail("json_lire: chemin '" + chemin + "' invalide a '" + seg + "'");
                        if (cur is null) return ResultatExecution.Ok(new() { ["valeur"] = "" });
                    }
                    var v = cur?.ToJsonString() ?? "";
                    if (cur is JsonValue jv) v = jv.ToJsonString().Trim('"');
                    return ResultatExecution.Ok(new() { ["valeur"] = v });
                }
                catch (Exception ex) { return ResultatExecution.Fail("json_lire: " + ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "json_ecrire", "json_ecrire",
            "Wrap une valeur en JSON selon le type demande. 'object' et 'array' parsent la valeur en JSON d'abord.",
            Espace.Codage, "Donnees",
            new List<Port> { new("valeur", TypePort.Texte, true) },
            new List<Port> { new("json", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("type", "Type", "liste", "string", new List<string> { "string", "number", "boolean", "array", "object" }),
            },
            async ctx =>
            {
                var valeur = ctx.Entree("valeur") ?? "";
                var type = ctx.Ch("type", "string");
                try
                {
                    JsonNode? node = type switch
                    {
                        "string" => JsonValue.Create(valeur),
                        "number" => JsonValue.Create(double.TryParse(valeur, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0.0),
                        "boolean" => JsonValue.Create(bool.TryParse(valeur, out var b) && b),
                        "array" => JsonValue.Create(JsonNode.Parse(valeur.Replace('\'', '"'))?.AsArray() ?? new JsonArray()),
                        "object" => JsonValue.Create(JsonNode.Parse(valeur.Replace('\'', '"'))?.AsObject() ?? new JsonObject()),
                        _ => JsonValue.Create(valeur),
                    };
                    return ResultatExecution.Ok(new() { ["json"] = node?.ToJsonString() ?? "null" });
                }
                catch (Exception ex) { return ResultatExecution.Fail("json_ecrire: " + ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "csv_parser", "csv_parser",
            "Parse un texte CSV en liste de dicts (JSON). La premiere ligne peut etre les headers (a_header=true).",
            Espace.Codage, "Donnees",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("lignes", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("separateur", "Separateur", "texte", ","),
                new("a_header", "Premiere ligne = headers", "booleen", true),
            },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? "";
                var sep = ctx.Ch("separateur", ",");
                var header = ctx.ChBool("a_header", true);
                if (sep.Length != 1)
                    return ResultatExecution.Fail("csv_parser: separateur doit etre 1 caractere, pas '" + sep + "'");
                if (string.IsNullOrEmpty(texte))
                    return ResultatExecution.Ok(new() { ["lignes"] = "[]" });
                // Split lines sans regex (string.Split supporte les char)
                var lines = texte.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                  .Where(l => l.Length > 0).ToList();
                if (lines.Count == 0) return ResultatExecution.Ok(new() { ["lignes"] = "[]" });
                string[] headers;
                int startIdx;
                if (header)
                {
                    headers = lines[0].Split(sep[0]);
                    startIdx = 1;
                }
                else
                {
                    var firstRow = lines[0].Split(sep[0]);
                    headers = firstRow.Select((_, i) => "col" + (i + 1)).ToArray();
                    startIdx = 0;
                }
                var arr = new JsonArray();
                for (int i = startIdx; i < lines.Count; i++)
                {
                    var cols = lines[i].Split(sep[0]);
                    var obj = new JsonObject();
                    for (int c = 0; c < headers.Length; c++)
                        obj[headers[c]] = c < cols.Length ? cols[c] : "";
                    arr.Add(obj);
                }
                return ResultatExecution.Ok(new() { ["lignes"] = arr.ToJsonString() });
            }
        ));
    }
}
