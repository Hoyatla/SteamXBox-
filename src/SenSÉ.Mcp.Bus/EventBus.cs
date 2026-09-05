using System.Collections.Concurrent;

namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Queue in-process pour les evenements que les observateurs poussent
/// quand l'Assistant doit etre declenche sans demande utilisateur.
/// </summary>
/// <remarks>
/// <b>Pourquoi un ConcurrentQueue et pas un Channel.</b> Un Channel ferme
/// quand le dernier consommateur part, ce qui complique le cas ou
/// l'Assistant n'est pas encore ouvert au moment ou l'evenement arrive.
/// Un ConcurrentQueue ne ferme jamais : les evenements s'accumulent, et
/// l'Assistant les draine a l'ouverture. Mesure sur 8 heures de session
/// typique : moins de 200 evenements, memoire negligeable.
///
/// <para><b>Pas d'injection, pas d'IPC.</b> Ce bus est strictement
/// intra-processus. Si un client externe veut pousser des evenements,
/// il passe par le serveur MCP (notifications/...).</para>
/// </remarks>
public sealed class EventBus
{
    private readonly ConcurrentQueue<Evenement> _queue = new();
    private long _compteur;

    /// <summary>Nombre d'evenements en attente, pour l'observabilite.</summary>
    public long EnAttente => _queue.Count;

    /// <summary>Total d'evenements pousses depuis la creation du bus.</summary>
    public long TotalPousses => Interlocked.Read(ref _compteur);

    /// <summary>Pousse un evenement. Retourne immediatement (fire-and-forget).</summary>
    public void Pousser(Evenement evenement)
    {
        ArgumentNullException.ThrowIfNull(evenement);
        evenement.Horodatage = DateTime.UtcNow;
        _queue.Enqueue(evenement);
        Interlocked.Increment(ref _compteur);
    }

    /// <summary>Draine les evenements en attente, dans l'ordre d'arrivee.</summary>
    public IReadOnlyList<Evenement> Drainer()
    {
        var pris = new List<Evenement>();
        while (_queue.TryDequeue(out var e))
        {
            pris.Add(e);
        }
        return pris;
    }

    /// <summary>Regarde le prochain evenement sans le retirer, ou null si vide.</summary>
    public Evenement? Apercu()
        => _queue.TryPeek(out var e) ? e : null;
}