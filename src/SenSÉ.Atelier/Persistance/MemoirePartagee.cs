using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Atelier.Memoire;

namespace SenSÉ.Atelier.Memoire;

/// <summary>
/// Memoire partagee entre tous les graphes et toutes les executions d'un
/// meme Atelier. Singleton. Thread-safe. Persistee sur disque (JSON).
/// </summary>
/// <remarks>
/// <b>Difference avec <see cref="Variables"/>.</b> Les variables sont
/// scopees au meme process et perdues au reboot. La memoire partagee est
/// serialisee dans Outils/Atelier/Memoire/partagee.json, donc elle survit
/// aux redemarrages de l'Atelier.
///
/// <para><b>Format JSON.</b> Le fichier est un objet {cle: valeur}. Les
/// valeurs sont stockees comme leur representation JSON (string, number,
/// boolean, object, array). Pour stocker un objet C# complexe, on serialize
/// d'abord en JSON puis on stocke la string.
/// </para>
/// </remarks>
public sealed class MemoirePartagee
{
    private static MemoirePartagee? _instance;
    public static MemoirePartagee Instance => _instance ??= new MemoirePartagee();

    private readonly Dictionary<string, object?> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private string? _cheminFichier;

    /// <summary>Initialise le singleton avec un chemin de fichier. Doit etre
    /// appele une fois au demarrage de l'Atelier.</summary>
    public void Initialiser(string racine)
    {
        var dir = Path.Combine(racine, "Memoire");
        Directory.CreateDirectory(dir);
        _cheminFichier = Path.Combine(dir, "partagee.json");
        Charger();
    }

    public void Set(string cle, object? valeur)
    {
        if (string.IsNullOrEmpty(cle)) throw new ArgumentException("cle vide", nameof(cle));
        lock (_lock)
        {
            _store[cle] = Normaliser(valeur);
            Sauvegarder();
        }
    }

    /// <summary>Renvoie la valeur comme string (sérialisée si objet).
    /// Renvoie defaut si la cle n'existe pas.</summary>
    public string Get(string cle, string defaut = "")
    {
        if (string.IsNullOrEmpty(cle)) return defaut;
        lock (_lock)
        {
            if (!_store.TryGetValue(cle, out var v) || v is null) return defaut;
            return v switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonNode n => n.ToJsonString(),
                _ => JsonSerializer.Serialize(v),
            };
        }
    }

    /// <summary>Renvoie la cle existe-t-elle (sans tenir compte de la valeur).</summary>
    public bool Existe(string cle)
    {
        if (string.IsNullOrEmpty(cle)) return false;
        lock (_lock) return _store.ContainsKey(cle);
    }

    public bool Delete(string cle)
    {
        if (string.IsNullOrEmpty(cle)) return false;
        lock (_lock)
        {
            var ok = _store.Remove(cle);
            if (ok) Sauvegarder();
            return ok;
        }
    }

    /// <summary>Renvoie la liste des cles. Snapshot sous lock.</summary>
    public List<string> Lister()
    {
        lock (_lock) return _store.Keys.ToList();
    }

    /// <summary>Renvoie le nombre de cles.</summary>
    public int Taille
    {
        get { lock (_lock) return _store.Count; }
    }

    // ============== Persistance ==============

    private void Charger()
    {
        if (_cheminFichier is null || !File.Exists(_cheminFichier)) return;
        try
        {
            var json = File.ReadAllText(_cheminFichier);
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj) return;
            _store.Clear();
            foreach (var kv in obj)
            {
                // On stocke le JsonNode directement pour preserver le type.
                _store[kv.Key] = kv.Value?.DeepClone();
            }
        }
        catch (Exception)
        {
            // Si le fichier est corrompu, on repart de zero sans crash.
            _store.Clear();
        }
    }

    private void Sauvegarder()
    {
        if (_cheminFichier is null) return;
        try
        {
            var obj = new JsonObject();
            foreach (var kv in _store)
            {
                obj[kv.Key] = kv.Value switch
                {
                    null => null,
                    JsonNode n => n.DeepClone(),
                    string s => JsonValue.Create(s),
                    bool b => JsonValue.Create(b),
                    double d => JsonValue.Create(d),
                    int i => JsonValue.Create(i),
                    _ => JsonValue.Create(kv.Value?.ToString() ?? ""),
                };
            }
            var tmp = _cheminFichier + ".tmp";
            File.WriteAllText(tmp, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            // Ecriture atomique : remplace le fichier cible
            if (File.Exists(_cheminFichier)) File.Delete(_cheminFichier);
            File.Move(tmp, _cheminFichier);
        }
        catch (Exception)
        {
            // Pas de crash : la prochaine mutation re-essaiera.
        }
    }

    private static object? Normaliser(object? v) => v switch
    {
        null => null,
        JsonNode => v,
        string or bool or double or int or long or float or decimal => v,
        _ => JsonSerializer.Serialize(v),
    };
}
