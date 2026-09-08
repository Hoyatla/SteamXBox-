using System.Collections.Generic;

namespace SenSÉ.Atelier.Modele;

public enum StatutExecution
{
    EnAttente,
    EnCours,
    Reussi,
    Echec,
    Annule,
}

public sealed class ResultatExecution
{
    /// <summary>Mis a true par un Executeur (controle_si) pour demander au moteur de skip les noeuds en aval.</summary>
    public bool DesactiveAval { get; set; }
    public bool Succes { get; set; } = true;
    public string? Erreur { get; set; }
    public Dictionary<string, object?> Sorties { get; set; } = new();
    public long DureeMs { get; set; }
    public string? Log { get; set; }

    public static ResultatExecution Ok(Dictionary<string, object?>? sorties = null, string? log = null)
    {
        var r = new ResultatExecution { Succes = true, Log = log };
        if (sorties is not null)
        {
            foreach (var kv in sorties) r.Sorties[kv.Key] = kv.Value;
        }
        return r;
    }

    public static ResultatExecution Fail(string erreur, string? log = null)
        => new() { Succes = false, Erreur = erreur, Log = log };
}

public sealed class EtatNoeudExecution
{
    public string NoeudId { get; set; } = "";
    public string Type { get; set; } = "";
    public StatutExecution Statut { get; set; } = StatutExecution.EnAttente;
    public string? Erreur { get; set; }
    public Dictionary<string, object?> Sorties { get; set; } = new();
    public long DureeMs { get; set; }
}

public sealed class ExecutionGraphe
{
    public string ExecutionId { get; set; } = "";
    public string GrapheId { get; set; } = "";
    public StatutExecution Statut { get; set; } = StatutExecution.EnAttente;
    public List<EtatNoeudExecution> Noeuds { get; } = new();
    public Dictionary<string, object?> SortiesFinales { get; } = new();
    public string? Erreur { get; set; }
    public DateTime DemarreeLe { get; set; } = DateTime.UtcNow;
    public DateTime TermineeLe { get; set; }
}