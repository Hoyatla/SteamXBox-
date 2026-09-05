using System.IO;
using SenSÉ.Mcp.Bus;

namespace SenSÉ.Desktop.Observateurs;

/// <summary>
/// Surveille le dossier <c>Outils\Projets\</c> et pousse un evenement
/// sur l'EventBus a chaque creation, modification ou suppression.
/// </summary>
/// <remarks>
/// <b>Premier observateur de la liste.</b> Il sert de demo et de
/// reeller : un fichier .cs/.py/.ps1 depose, l'Assistant le voit et
/// peut le commenter, le valider, le lancer.
///
/// <para><b>Debounce implicite.</b> Un editeur qui enregistre un
/// fichier declenche souvent 2-3 evenements en quelques millisecondes.
/// On attend 200ms de silence avant de pousser pour ne pas inonder
/// l'EventBus.</para>
/// </remarks>
public sealed class ProjetsWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly EventBus _bus;
    private readonly Action<string>? _journal;
    private readonly Dictionary<string, DateTime> _dernierEvenement = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    public ProjetsWatcher(string racine, EventBus bus, Action<string>? journal = null)
    {
        if (!Directory.Exists(racine))
        {
            Directory.CreateDirectory(racine);
        }

        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _journal = journal;
        _watcher = new FileSystemWatcher(racine)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += SurChangement;
        _watcher.Changed += SurChangement;
        _watcher.Deleted += SurChangement;
        _watcher.Renamed += SurRenommage;
        _watcher.Error += SurErreur;
    }

    /// <summary>Demarre la surveillance.</summary>
    public void Demarrer()
    {
        _watcher.EnableRaisingEvents = true;
        _journal?.Invoke($"ProjetsWatcher: surveillance demarree sur {_watcher.Path}");
    }

    /// <summary>Arrete la surveillance.</summary>
    public void Arreter()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void SurChangement(object sender, FileSystemEventArgs e)
    {
        if (DebounceRejete(e.FullPath)) return;
        Pousser(TypeEvenement(e.ChangeType), e.FullPath, null);
    }

    private void SurRenommage(object sender, RenamedEventArgs e)
    {
        if (DebounceRejete(e.FullPath)) return;
        Pousser("fichier.renomme", e.FullPath, new Dictionary<string, string>
        {
            ["ancien"] = e.OldFullPath,
        });
    }

    private void SurErreur(object sender, ErrorEventArgs e)
    {
        _journal?.Invoke($"ProjetsWatcher erreur: {e.GetException()?.Message}");
    }

    private bool DebounceRejete(string chemin)
    {
        lock (_gate)
        {
            var maintenant = DateTime.UtcNow;
            if (_dernierEvenement.TryGetValue(chemin, out var precedent)
                && maintenant - precedent < Debounce)
            {
                return true;
            }
            _dernierEvenement[chemin] = maintenant;
            return false;
        }
    }

    private static string TypeEvenement(WatcherChangeTypes change)
        => change switch
        {
            WatcherChangeTypes.Created => "fichier.cree",
            WatcherChangeTypes.Deleted => "fichier.supprime",
            WatcherChangeTypes.Changed => "fichier.modifie",
            _ => "fichier.inconnu",
        };

    private void Pousser(string type, string chemin, Dictionary<string, string>? extra)
    {
        var donnees = new Dictionary<string, string>
        {
            ["chemin"] = chemin,
            ["nom"] = Path.GetFileName(chemin),
            ["extension"] = Path.GetExtension(chemin),
        };
        if (extra is not null)
        {
            foreach (var kv in extra) donnees[kv.Key] = kv.Value;
        }
        _bus.Pousser(new Evenement
        {
            Source = "projets",
            Type = type,
            Donnees = donnees,
        });
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}