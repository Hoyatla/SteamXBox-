using System.Collections.Generic;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Historique undo/redo par graphe. Chaque modif (push) serialise le graphe
/// en JSON via Persistance. Undo depile le dernier snapshot, restore, et
/// empile l'etat courant dans redo. Limite : 100 entrees par pile.
/// </summary>
public static class Historique
{
    private const int Limite = 100;
    private static readonly Dictionary<string, Stack<string>> _undo = new();
    private static readonly Dictionary<string, Stack<string>> _redo = new();

    /// <summary>Push l'etat courant du graphe dans la pile undo. Vide redo.</summary>
    public static void Pousser(Graphe g)
    {
        if (!_undo.TryGetValue(g.Id, out var pile)) { pile = new Stack<string>(); _undo[g.Id] = pile; }
        pile.Push(Persistance.ToJson(g));
        while (pile.Count > Limite) { var arr = pile.ToArray(); pile.Clear(); for (int i = arr.Length - 2; i >= 0; i--) pile.Push(arr[i]); }
        if (_redo.TryGetValue(g.Id, out var r)) r.Clear();
    }

    /// <summary>Annule la derniere action. Retourne true si ok.</summary>
    public static bool Annuler(Graphe g)
    {
        if (!_undo.TryGetValue(g.Id, out var pile) || pile.Count == 0) return false;
        if (!_redo.TryGetValue(g.Id, out var redo)) { redo = new Stack<string>(); _redo[g.Id] = redo; }
        redo.Push(Persistance.ToJson(g));
        var snap = pile.Pop();
        Persistance.FromJson(g, snap);
        return true;
    }

    /// <summary>Refait la derniere action annulee. Retourne true si ok.</summary>
    public static bool Refaire(Graphe g)
    {
        if (!_redo.TryGetValue(g.Id, out var redo) || redo.Count == 0) return false;
        if (!_undo.TryGetValue(g.Id, out var pile)) { pile = new Stack<string>(); _undo[g.Id] = pile; }
        pile.Push(Persistance.ToJson(g));
        var snap = redo.Pop();
        Persistance.FromJson(g, snap);
        return true;
    }

    public static bool PeutAnnuler(string grapheId) => _undo.TryGetValue(grapheId, out var p) && p.Count > 0;
    public static bool PeutRefaire(string grapheId) => _redo.TryGetValue(grapheId, out var p) && p.Count > 0;

    /// <summary>Vide les piles d'un graphe (ex: suppression du graphe).</summary>
    public static void Vider(string grapheId)
    {
        _undo.Remove(grapheId);
        _redo.Remove(grapheId);
    }
}