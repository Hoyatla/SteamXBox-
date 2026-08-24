using System.IO;

namespace SteamXBox.Plugins;

/// <summary>
/// Surveille les dossiers d'outils et prévient quand ce qu'ils contiennent a changé.
/// </summary>
/// <remarks>
/// <b>Pourquoi guetter plutôt qu'indexer.</b> Un index se désynchronise, et il faut penser à le
/// régénérer — le jour où on oublie, l'écran ment sans le dire. Le système de fichiers, lui, sait
/// déjà prévenir. Le coût est de quelques millisecondes par changement et il ne peut pas mentir.
///
/// <para>
/// <b>Le délai n'est pas une prudence, c'est une nécessité.</b> Un installeur écrit des milliers de
/// fichiers en quelques secondes ; reconstruire la grille à chacun la ferait clignoter pendant toute
/// l'installation et mangerait le fil d'affichage. On attend donc que ça se calme, et l'on ne
/// prévient qu'une fois — ce qui intéresse l'écran n'est pas chaque fichier mais le fait qu'il y ait
/// eu un changement.
/// </para>
///
/// <para>
/// Les deux dossiers comptent, et pour des raisons différentes. <c>Plugins</c> porte les manifestes,
/// donc les tuiles ; <c>Outils</c> porte les corps, donc ce qu'un manifeste promet. Un outil dont le
/// corps disparaît garde une tuile qui ne mène nulle part si l'on ne regarde que le premier.
/// </para>
/// </remarks>
public sealed class VeilleOutils : IDisposable
{
    private readonly List<FileSystemWatcher> _guetteurs = [];
    private readonly System.Timers.Timer _calme;
    private readonly Action<string>? _journal;

    /// <summary>Combien de temps sans le moindre changement avant de prévenir.</summary>
    /// <remarks>
    /// Assez long pour qu'une installation entière ne compte que pour un, assez court pour qu'un
    /// dossier déposé à la main apparaisse avant qu'on se demande s'il faut redémarrer.
    /// </remarks>
    public const int CalmeMillisecondes = 1200;

    /// <summary>Prévient qu'il faut relire les outils. Arrive hors du fil d'affichage.</summary>
    public event Action? Change;

    public VeilleOutils(string racine, Action<string>? journal = null)
    {
        _journal = journal;
        _calme = new System.Timers.Timer(CalmeMillisecondes) { AutoReset = false };
        _calme.Elapsed += (_, _) => Change?.Invoke();

        foreach (var nom in (string[])["Plugins", "Outils"])
        {
            Guetter(Path.Combine(racine, nom));
        }
    }

    private void Guetter(string dossier)
    {
        if (!Directory.Exists(dossier))
        {
            // Un dossier absent n'est pas une panne : « Outils » n'existe pas sur une installation
            // qui n'a encore rien accueilli. Il apparaîtra, et le prochain démarrage le prendra.
            return;
        }

        try
        {
            var guetteur = new FileSystemWatcher(dossier)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };

            guetteur.Created += (_, _) => Remuer();
            guetteur.Deleted += (_, _) => Remuer();
            guetteur.Renamed += (_, _) => Remuer();
            guetteur.Changed += (_, _) => Remuer();

            // Un débordement se produit quand il se passe plus de choses que la mémoire du guetteur
            // n'en tient — précisément pendant une installation. Il ne dit pas quoi a changé, mais
            // il dit que quelque chose a changé, et c'est tout ce dont on a besoin.
            guetteur.Error += (_, _) => Remuer();

            guetteur.EnableRaisingEvents = true;
            _guetteurs.Add(guetteur);

            _journal?.Invoke($"veille : {dossier}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Sans veille, on retombe sur l'ancien comportement : les outils apparaissent au
            // prochain démarrage. C'est une régression de confort, pas une panne.
            _journal?.Invoke($"veille impossible sur {dossier} : {exception.Message}");
        }
    }

    private void Remuer()
    {
        _calme.Stop();
        _calme.Start();
    }

    public void Dispose()
    {
        foreach (var guetteur in _guetteurs)
        {
            guetteur.EnableRaisingEvents = false;
            guetteur.Dispose();
        }

        _guetteurs.Clear();
        _calme.Dispose();
    }
}
