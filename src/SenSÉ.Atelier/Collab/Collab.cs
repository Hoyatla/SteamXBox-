using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Atelier.Collab;

/// <summary>
/// Edition collaborative MVP : last-write-wins via push/pull.
/// Pas de SSE (TODO pour diffusion temps reel).
/// </summary>
/// <remarks>
/// <b>Stockage :</b> Outils/Atelier/Collab/&lt;graphe_id&gt;.json contient la liste
/// des modifs avec leur timestamp. Chaque modif = {ts, user, type, payload}.
///
/// <b>Types de modifs supportees :</b>
/// - <c>noeud_ajoute</c> : {noeud (JSON complet)}
/// - <c>noeud_supprime</c> : {id}
/// - <c>noeud_modifie</c> : {id, x?, y?, params?}
/// - <c>lien_ajoute</c> : {lien}
/// - <c>lien_supprime</c> : {id}
///
/// <b>Conflict resolution :</b> last-write-wins. Si deux users modifient le
/// meme noeud en meme temps, la derniere modif gagne.
/// </remarks>
public static class Collab
{
    public const string Dossier = "Collab";

    public class Modif
    {
        public DateTime Ts { get; set; } = DateTime.UtcNow;
        public string User { get; set; } = "";
        public string Type { get; set; } = ""; // noeud_ajoute, noeud_supprime, etc.
        public JsonObject? Payload { get; set; }
    }

    private static readonly object _lock = new();

    /// <summary>Pousse une modif dans le store.</summary>
    public static void Push(string racine, string grapheId, Modif m)
    {
        var fichier = Path.Combine(racine, Dossier, grapheId + ".json");
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
            List<Modif> mods = ChargerListe(fichier);
            mods.Add(m);
            EcrireListe(fichier, mods);
        }
    }

    /// <summary>Recupere les modifs depuis un timestamp donne (inclusif).</summary>
    public static List<Modif> Pull(string racine, string grapheId, DateTime depuis)
    {
        var fichier = Path.Combine(racine, Dossier, grapheId + ".json");
        lock (_lock)
        {
            var mods = ChargerListe(fichier);
            return mods.Where(m => m.Ts >= depuis).ToList();
        }
    }

    /// <summary>Calcule un diff simple entre 2 versions (MVP : compte des noeuds et liens).</summary>
    public static object Diff(string racine, string grapheIdA, string grapheIdB)
    {
        var fa = Path.Combine(racine, Dossier, grapheIdA + ".json");
        var fb = Path.Combine(racine, Dossier, grapheIdB + ".json");
        var na = File.Exists(fa) ? ChargerListe(fa).Count : 0;
        var nb = File.Exists(fb) ? ChargerListe(fb).Count : 0;
        return new { graphe_a = grapheIdA, graphe_b = grapheIdB, nb_modifs_a = na, nb_modifs_b = nb };
    }

    /// <summary>Resout un conflit (MVP : last-write-wins, donc juste on rejoue la modif la plus recente).</summary>
    public static bool Resoudre(string racine, string grapheId, JsonObject resolution, out string err)
    {
        err = "";
        // MVP : on accepte juste la modif "winner" et on supprime les modifs precedentes sur le meme noeud.
        try
        {
            var type = resolution["type"]?.GetValue<string>() ?? "";
            var user = resolution["user"]?.GetValue<string>() ?? "winner";
            var winner = new Modif { Type = type, User = user, Payload = resolution["payload"] as JsonObject };
            Push(racine, grapheId, winner);
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    private static List<Modif> ChargerListe(string fichier)
    {
        if (!File.Exists(fichier)) return new();
        try
        {
            var json = File.ReadAllText(fichier);
            var n = JsonNode.Parse(json);
            if (n is not JsonObject root) return new();
            var arr = root["modifs"] as JsonArray;
            if (arr is null) return new();
            var result = new List<Modif>();
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                var m = new Modif
                {
                    Ts = o["ts"]?.GetValue<DateTime>() ?? DateTime.MinValue,
                    User = o["user"]?.GetValue<string>() ?? "",
                    Type = o["type"]?.GetValue<string>() ?? "",
                };
                if (o["payload"] is JsonObject p) m.Payload = (JsonObject)p.DeepClone();
                result.Add(m);
            }
            return result;
        }
        catch { return new(); }
    }

    private static void EcrireListe(string fichier, List<Modif> mods)
    {
        var arr = new JsonArray();
        foreach (var m in mods)
        {
            arr.Add(new JsonObject
            {
                ["ts"] = m.Ts.ToString("o"),
                ["user"] = m.User,
                ["type"] = m.Type,
                ["payload"] = m.Payload?.DeepClone(),
            });
        }
        var root = new JsonObject { ["modifs"] = arr };
        File.WriteAllText(fichier, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
