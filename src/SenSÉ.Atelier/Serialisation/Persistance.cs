using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Custom;

namespace SenSÉ.Atelier.Serialisation;

/// <summary>
/// Sauvegarde / chargement JSON des graphes et des noeuds custom.
/// </summary>
/// <remarks>
/// <para>Le repertoire racine est configurable (par defaut
/// <c>Outils\Atelier\</c> a cote de l'exe).</para>
///
/// <para>Les graphes sont stockes sous
/// <c>Graphes\&lt;espace&gt;\&lt;graphe_id&gt;.json</c> et les noeuds
/// custom sous <c>NoeudsCustom\&lt;type_id&gt;.json</c>.</para>
/// </remarks>
public sealed class Persistance
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string Racine { get; }

    public Persistance(string racine)
    {
        Racine = racine;
        Directory.CreateDirectory(Path.Combine(racine, "Graphes", "codage"));
        Directory.CreateDirectory(Path.Combine(racine, "Graphes", "multimedia"));
        Directory.CreateDirectory(Path.Combine(racine, "NoeudsCustom"));
    }

    // ============== GRAPHES ==============

    public string SauvegarderGraphe(Graphe g)
    {
        g.ModifieLe = DateTime.UtcNow;
        var dir = Path.Combine(Racine, "Graphes", g.Espace.Id());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, g.Id + ".json");
        var json = JsonSerializer.Serialize(ToDto(g), Opt);
        File.WriteAllText(path, json);
        return g.Id;
    }

    public Graphe? ChargerGraphe(string id, Espace? espace = null)
    {
        // Si on connait l'espace, on cherche directement. Sinon on tente les deux.
        if (espace is not null)
        {
            var p = Path.Combine(Racine, "Graphes", espace.Value.Id(), id + ".json");
            if (File.Exists(p)) return FromDto(JsonNode.Parse(File.ReadAllText(p))!.AsObject());
        }
        foreach (var esp in new[] { Espace.Codage, Espace.Multimedia })
        {
            var p = Path.Combine(Racine, "Graphes", esp.Id(), id + ".json");
            if (File.Exists(p)) return FromDto(JsonNode.Parse(File.ReadAllText(p))!.AsObject());
        }
        return null;
    }

    public List<(string id, Espace espace, string nom, DateTime modifieLe)> ListerGraphes()
    {
        var liste = new List<(string, Espace, string, DateTime)>();
        foreach (var esp in new[] { Espace.Codage, Espace.Multimedia })
        {
            var dir = Path.Combine(Racine, "Graphes", esp.Id());
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var n = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                    var g = FromDto(n);
                    liste.Add((g.Id, g.Espace, g.Nom, g.ModifieLe));
                }
                catch { /* ignorer les fichiers corrompus */ }
            }
        }
        return liste;
    }

    public void SupprimerGraphe(string id, Espace espace)
    {
        var p = Path.Combine(Racine, "Graphes", espace.Id(), id + ".json");
        if (File.Exists(p)) File.Delete(p);
    }

    public string ExporterGraphe(Graphe g, string chemin)
    {
        var json = JsonSerializer.Serialize(ToDto(g), Opt);
        File.WriteAllText(chemin, json);
        return chemin;
    }

    public Graphe? ImporterGraphe(string chemin)
    {
        if (!File.Exists(chemin)) return null;
        var n = JsonNode.Parse(File.ReadAllText(chemin))!.AsObject();
        var g = FromDto(n);
        g.Id = Guid.NewGuid().ToString("N"); // nouveau id a l'import
        return g;
    }

    // ============== NOEUDS CUSTOM ==============

    public string SauvegarderCustom(NoeudCustom nc)
    {
        var path = Path.Combine(Racine, "NoeudsCustom", nc.Id + ".json");
        var json = JsonSerializer.Serialize(nc, Opt);
        File.WriteAllText(path, json);
        return nc.Id;
    }

    public NoeudCustom? ChargerCustom(string id)
    {
        var p = Path.Combine(Racine, "NoeudsCustom", id + ".json");
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<NoeudCustom>(File.ReadAllText(p), Opt);
    }

    public List<NoeudCustom> ListerCustom()
    {
        var liste = new List<NoeudCustom>();
        var dir = Path.Combine(Racine, "NoeudsCustom");
        if (!Directory.Exists(dir)) return liste;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var nc = JsonSerializer.Deserialize<NoeudCustom>(File.ReadAllText(f), Opt);
                if (nc is not null) liste.Add(nc);
            }
            catch { }
        }
        return liste;
    }

    public void SupprimerCustom(string id)
    {
        var p = Path.Combine(Racine, "NoeudsCustom", id + ".json");
        if (File.Exists(p)) File.Delete(p);
    }


    // ============== SERIALISATION INLINE (pour Historique undo/redo) ==============

    /// <summary>Serialise un graphe en JSON (DTO interne).</summary>
    public static string ToJson(Graphe g)
        => JsonSerializer.Serialize(ToDto(g), Opt);

    /// <summary>Restaure l'etat d'un graphe existant depuis JSON (preserve l'instance).</summary>
    public static void FromJson(Graphe g, string json)
    {
        var n = JsonNode.Parse(json)!.AsObject();
        var src = FromDto(n);
        g.Id = src.Id;
        g.Nom = src.Nom;
        g.Espace = src.Espace;
        g.ModifieLe = src.ModifieLe;
        g.Noeuds.Clear();
        g.Noeuds.AddRange(src.Noeuds);
        g.Liens.Clear();
        g.Liens.AddRange(src.Liens);
    }
    // ============== DTO ==============

    private static object ToDto(Graphe g) => new
    {
        v = 1,
        id = g.Id,
        espace = g.Espace.Id(),
        nom = g.Nom,
        modifieLe = g.ModifieLe,
        noeuds = g.Noeuds.Select(n => new
        {
            id = n.Id,
            type = n.Type,
            x = n.X,
            y = n.Y,
            params_ = n.Params,
            portsEntree = n.PortsEntree.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
            portsSortie = n.PortsSortie.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
        }),
        liens = g.Liens.Select(l => new
        {
            id = l.Id,
            noeudSourceId = l.NoeudSourceId,
            portSourceNom = l.PortSourceNom,
            noeudCibleId = l.NoeudCibleId,
            portCibleNom = l.PortCibleNom,
        }),
    };

    private static Graphe FromDto(JsonObject o)
    {
        var g = new Graphe
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Nom = o["nom"]?.GetValue<string>() ?? "Sans nom",
        };
        var esp = o["espace"]?.GetValue<string>() ?? "codage";
        EspaceExtensions.TryParse(esp, out var espVal); g.Espace = espVal;
        if (o["modifieLe"] is JsonValue v && DateTime.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dtmp2)) g.ModifieLe = dtmp2;
        if (o["noeuds"] is JsonArray nds)
        {
            foreach (var n in nds)
            {
                if (n is not JsonObject no) continue;
                var node = new Noeud
                {
                    Id = no["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Type = no["type"]?.GetValue<string>() ?? "",
                    X = no["x"]?.GetValue<double>() ?? 0,
                    Y = no["y"]?.GetValue<double>() ?? 0,
                };
                if (no["params_"] is JsonObject pp)
                    foreach (var kv in pp) node.Params[kv.Key] = kv.Value;
                if (no["portsEntree"] is JsonArray pe)
                    foreach (var p in pe)
                    {
                        if (p is not JsonObject po) continue;
                        node.PortsEntree.Add(new Port(
                            po["nom"]?.GetValue<string>() ?? "",
                            Enum.TryParse<TypePort>(po["type"]?.GetValue<string>(), out var t1) ? t1 : TypePort.Texte,
                            true));
                    }
                if (no["portsSortie"] is JsonArray ps)
                    foreach (var p in ps)
                    {
                        if (p is not JsonObject po) continue;
                        node.PortsSortie.Add(new Port(
                            po["nom"]?.GetValue<string>() ?? "",
                            Enum.TryParse<TypePort>(po["type"]?.GetValue<string>(), out var t2) ? t2 : TypePort.Texte,
                            false));
                    }
                g.Noeuds.Add(node);
            }
        }
        if (o["liens"] is JsonArray ls)
        {
            foreach (var l in ls)
            {
                if (l is not JsonObject lo) continue;
                g.Liens.Add(new Lien
                {
                    Id = lo["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    NoeudSourceId = lo["noeudSourceId"]?.GetValue<string>() ?? "",
                    PortSourceNom = lo["portSourceNom"]?.GetValue<string>() ?? "",
                    NoeudCibleId = lo["noeudCibleId"]?.GetValue<string>() ?? "",
                    PortCibleNom = lo["portCibleNom"]?.GetValue<string>() ?? "",
                });
            }
        }
        return g;
    }
}