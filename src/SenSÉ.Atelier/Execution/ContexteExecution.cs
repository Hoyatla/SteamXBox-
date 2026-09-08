using System.Collections.Generic;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Execution;

/// <summary>
/// Contexte passe a l'executeur d'un noeud. Contient les entrees (par nom
/// de port), les params, un journal, et l'API pour ecrire les sorties.
/// </summary>
public sealed class ContexteExecution
{
    public string NoeudId { get; }
    public string NoeudType { get; }
    public Dictionary<string, object?> Entrees { get; } = new();
    public Dictionary<string, object?> Params { get; } = new();
    public Action<string>? Journal { get; set; }
    public CancellationToken Annulation { get; set; }

    /// <summary>Si un Executeur met ce flag a true (via controle_si),
    /// le moteur skip tous les noeuds en aval dans la meme branche.
    /// MVP : le moteur collecte les IDs downstream et les desactive.</summary>
    public bool DesactiveAval { get; set; }

    public ContexteExecution(string noeudId, string noeudType)
    {
        NoeudId = noeudId;
        NoeudType = noeudType;
    }

    public string Ch(string cle, string defaut = "")
        => Params.TryGetValue(cle, out var v) ? v?.ToString() ?? defaut : defaut;

    public int ChInt(string cle, int defaut = 0)
        => Params.TryGetValue(cle, out var v) && int.TryParse(v?.ToString(), out var i) ? i : defaut;

    public double ChDouble(string cle, double defaut = 0)
        => Params.TryGetValue(cle, out var v) && double.TryParse(v?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : defaut;

    public bool ChBool(string cle, bool defaut = false)
        => Params.TryGetValue(cle, out var v) && bool.TryParse(v?.ToString(), out var b) && b;

    public string? Entree(string cle)
        => Entrees.TryGetValue(cle, out var v) ? v?.ToString() : null;
}