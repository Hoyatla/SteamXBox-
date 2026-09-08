using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Execution;

/// <summary>
/// Le moteur d'execution : prend un graphe, fait le tri topologique,
/// execute les noeuds dans l'ordre, propage les sorties par les liens.
/// </summary>
/// <remarks>
/// <para>Asynchrone : chaque noeud est execute via son Executeur (qui
/// peut etre synchrone ou async via wrapping).</para>
///
/// <para>Le moteur est un singleton (accès via <see cref="Instance"/>) :
/// l'UI et le serveur HTTP partagent la meme instance pour permettre
/// le suivi live des executions en cours.</para>
/// </remarks>
public sealed class Moteur
{
    public static Moteur Instance { get; } = new();
    private Moteur() { }

    private readonly Dictionary<string, ExecutionGraphe> _executions = new();
    private readonly Dictionary<string, ManualResetEventSlim> _terminaisons = new();
    private readonly Dictionary<string, HashSet<string>> _saute = new();
    private readonly object _lock = new();

    public ExecutionGraphe? EtatExecution(string executionId)
    {
        lock (_lock) return _executions.TryGetValue(executionId, out var e) ? e : null;
    }

    public List<ExecutionGraphe> ToutesExecutions()
    {
        lock (_lock) return _executions.Values.ToList();
    }

    public void Annuler(string executionId)
    {
        if (EtatExecution(executionId) is { } e && e.Statut == StatutExecution.EnCours)
            e.Statut = StatutExecution.Annule;
    }

    /// <summary>Lance l'execution d'un graphe de maniere asynchrone.</summary>
    public ExecutionGraphe LancerAsync(Graphe g, Dictionary<string, object?>? entree = null, CancellationToken ct = default)
    {
        var exec = new ExecutionGraphe
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            GrapheId = g.Id,
            Statut = StatutExecution.EnCours,
            DemarreeLe = DateTime.UtcNow,
        };
        // Initialiser les etats noeuds
        foreach (var n in g.Noeuds)
        {
            exec.Noeuds.Add(new EtatNoeudExecution
            {
                NoeudId = n.Id,
                Type = n.Type,
                Statut = StatutExecution.EnAttente,
            });
        }
        lock (_lock) { _executions[exec.ExecutionId] = exec; _terminaisons[exec.ExecutionId] = new ManualResetEventSlim(false); }
        _ = Task.Run(() => ExecuterInterneAsync(exec, g, entree ?? new(), ct), ct);
        return exec;
    }

    /// <summary>Variante synchrone de LancerAsync. Bloque jusqu'a la fin et renvoie l'ExecutionGraphe final.</summary>
    public ExecutionGraphe LancerSync(Graphe g, Dictionary<string, object?>? entree = null)
    {
        var exec = LancerAsync(g, entree);
        ManualResetEventSlim? ev;
        lock (_lock) { _terminaisons.TryGetValue(exec.ExecutionId, out ev); }
        ev?.Wait();
        return EtatExecution(exec.ExecutionId) ?? exec;
    }

    /// <summary>Execute un seul noeud (test isole) et renvoie l'execution.</summary>
    public ExecutionGraphe ExecuterNoeudAsync(Noeud n, Dictionary<string, object?>? entree = null, CancellationToken ct = default)
    {
        var g = new Graphe { Nom = "_test_" + n.Id };
        g.Noeuds.Add(n);
        return LancerAsync(g, entree, ct);
    }

    private async Task ExecuterInterneAsync(ExecutionGraphe exec, Graphe g, Dictionary<string, object?> entrees, CancellationToken ct)
    {
        try
        {
            // Topo sort
            var ordre = TopoSort(g);
            if (ordre is null)
            {
                exec.Statut = StatutExecution.Echec;
                exec.Erreur = "cycle detecte dans le graphe";
                exec.TermineeLe = DateTime.UtcNow;
                return;
            }

            // Map noeud_id -> sorties (par nom de port)
            var sortiesParNoeud = new Dictionary<string, Dictionary<string, object?>>();

            foreach (var noeudId in ordre)
            {
                ct.ThrowIfCancellationRequested();
                if (exec.Statut == StatutExecution.Annule) return;

                var noeud = g.TrouverNoeud(noeudId);
                if (noeud is null) continue;
                var def = CatalogueNoeuds.Trouver(noeud.Type);
                var etat = exec.Noeuds.First(e => e.NoeudId == noeudId);
                // Check skip downstream (controle_si avec DesactiveAval=true)
                HashSet<string>? aSaute;
                lock (_lock) { _saute.TryGetValue(exec.ExecutionId, out aSaute); }
                if (aSaute is not null && aSaute.Contains(noeudId))
                {
                    etat.Statut = StatutExecution.Reussi;
                    etat.Sorties = new Dictionary<string, object?>();
                    sortiesParNoeud[noeudId] = new Dictionary<string, object?>();
                    continue;
                }
                etat.Statut = StatutExecution.EnCours;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                if (def is null)
                {
                    etat.Statut = StatutExecution.Echec;
                    etat.Erreur = $"type de noeud inconnu: {noeud.Type}";
                    exec.Statut = StatutExecution.Echec;
                    exec.Erreur = etat.Erreur;
                    return;
                }

                // Construire le contexte
                var ctx = new ContexteExecution(noeud.Id, noeud.Type)
                {
                    Journal = s => Console.WriteLine($"[atelier] {noeud.Type}: {s}"),
                    Annulation = ct,
                };
                // Entrées : on tire des liens entrants + entrées externes
                foreach (var p in def.PortsEntree)
                {
                    var lien = g.Liens.FirstOrDefault(l => l.NoeudCibleId == noeudId && l.PortCibleNom == p.Nom);
                    if (lien is not null && sortiesParNoeud.TryGetValue(lien.NoeudSourceId, out var src) &&
                        src.TryGetValue(lien.PortSourceNom, out var val))
                    {
                        ctx.Entrees[p.Nom] = val;
                    }
                }
                // Si pas de lien et qu'une entree globale du meme nom existe, on l'utilise
                foreach (var p in def.PortsEntree)
                {
                    if (!ctx.Entrees.ContainsKey(p.Nom) && entrees.TryGetValue(p.Nom, out var ev))
                        ctx.Entrees[p.Nom] = ev;
                }
                // Params du noeud
                foreach (var kv in noeud.Params) ctx.Params[kv.Key] = kv.Value;

                ResultatExecution res;
                try
                {
                    res = await def.Executeur(ctx);
                }
                catch (Exception ex)
                {
                    res = ResultatExecution.Fail(ex.Message);
                }

                sw.Stop();
                etat.DureeMs = sw.ElapsedMilliseconds;
                
                etat.Sorties = new Dictionary<string, object?>(res.Sorties);
                sortiesParNoeud[noeudId] = res.Sorties;

                if (!res.Succes)
                {
                    etat.Statut = StatutExecution.Echec;
                    etat.Erreur = res.Erreur;
                    exec.Statut = StatutExecution.Echec;
                    exec.Erreur = $"noeud {noeud.Type} ({noeudId[..8]}): {res.Erreur}";
                    exec.TermineeLe = DateTime.UtcNow;
                    return;
                }
                if (res.DesactiveAval) PropagerSkip(exec.ExecutionId, noeudId, g);
                etat.Statut = StatutExecution.Reussi;
            }

            // Sorties finales = sorties des noeuds qui n'ont pas de successeurs (terminaux)
            var terminaux = g.Noeuds
                .Where(n => !g.Liens.Any(l => l.NoeudSourceId == n.Id))
                .ToList();
            foreach (var t in terminaux)
            {
                if (sortiesParNoeud.TryGetValue(t.Id, out var s))
                {
                    foreach (var kv in s) exec.SortiesFinales[$"{t.Type}.{kv.Key}"] = kv.Value;
                }
            }
            exec.Statut = StatutExecution.Reussi;
            exec.TermineeLe = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            exec.Statut = StatutExecution.Annule;
            exec.TermineeLe = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            exec.Statut = StatutExecution.Echec;
            exec.Erreur = ex.Message;
            exec.TermineeLe = DateTime.UtcNow;
        }
        finally { SetEventTerminaison(exec.ExecutionId); }
    }

    /// <summary>
    /// Tri topologique par algorithme de Kahn. Renvoie null si le graphe
    /// contient un cycle. Les noeuds sans dépendance sont executes
    /// en premier, dans l'ordre de declaration.
    /// </summary>
    private static List<string>? TopoSort(Graphe g)
    {
        var inDeg = g.Noeuds.ToDictionary(n => n.Id, _ => 0);
        foreach (var l in g.Liens)
        {
            if (inDeg.ContainsKey(l.NoeudCibleId)) inDeg[l.NoeudCibleId]++;
        }
        var queue = new Queue<string>(inDeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var ordre = new List<string>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            ordre.Add(id);
            foreach (var l in g.Liens.Where(l => l.NoeudSourceId == id))
            {
                if (--inDeg[l.NoeudCibleId] == 0) queue.Enqueue(l.NoeudCibleId);
            }
        }
        return ordre.Count == g.Noeuds.Count ? ordre : null;
    }

    private void SetEventTerminaison(string executionId)
    {
        ManualResetEventSlim? ev;
        lock (_lock)
        {
            if (_terminaisons.TryGetValue(executionId, out ev)) _terminaisons.Remove(executionId);
            _saute.Remove(executionId);
        }
        if (ev is not null) { try { ev.Set(); ev.Dispose(); } catch { } }
    }

    /// <summary>Apres un controle_si qui demande a skip, marque tous les noeuds en aval (BFS sur les liens) comme a sauter.</summary>
    private void PropagerSkip(string executionId, string fromNoeudId, Graphe g)
    {
        HashSet<string>? aSaute;
        lock (_lock)
        {
            if (!_saute.TryGetValue(executionId, out aSaute))
            { aSaute = new HashSet<string>(); _saute[executionId] = aSaute; }
        }
        var queue = new Queue<string>();
        foreach (var l in g.Liens.Where(l => l.NoeudSourceId == fromNoeudId))
            queue.Enqueue(l.NoeudCibleId);
        while (queue.Count > 0)
        {
            var nid = queue.Dequeue();
            if (aSaute!.Contains(nid)) continue;
            aSaute.Add(nid);
            foreach (var l in g.Liens.Where(l => l.NoeudSourceId == nid))
                queue.Enqueue(l.NoeudCibleId);
        }
    }
}