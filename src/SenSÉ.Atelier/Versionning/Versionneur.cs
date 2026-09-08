using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Versionning;

/// <summary>
/// Versioning git-like : chaque appel a <see cref="Versionner"/> cree un
/// snapshot complet du graphe dans Outils/Atelier/Versionning/&lt;graphe_id&gt;/&lt;version_id&gt;.json.
/// </summary>
public sealed class Versionneur
{
    private readonly string _racine;
    private readonly Persistance _persistance;
    private readonly string _racineVersionning;

    /// <summary>Acces a la persistance utilisee (pour Version.cs et autres).</summary>
    public Persistance Persistance => _persistance;

    public Versionneur(string racine, Persistance persistance)
    {
        _racine = racine;
        _persistance = persistance;
        _racineVersionning = Path.Combine(racine, "Versionning");
        Directory.CreateDirectory(_racineVersionning);
    }

    /// <summary>Cree une version du graphe courant. Renvoie l'ID de la version.</summary>
    public string Versionner(string grapheId, string message = "Auto-save")
    {
        var g = _persistance.ChargerGraphe(grapheId);
        if (g is null) throw new InvalidOperationException($"graphe introuvable : {grapheId}");
        var versionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var dir = Path.Combine(_racineVersionning, grapheId);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, versionId + ".json");
        var snap = new JsonObject
        {
            ["version_id"] = versionId,
            ["graphe_id"] = grapheId,
            ["created_at"] = DateTime.UtcNow.ToString("o"),
            ["message"] = message ?? "",
            ["graphe"] = SerialiserGraphe(g),
        };
        File.WriteAllText(file, snap.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return versionId;
    }

    public record VersionInfo(string VersionId, string Message, DateTime CreatedAt, int NbNoeuds, int NbLiens);

    public List<VersionInfo> Lister(string grapheId)
    {
        var dir = Path.Combine(_racineVersionning, grapheId);
        if (!Directory.Exists(dir)) return new();
        var result = new List<VersionInfo>();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var n = JsonNode.Parse(File.ReadAllText(f));
                if (n is not JsonObject o) continue;
                var versionId = o["version_id"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(f);
                var message = o["message"]?.GetValue<string>() ?? "";
                var createdAt = o["created_at"]?.GetValue<DateTime>() ?? DateTime.MinValue;
                var grapheNode = o["graphe"] as JsonObject;
                var noeuds = (grapheNode?["noeuds"] as JsonArray)?.Count ?? 0;
                var liens = (grapheNode?["liens"] as JsonArray)?.Count ?? 0;
                result.Add(new VersionInfo(versionId, message, createdAt, noeuds, liens));
            }
            catch { }
        }
        return result.OrderByDescending(v => v.CreatedAt).ToList();
    }

    public bool Restaurer(string grapheId, string versionId, out string err)
    {
        err = "";
        var f = Path.Combine(_racineVersionning, grapheId, versionId + ".json");
        if (!File.Exists(f)) { err = $"version introuvable : {versionId}"; return false; }
        try
        {
            var n = JsonNode.Parse(File.ReadAllText(f));
            if (n is not JsonObject o) { err = "snapshot invalide"; return false; }
            var grapheNode = o["graphe"] as JsonObject;
            if (grapheNode is null) { err = "snapshot incomplet (pas de graphe)"; return false; }
            var g = DeserialiserGraphe(grapheNode);
            _persistance.SauvegarderGraphe(g);
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    public object Comparer(string grapheId, string versionA, string versionB)
    {
        var fa = Path.Combine(_racineVersionning, grapheId, versionA + ".json");
        var fb = Path.Combine(_racineVersionning, grapheId, versionB + ".json");
        if (!File.Exists(fa) || !File.Exists(fb))
            return new { ok = false, error = "version(s) introuvable(s)" };
        try
        {
            var a = ChargerGrapheVersion(fa);
            var b = ChargerGrapheVersion(fb);
            if (a is null || b is null) return new { ok = false, error = "snapshot invalide" };
            var idsA = a.Noeuds.Select(n => n.Id).ToHashSet();
            var idsB = b.Noeuds.Select(n => n.Id).ToHashSet();
            var ajoutes = idsB.Except(idsA).ToList();
            var retires = idsA.Except(idsB).ToList();
            return new { ok = true, data = new
            {
                version_a = versionA,
                version_b = versionB,
                noeuds_ajoutes = ajoutes.Count,
                noeuds_retires = retires.Count,
                ids_ajoutes = ajoutes,
                ids_retires = retires,
            } };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    // === Helpers de serialisation ===

    private static JsonObject SerialiserGraphe(Graphe g)
    {
        var noeuds = new JsonArray();
        foreach (var n in g.Noeuds)
        {
            var nj = new JsonObject
            {
                ["id"] = n.Id,
                ["type"] = n.Type,
                ["x"] = n.X,
                ["y"] = n.Y,
                ["params"] = ParamsToJson(n.Params),
                ["ports_entree"] = PortsToJson(n.PortsEntree),
                ["ports_sortie"] = PortsToJson(n.PortsSortie),
            };
            noeuds.Add(nj);
        }
        var liens = new JsonArray();
        foreach (var l in g.Liens)
        {
            liens.Add(new JsonObject
            {
                ["id"] = l.Id,
                ["noeud_source_id"] = l.NoeudSourceId,
                ["port_source_nom"] = l.PortSourceNom,
                ["noeud_cible_id"] = l.NoeudCibleId,
                ["port_cible_nom"] = l.PortCibleNom,
            });
        }
        var groupes = new JsonArray();
        foreach (var grp in g.Groupes)
            groupes.Add(new JsonObject
            {
                ["id"] = grp.Id,
                ["couleur"] = grp.Couleur,
                ["label"] = grp.Label,
                ["noeud_ids"] = new JsonArray(grp.NoeudIds.Select(id => JsonValue.Create(id)).ToArray()),
            });
        var commentaires = new JsonArray();
        foreach (var c in g.Commentaires)
            commentaires.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["texte"] = c.Texte,
                ["x"] = c.X,
                ["y"] = c.Y,
                ["taille"] = c.Taille,
                ["couleur"] = c.Couleur,
            });
        return new JsonObject
        {
            ["id"] = g.Id,
            ["espace"] = g.Espace.Id(),
            ["nom"] = g.Nom,
            ["modifie_le"] = g.ModifieLe.ToString("o"),
            ["noeuds"] = noeuds,
            ["liens"] = liens,
            ["groupes"] = groupes,
            ["commentaires"] = commentaires,
        };
    }

    private static Graphe DeserialiserGraphe(JsonObject o)
    {
        var g = new Graphe
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Nom = o["nom"]?.GetValue<string>() ?? "Sans nom",
        };
        var espaceStr = o["espace"]?.GetValue<string>() ?? "codage";
        EspaceExtensions.TryParse(espaceStr, out var espace);
        g.Espace = espace;
        if (o["modifie_le"] is JsonValue jv && jv.TryGetValue<DateTime>(out var d)) g.ModifieLe = d;
        foreach (var n in o["noeuds"] as JsonArray ?? new JsonArray())
        {
            if (n is not JsonObject nj) continue;
            var noeud = new Noeud
            {
                Id = nj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                Type = nj["type"]?.GetValue<string>() ?? "",
                X = nj["x"]?.GetValue<double>() ?? 0,
                Y = nj["y"]?.GetValue<double>() ?? 0,
            };
            foreach (var kv in (nj["params"] as JsonObject) ?? new JsonObject())
                noeud.Params[kv.Key] = kv.Value?.DeepClone();
            foreach (var p in nj["ports_entree"] as JsonArray ?? new JsonArray())
                if (p is JsonObject po) noeud.PortsEntree.Add(PortFromJson(po));
            foreach (var p in nj["ports_sortie"] as JsonArray ?? new JsonArray())
                if (p is JsonObject po) noeud.PortsSortie.Add(PortFromJson(po));
            g.Noeuds.Add(noeud);
        }
        foreach (var l in o["liens"] as JsonArray ?? new JsonArray())
        {
            if (l is not JsonObject lj) continue;
            g.Liens.Add(new Lien
            {
                Id = lj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                NoeudSourceId = lj["noeud_source_id"]?.GetValue<string>() ?? "",
                PortSourceNom = lj["port_source_nom"]?.GetValue<string>() ?? "",
                NoeudCibleId = lj["noeud_cible_id"]?.GetValue<string>() ?? "",
                PortCibleNom = lj["port_cible_nom"]?.GetValue<string>() ?? "",
            });
        }
        foreach (var grp in o["groupes"] as JsonArray ?? new JsonArray())
        {
            if (grp is not JsonObject gj) continue;
            var ng = new Groupe
            {
                Id = gj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                Couleur = gj["couleur"]?.GetValue<string>() ?? "blue",
                Label = gj["label"]?.GetValue<string>(),
            };
            foreach (var id in gj["noeud_ids"] as JsonArray ?? new JsonArray())
                if (id is JsonValue iv) ng.NoeudIds.Add(iv.GetValue<string>() ?? "");
            g.Groupes.Add(ng);
        }
        foreach (var c in o["commentaires"] as JsonArray ?? new JsonArray())
        {
            if (c is not JsonObject cj) continue;
            g.Commentaires.Add(new Commentaire
            {
                Id = cj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                Texte = cj["texte"]?.GetValue<string>() ?? "",
                X = cj["x"]?.GetValue<double>() ?? 0,
                Y = cj["y"]?.GetValue<double>() ?? 0,
                Taille = cj["taille"]?.GetValue<int>() ?? 12,
                Couleur = cj["couleur"]?.GetValue<string>() ?? "#F5E10B",
            });
        }
        return g;
    }

    private Graphe? ChargerGrapheVersion(string fichier)
    {
        var n = JsonNode.Parse(File.ReadAllText(fichier));
        if (n is not JsonObject o) return null;
        var g = o["graphe"] as JsonObject;
        return g is null ? null : DeserialiserGraphe(g);
    }

    private static JsonObject ParamsToJson(Dictionary<string, object?> p)
    {
        var jo = new JsonObject();
        foreach (var kv in p) jo[kv.Key] = kv.Value switch
        {
            null => null,
            JsonNode jn => jn.DeepClone(),
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            double d => JsonValue.Create(d),
            int i => JsonValue.Create(i),
            _ => JsonValue.Create(kv.Value?.ToString() ?? ""),
        };
        return jo;
    }

    private static JsonArray PortsToJson(IReadOnlyList<Port> ports)
    {
        var arr = new JsonArray();
        foreach (var p in ports) arr.Add(new JsonObject
        {
            ["nom"] = p.Nom,
            ["type"] = p.Type.ToString(),
        });
        return arr;
    }

    private static Port PortFromJson(JsonObject po)
    {
        var nom = po["nom"]?.GetValue<string>() ?? "";
        var typeStr = po["type"]?.GetValue<string>() ?? "Texte";
        Enum.TryParse<TypePort>(typeStr, out var type);
        return new Port(nom, type, false);
    }
}
