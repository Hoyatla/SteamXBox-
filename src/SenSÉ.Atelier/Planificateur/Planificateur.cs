using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Planificateur;

/// <summary>
/// Planificateur cron-like : pour chaque tache (graphe_id, expression cron),
/// verifie toutes les minutes si l'expression matche l'heure actuelle, et
/// declenche le graphe via Moteur.Instance.LancerAsync.
/// </summary>
/// <remarks>
/// <b>Format cron supporte (MVP) :</b>
/// <code>
///   minute    : 0-59, * , N, N-M, N,M,..., */N
///   heure     : 0-23, * , N, N-M, */N
///   jour-mois : 1-31, * , N
///   mois      : 1-12, * , N
///   jour-sem  : 0-6 (0=dimanche), * , N
/// </code>
/// La comparaison est faite a la minute. Le 1er tick peut etre
/// immediat ou a la minute suivante (leger alea au demarrage).
///
/// <para><b>Persistance :</b> Outils/Atelier/Planificateur/taches.json.
/// Format : {taches: [{graphe_id, cron, dernierdeclenchement: "..."}]}.</para>
/// </remarks>
public sealed class Planificateur : IDisposable
{
    private static Planificateur? _instance;
    public static Planificateur Instance => _instance ??= new Planificateur();

    private readonly string _racine;
    private readonly Persistance _persistance;
    private readonly string _cheminFichier;
    private readonly Dictionary<string, Tache> _taches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private Timer? _timer;
    private DateTime _dernierTickMinute = DateTime.MinValue;

    public class Tache
    {
        public string GrapheId { get; set; } = "";
        public string Cron { get; set; } = "";
        public DateTime? DernierDeclenchement { get; set; }
    }

    private Planificateur()
    {
        _racine = AppContext.BaseDirectory;
        _persistance = new Persistance(_racine);
        var dir = Path.Combine(_racine, "Planificateur");
        Directory.CreateDirectory(dir);
        _cheminFichier = Path.Combine(dir, "taches.json");
    }

    /// <summary>Demarre le timer (verifie toutes les 30 secondes, ne declenche qu'une fois par minute).</summary>
    public void Demarrer()
    {
        Charger();
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
    }

    public void Arreter()
    {
        _timer?.Dispose();
        _timer = null;
        Sauvegarder();
    }

    public void Dispose() => Arreter();

    public bool Ajouter(string grapheId, string cron, out DateTime prochain, out string err)
    {
        err = ""; prochain = DateTime.MinValue;
        prochain = DateTime.MinValue;
        if (string.IsNullOrEmpty(grapheId)) { err = "grapheId vide"; return false; }
        if (string.IsNullOrEmpty(cron)) { err = "cron vide"; return false; }
        if (!CronMatch(cron, DateTime.Now, out prochain)) { err = "cron invalide ou jamais declenchable"; return false; }
        lock (_lock)
        {
            _taches[grapheId] = new Tache { GrapheId = grapheId, Cron = cron };
            Sauvegarder();
        }
        return true;
    }

    public bool Retirer(string grapheId)
    {
        lock (_lock)
        {
            var ok = _taches.Remove(grapheId);
            if (ok) Sauvegarder();
            return ok;
        }
    }

    public List<Tache> Lister()
    {
        lock (_lock) return _taches.Values.ToList();
    }

    private void Tick()
    {
        try
        {
            var now = DateTime.Now;
            // Evite de declencher 2x dans la meme minute
            if (now.Year == _dernierTickMinute.Year && now.Month == _dernierTickMinute.Month
                && now.Day == _dernierTickMinute.Day && now.Hour == _dernierTickMinute.Hour
                && now.Minute == _dernierTickMinute.Minute)
                return;
            _dernierTickMinute = now;
            List<Tache> aExecuter;
            lock (_lock)
            {
                aExecuter = _taches.Values.Where(t =>
                    (t.DernierDeclenchement is null
                        || t.DernierDeclenchement.Value.Year != now.Year
                        || t.DernierDeclenchement.Value.Month != now.Month
                        || t.DernierDeclenchement.Value.Day != now.Day
                        || t.DernierDeclenchement.Value.Hour != now.Hour
                        || t.DernierDeclenchement.Value.Minute != now.Minute)
                    && CronMatch(t.Cron, now, out _)
                ).ToList();
            }
            foreach (var t in aExecuter) Declencher(t);
        }
        catch (Exception)
        {
            // Pas de crash sur erreur de tick
        }
    }

    private void Declencher(Tache t)
    {
        try
        {
            var g = _persistance.ChargerGraphe(t.GrapheId);
            if (g is null) return;
            Moteur.Instance.LancerAsync(g);
            lock (_lock) t.DernierDeclenchement = DateTime.Now;
            Sauvegarder();
        }
        catch { /* ne pas casser le timer */ }
    }

    private void Charger()
    {
        try
        {
            if (!File.Exists(_cheminFichier)) return;
            var json = File.ReadAllText(_cheminFichier);
            var n = JsonNode.Parse(json);
            if (n is not JsonObject root) return;
            var arr = root["taches"] as JsonArray;
            if (arr is null) return;
            lock (_lock)
            {
                _taches.Clear();
                foreach (var item in arr)
                {
                    if (item is not JsonObject jo) continue;
                    var t = new Tache
                    {
                        GrapheId = jo["graphe_id"]?.GetValue<string>() ?? "",
                        Cron = jo["cron"]?.GetValue<string>() ?? "",
                    };
                    if (jo["dernierdeclenchement"] is JsonValue jv && jv.TryGetValue<DateTime>(out var d))
                        t.DernierDeclenchement = d;
                    if (!string.IsNullOrEmpty(t.GrapheId)) _taches[t.GrapheId] = t;
                }
            }
        }
        catch { _taches.Clear(); }
    }

    private void Sauvegarder()
    {
        try
        {
            var arr = new JsonArray();
            List<Tache> snapshot;
            lock (_lock) snapshot = _taches.Values.ToList();
            foreach (var t in snapshot)
            {
                var jo = new JsonObject
                {
                    ["graphe_id"] = t.GrapheId,
                    ["cron"] = t.Cron,
                };
                if (t.DernierDeclenchement.HasValue) jo["dernierdeclenchement"] = JsonValue.Create(t.DernierDeclenchement.Value);
                arr.Add(jo);
            }
            var root = new JsonObject { ["taches"] = arr };
            var tmp = _cheminFichier + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_cheminFichier)) File.Delete(_cheminFichier);
            File.Move(tmp, _cheminFichier);
        }
        catch { /* pas de crash */ }
    }

    /// <summary>Verifie si l'expression cron matche l'heure donnee.
    /// Renvoie true + la prochaine date ou true (impossible d'evaluer la prochaine si * * * * *).</summary>
    public static bool CronMatch(string cron, DateTime t, out DateTime prochain)
    {
        prochain = t;
        if (string.IsNullOrWhiteSpace(cron)) return false;
        var parties = cron.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parties.Length != 5) return false;
        try
        {
            if (!ChampMatch(parties[0], t.Minute, 0, 59)) return false;
            if (!ChampMatch(parties[1], t.Hour, 0, 23)) return false;
            if (!ChampMatch(parties[2], t.Day, 1, 31)) return false;
            if (!ChampMatch(parties[3], t.Month, 1, 12)) return false;
            // jour-semaine : 0=dimanche. DayOfWeek : Sunday=0.
            if (!ChampMatch(parties[4], (int)t.DayOfWeek, 0, 6)) return false;
            prochain = t;
            return true;
        }
        catch { return false; }
    }

    private static bool ChampMatch(string spec, int valeur, int min, int max)
    {
        if (spec == "*") return true;
        // liste separee par des virgules
        foreach (var partie in spec.Split(','))
        {
            if (ChampMatchUne(partie.Trim(), valeur, min, max)) return true;
        }
        return false;
    }

    private static bool ChampMatchUne(string s, int valeur, int min, int max)
    {
        if (s == "*") return true;
        if (s.StartsWith("*/"))
        {
            if (int.TryParse(s.Substring(2), out var pas) && pas > 0)
                return ((valeur - min) % pas) == 0;
            return false;
        }
        if (s.Contains('-'))
        {
            var bornes = s.Split('-');
            if (bornes.Length == 2
                && int.TryParse(bornes[0], out var a)
                && int.TryParse(bornes[1], out var b)
                && a <= b && a >= min && b <= max)
                return valeur >= a && valeur <= b;
            return false;
        }
        if (int.TryParse(s, out var n) && n >= min && n <= max)
            return valeur == n;
        return false;
    }
}
