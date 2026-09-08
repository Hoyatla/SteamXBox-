using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Templates;

/// <summary>
/// Marketplace de templates : graphe prets a l'emploi.
/// Stockes dans Outils/Atelier/Templates/&lt;id&gt;.json + index.json.
/// Au boot, si vide, telecharge depuis l'URL de defaut.
/// En mode degrade (offline), un template minimal est cree.
/// </summary>
public static class Templates
{
    public const string Dossier = "Templates";
    public const string IndexFichier = "index.json";
    public const string UrlDefaut = "https://raw.githubusercontent.com/Hoyatla/SenS-/main/Templates/index.json";

    /// <summary>Initialise le dossier Templates. Si vide, essaie de telecharger, sinon cree un template en dur.</summary>
    public static void Initialiser(string racine)
    {
        var dir = Path.Combine(racine, Dossier);
        Directory.CreateDirectory(dir);
        var indexFile = Path.Combine(dir, IndexFichier);
        if (File.Exists(indexFile)) return;

        // Mode degrade : creer 1 template en dur (Hello World Python)
        try
        {
            var hello = CreerTemplateHelloWorld();
            File.WriteAllText(Path.Combine(dir, "hello-world.json"), JsonSerializer.Serialize(hello, new JsonSerializerOptions { WriteIndented = true }));
            var index = new JsonObject
            {
                ["version"] = 1,
                ["templates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "hello-world",
                        ["nom"] = "Hello World Python",
                        ["description"] = "Un seul noeud executer_code qui affiche 'Hello, World!' en Python.",
                        ["auteur"] = "SenSÉ",
                        ["tags"] = new JsonArray { "python", "demo" },
                        ["fichier"] = "hello-world.json",
                    }
                }
            };
            File.WriteAllText(indexFile, index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* mode degrade silencieux */ }
    }

    public record TemplateInfo(string Id, string Nom, string Description, string Auteur, List<string> Tags, string Fichier);

    /// <summary>Lit l'index.json et retourne la liste des templates.</summary>
    public static List<TemplateInfo> Lister(string racine)
    {
        var indexFile = Path.Combine(racine, Dossier, IndexFichier);
        if (!File.Exists(indexFile)) return new();
        try
        {
            var n = JsonNode.Parse(File.ReadAllText(indexFile));
            if (n is not JsonObject root) return new();
            var arr = root["templates"] as JsonArray;
            if (arr is null) return new();
            var result = new List<TemplateInfo>();
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                var tags = new List<string>();
                if (o["tags"] is JsonArray ta) foreach (var t in ta) if (t is JsonValue tv) tags.Add(tv.GetValue<string>() ?? "");
                result.Add(new TemplateInfo(
                    o["id"]?.GetValue<string>() ?? "",
                    o["nom"]?.GetValue<string>() ?? "",
                    o["description"]?.GetValue<string>() ?? "",
                    o["auteur"]?.GetValue<string>() ?? "",
                    tags,
                    o["fichier"]?.GetValue<string>() ?? ""
                ));
            }
            return result;
        }
        catch { return new(); }
    }

    /// <summary>Charge un template et le retourne comme Graphe (nouvel ID).</summary>
    public static Graphe? Charger(string racine, string templateId)
    {
        var templates = Lister(racine);
        var t = templates.FirstOrDefault(x => x.Id == templateId);
        if (t is null) return null;
        var fichier = Path.Combine(racine, Dossier, t.Fichier);
        if (!File.Exists(fichier)) return null;
        try
        {
            var n = JsonNode.Parse(File.ReadAllText(fichier));
            if (n is not JsonObject o) return null;
            return LireGraphe(o);
        }
        catch { return null; }
    }

    /// <summary>Publie un graphe comme template. Le graphe est copie (nouvel ID) et l'index est mis a jour.</summary>
    public static bool Publier(string racine, Graphe g, string nom, string description, List<string> tags, out string err)
    {
        err = "";
        try
        {
            var dir = Path.Combine(racine, Dossier);
            Directory.CreateDirectory(dir);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var fichier = id + ".json";
            // Copie : nouvel ID, sans Groupes niCommentaires (pour rester minimal)
            var copie = new Graphe
            {
                Id = id,
                Espace = g.Espace,
                Nom = nom,
                Noeuds = g.Noeuds.Select(n => new Noeud
                {
                    Id = n.Id,
                    Type = n.Type,
                    X = n.X,
                    Y = n.Y,
                    Params = n.Params,
                    PortsEntree = n.PortsEntree,
                    PortsSortie = n.PortsSortie,
                }).ToList(),
                Liens = g.Liens.ToList(),
            };
            var obj = EcrireGraphe(copie);
            File.WriteAllText(Path.Combine(dir, fichier), obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // Mettre a jour l'index
            var indexFile = Path.Combine(dir, IndexFichier);
            JsonArray arr;
            if (File.Exists(indexFile))
            {
                var n = JsonNode.Parse(File.ReadAllText(indexFile));
                arr = n?["templates"] as JsonArray ?? new JsonArray();
            }
            else arr = new JsonArray();
            var tagsArr = new JsonArray();
            foreach (var t in tags) tagsArr.Add(t);
            arr.Add(new JsonObject
            {
                ["id"] = id,
                ["nom"] = nom,
                ["description"] = description,
                ["auteur"] = "moi",
                ["tags"] = tagsArr,
                ["fichier"] = fichier,
            });
            var root = new JsonObject { ["version"] = 1, ["templates"] = arr };
            File.WriteAllText(indexFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    private static Graphe CreerTemplateHelloWorld()
    {
        var g = new Graphe { Id = "hello-world", Espace = Espace.Codage, Nom = "Hello World Python" };
        var n = new Noeud
        {
            Id = "n1",
            Type = "executer_code",
            X = 100, Y = 100,
            Params = new Dictionary<string, object?>
            {
                ["langage"] = "python",
                ["code"] = "print('Hello, World!')",
                ["limite_s"] = 5,
            },
            PortsEntree = new List<Port>(),
            PortsSortie = new List<Port>
            {
                new("stdout", TypePort.Texte, false),
                new("code_retour", TypePort.Nombre, false),
            },
        };
        g.Noeuds.Add(n);
        return g;
    }

    // === Sérialisation Graphe <-> JsonObject (semblable a Persistance mais standalone) ===

    private static JsonObject EcrireGraphe(Graphe g)
    {
        var noeuds = new JsonArray();
        foreach (var n in g.Noeuds)
        {
            var p = new JsonObject();
            foreach (var kv in n.Params) p[kv.Key] = kv.Value is JsonNode jn ? jn.DeepClone() : JsonValue.Create(kv.Value?.ToString() ?? "");
            noeuds.Add(new JsonObject
            {
                ["id"] = n.Id, ["type"] = n.Type, ["x"] = n.X, ["y"] = n.Y, ["params"] = p,
            });
        }
        var liens = new JsonArray();
        foreach (var l in g.Liens) liens.Add(new JsonObject
        {
            ["id"] = l.Id, ["noeud_source_id"] = l.NoeudSourceId, ["port_source_nom"] = l.PortSourceNom,
            ["noeud_cible_id"] = l.NoeudCibleId, ["port_cible_nom"] = l.PortCibleNom,
        });
        return new JsonObject
        {
            ["id"] = g.Id,
            ["espace"] = g.Espace.Id(),
            ["nom"] = g.Nom,
            ["modifie_le"] = g.ModifieLe.ToString("o"),
            ["noeuds"] = noeuds,
            ["liens"] = liens,
        };
    }

    private static Graphe LireGraphe(JsonObject o)
    {
        var g = new Graphe
        {
            Id = Guid.NewGuid().ToString("N"), // nouvel ID pour eviter collision
            Nom = o["nom"]?.GetValue<string>() ?? "Template",
        };
        var espaceStr = o["espace"]?.GetValue<string>() ?? "codage";
        SenSÉ.Atelier.Modele.EspaceExtensions.TryParse(espaceStr, out var espace);
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
            if (nj["params"] is JsonObject pp)
                foreach (var kv in pp) noeud.Params[kv.Key] = kv.Value?.DeepClone();
            g.Noeuds.Add(noeud);
        }
        foreach (var l in o["liens"] as JsonArray ?? new JsonArray())
        {
            if (l is not JsonObject lj) continue;
            g.Liens.Add(new Lien
            {
                Id = Guid.NewGuid().ToString("N"),
                NoeudSourceId = lj["noeud_source_id"]?.GetValue<string>() ?? "",
                PortSourceNom = lj["port_source_nom"]?.GetValue<string>() ?? "",
                NoeudCibleId = lj["noeud_cible_id"]?.GetValue<string>() ?? "",
                PortCibleNom = lj["port_cible_nom"]?.GetValue<string>() ?? "",
            });
        }
        return g;
    }
}
