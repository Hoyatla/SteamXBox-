using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Singleton : scanne Outils/Langages/*/moteur.json, execute la commande de detection,
/// et expose la liste des moteurs effectivement presents sur la machine.
/// </summary>
public static class Detecteur
{
    private static List<MoteurSpec>? _moteurs;
    private static readonly object _lock = new();

    /// <summary>Repertoire racine des langages (relatif a AppContext.BaseDirectory).</summary>
    public static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils", "Langages");

    /// <summary>Tous les moteurs detectes (dans l ordre de latence croissante).</summary>
    public static IReadOnlyList<MoteurSpec> Moteurs
    {
        get
        {
            lock (_lock)
            {
                if (_moteurs is null) _moteurs = Scanner();
                return _moteurs;
            }
        }
    }

    /// <summary>Trouve un moteur par id (null si absent).</summary>
    public static MoteurSpec? Trouver(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return Moteurs.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Force la re-detection (appele apres une install).</summary>
    public static void Recharger()
    {
        lock (_lock) { _moteurs = null; }
    }

    private static List<MoteurSpec> Scanner()
    {
        var liste = new List<MoteurSpec>();
        if (!Directory.Exists(Racine)) return liste;
        var opt = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        foreach (var dossier in Directory.EnumerateDirectories(Racine))
        {
            var path = Path.Combine(dossier, "moteur.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var m = JsonSerializer.Deserialize<MoteurSpec>(json, opt);
                if (m is null) continue;
                m.Dossier = dossier;
                if (Detecte(m)) liste.Add(m);
            }
            catch { /* manifeste corrompu, on ignore */ }
        }
        // Tri par latence croissante (le moins cher d abord)
        liste.Sort((a, b) => a.LatenceMs.CompareTo(b.LatenceMs));
        return liste;
    }

    /// <summary>Execute la commande de detection et retourne true si elle reussit (exit 0 en 5s).</summary>
    public static bool Detecte(MoteurSpec m)
    {
        try
        {
            // Si l'executable n existe pas sur disque ET n est pas dans le PATH, on skip
            // (le test final se fait dans ExecuterCode avant l execution, ici juste la verif rapide).
            if (m.Rang == "embarque" && !File.Exists(m.ExecutableAbsolu)) return false;

            // La commande de detection est un programme + args (ex: "node --version").
            // On coupe au premier espace : FileName = programme, ArgumentList = args.
            var cmd = m.Detection ?? "";
            var firstSpace = cmd.IndexOf(' ');
            var programme = firstSpace < 0 ? cmd : cmd.Substring(0, firstSpace);
            var reste = firstSpace < 0 ? "" : cmd.Substring(firstSpace + 1);

            var psi = new ProcessStartInfo
            {
                FileName = programme,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = m.Dossier,
            };
            if (!string.IsNullOrWhiteSpace(reste)) psi.ArgumentList.Add(reste);
            using var p = Process.Start(psi);
            if (p is null) return false;
            if (!p.WaitForExit(5_000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}