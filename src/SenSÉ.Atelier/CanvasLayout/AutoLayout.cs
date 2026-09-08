using System;
using System.Collections.Generic;
using System.Linq;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.CanvasLayout;

/// <summary>
/// Auto-layout deterministe pour les graphes. Place les noeuds en grille
/// selon leur profondeur (BFS depuis les sources).
/// </summary>
public static class AutoLayout
{
    public const double PasHorizontal = 250.0; // entre rangees
    public const double PasVertical = 200.0;   // entre noeuds d'une meme rangee
    public const double MargeX = 100.0;
    public const double MargeY = 100.0;

    public enum Sens { Horizontal, Vertical }

    /// <summary>Retourne un dictionnaire noeud_id -> (X, Y) avec le layout calcule.</summary>
    public static Dictionary<string, (double X, double Y)> Calculer(Graphe g, Sens sens)
    {
        var result = new Dictionary<string, (double, double)>();
        if (g.Noeuds.Count == 0) return result;

        // 1. Index des noeuds et des liens entrants
        var noeudIds = g.Noeuds.Select(n => n.Id).ToHashSet();
        var entrants = new Dictionary<string, List<string>>();
        foreach (var n in g.Noeuds) entrants[n.Id] = new List<string>();
        foreach (var l in g.Liens)
        {
            if (noeudIds.Contains(l.NoeudSourceId) && noeudIds.Contains(l.NoeudCibleId))
                entrants[l.NoeudCibleId].Add(l.NoeudSourceId);
        }

        // 2. Trouver les sources (noeuds sans entrant)
        var sources = g.Noeuds.Where(n => entrants[n.Id].Count == 0).Select(n => n.Id).ToList();
        if (sources.Count == 0)
        {
            // Cycle ou graphe vide : on prend le premier noeud comme source
            sources.Add(g.Noeuds[0].Id);
        }

        // 3. BFS par couche (longueur du plus long chemin depuis une source)
        var profondeur = new Dictionary<string, int>();
        var ordre = new List<string>();
        var queue = new Queue<string>();
        foreach (var s in sources) { profondeur[s] = 0; queue.Enqueue(s); ordre.Add(s); }
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var l in g.Liens.Where(l => l.NoeudSourceId == id && noeudIds.Contains(l.NoeudCibleId)))
            {
                var cible = l.NoeudCibleId;
                var nouvelleProf = profondeur[id] + 1;
                if (!profondeur.TryGetValue(cible, out var old) || nouvelleProf > old)
                {
                    profondeur[cible] = nouvelleProf;
                    if (!ordre.Contains(cible)) { ordre.Add(cible); queue.Enqueue(cible); }
                }
            }
        }
        // Noeuds non atteints (autre composante connexe) : profondeur 0
        foreach (var n in g.Noeuds)
            if (!profondeur.ContainsKey(n.Id)) { profondeur[n.Id] = 0; ordre.Add(n.Id); }

        // 4. Grouper par profondeur
        var parProfondeur = profondeur.GroupBy(kv => kv.Value).OrderBy(g => g.Key).ToList();

        // 5. Placer en grille
        foreach (var grp in parProfondeur)
        {
            var idx = 0;
            foreach (var (noeudId, _) in grp.OrderBy(kv => kv.Key))
            {
                if (sens == Sens.Horizontal)
                {
                    // Profondeur = X, index dans la rangee = Y
                    result[noeudId] = (MargeX + grp.Key * PasHorizontal, MargeY + idx * PasVertical);
                }
                else
                {
                    // Profondeur = Y, index dans la rangee = X
                    result[noeudId] = (MargeX + idx * PasVertical, MargeY + grp.Key * PasHorizontal);
                }
                idx++;
            }
        }
        return result;
    }

    /// <summary>Applique le layout au graphe en place. Sauvegarde via la persistance si passee.</summary>
    public static int Appliquer(Graphe g, Sens sens)
    {
        var positions = Calculer(g, sens);
        int nb = 0;
        foreach (var n in g.Noeuds)
        {
            if (positions.TryGetValue(n.Id, out var p))
            {
                n.X = p.X;
                n.Y = p.Y;
                nb++;
            }
        }
        g.ModifieLe = DateTime.UtcNow;
        return nb;
    }
}
