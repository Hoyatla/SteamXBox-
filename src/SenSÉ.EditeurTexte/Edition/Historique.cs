using System.Collections.Generic;
using System.Windows.Documents;
using SenSÉ.EditeurTexte.Format;

namespace SenSÉ.EditeurTexte.Edition;

/// <summary>
/// Pile d'etats de <see cref="FlowDocument"/> pour annuler/retablir.
/// Limite 50 entrees. Clone par XAML round-trip (XamlRoundTrip) — un FlowDocument
/// n'a pas de Clone() et le partage d'inlines detruit le document original.
/// </summary>
public sealed class Historique
{
    private const int Limite = 50;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _actuel;

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        _actuel = null;
    }

    public void Push(FlowDocument etat)
    {
        if (etat is null) return;
        var xaml = XamlRoundTrip.VersXaml(etat);
        if (_actuel is not null && _actuel == xaml) return;
        if (_actuel is not null) _undo.Push(_actuel);
        _actuel = xaml;
        if (_undo.Count > Limite)
        {
            // Stack n'a pas de Trim — on reconstitue.
            var temp = new Stack<string>(_undo.Take(Limite).Reverse());
            _undo.Clear();
            foreach (var s in temp) _undo.Push(s);
        }
        _redo.Clear();
    }

    public bool PeutAnnuler => _actuel is not null && _undo.Count > 0;
    public bool PeutRetablir => _redo.Count > 0;

    public void Undo(FlowDocument cible)
    {
        if (!PeutAnnuler) return;
        _redo.Push(_actuel!);
        _actuel = _undo.Pop();
        XamlRoundTrip.DepuisXaml(cible, _actuel!);
    }

    public void Redo(FlowDocument cible)
    {
        if (!PeutRetablir) return;
        _undo.Push(_actuel!);
        _actuel = _redo.Pop();
        XamlRoundTrip.DepuisXaml(cible, _actuel!);
    }
}
