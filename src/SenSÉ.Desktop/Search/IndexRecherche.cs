using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Search;

/// <summary>
/// Les index de recherche de la session : les fichiers et applications, les volumes montés, le
/// courrier. Construits une fois, lus par la barre de recherche comme par l'Assistant.
/// </summary>
/// <remarks>
/// <b>Pourquoi les sortir de la fenêtre.</b> Ces trois collections étaient des champs d'instance de
/// <see cref="SearchLauncherWindow"/>, ce qui allait tant qu'elle était seule à s'en servir. Ouvrir
/// la recherche à l'Assistant posait alors le choix : partager cet index, ou en reconstruire un
/// deuxième. Le second aurait fait parcourir tous les disques une fois de plus par session — ce que
/// le code évite délibérément au démarrage, et pour de bonnes raisons. D'où ce dépôt commun : un
/// seul parcours, deux lecteurs.
///
/// <para><b>La paresse est conservée telle quelle.</b> Rien n'est construit au démarrage de
/// l'environnement. Les fichiers attendent la première invocation — de la barre ou de l'Assistant —
/// et le courrier attend qu'on le demande par son préfixe. Un environnement qui parcourt les
/// disques pendant que l'utilisateur essaie de lancer un jeu est pire qu'une recherche lente la
/// première fois.</para>
///
/// <para><b>Deux chemins d'accès, deux besoins.</b> La fenêtre construit hors du fil d'interface et
/// se rafraîchit quand c'est prêt : elle a un statut à afficher et un focus à reprendre. L'Assistant
/// appelle depuis un fil de travail et peut attendre — <see cref="AssurerFichiers"/> et
/// <see cref="AssurerCourrier"/> construisent sur place si personne ne l'a fait. Le verrou évite
/// que les deux ne lancent le même parcours en même temps.</para>
/// </remarks>
public static class IndexRecherche
{
    private static readonly object _verrou = new();

    private static IReadOnlyList<SearchItem> _fichiers = [];
    private static IReadOnlyList<SearchItem> _volumes = [];
    private static MailIndex _courrier = MailIndex.Empty;

    /// <summary>Les fichiers et applications indexés. Vide tant que rien ne l'a demandé.</summary>
    public static IReadOnlyList<SearchItem> Fichiers => _fichiers;

    /// <summary>
    /// Les volumes montés.
    /// </summary>
    /// <remarks>
    /// Tenus à part des fichiers, et c'est ce qui permet à une clé branchée entre deux recherches
    /// d'apparaître sans rien reconstruire.
    /// </remarks>
    public static IReadOnlyList<SearchItem> Volumes => _volumes;

    /// <summary>Le courrier indexé. Vide tant que le préfixe ne l'a pas réclamé.</summary>
    public static MailIndex Courrier => _courrier;

    /// <summary>Vrai si l'index des fichiers porte déjà quelque chose.</summary>
    public static bool FichiersPrets => _fichiers.Count > 0;

    /// <summary>Range un index de fichiers construit ailleurs — par la fenêtre, hors du fil d'interface.</summary>
    public static void PoserFichiers(IReadOnlyList<SearchItem>? fichiers)
        => _fichiers = fichiers ?? [];

    /// <summary>Range un index de courrier construit ou chargé ailleurs.</summary>
    public static void PoserCourrier(MailIndex? courrier)
        => _courrier = courrier ?? MailIndex.Empty;

    /// <summary>Relève les volumes actuellement montés.</summary>
    public static void RafraichirVolumes(Action<string>? journal)
    {
        try
        {
            _volumes = Desktop.Search.Volumes.AsSearchItems(journal);
        }
        catch (Exception exception)
        {
            journal?.Invoke($"index: relevé des volumes échoué : {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Rend l'index des fichiers, en le construisant sur place s'il est vide.
    /// </summary>
    /// <remarks>
    /// Bloquant : à n'appeler que depuis un fil de travail. C'est le chemin de l'Assistant, qui
    /// tourne déjà hors interface et pour qui attendre le premier parcours vaut mieux que répondre
    /// « rien trouvé » sur un index vide — une réponse fausse qu'il n'aurait aucun moyen de
    /// distinguer d'une vraie.
    /// </remarks>
    public static IReadOnlyList<SearchItem> AssurerFichiers(Action<string>? journal)
    {
        if (_fichiers.Count > 0) return _fichiers;

        lock (_verrou)
        {
            if (_fichiers.Count > 0) return _fichiers;

            try
            {
                _fichiers = SearchIndexBuilder.Build(journal);
            }
            catch (Exception exception)
            {
                journal?.Invoke($"index: construction échouée : {exception.GetType().Name}: {exception.Message}");
                _fichiers = [];
            }
        }

        if (_volumes.Count == 0)
        {
            RafraichirVolumes(journal);
        }

        return _fichiers;
    }

    /// <summary>
    /// Rend l'index du courrier, en le chargeant ou le construisant s'il est vide.
    /// </summary>
    /// <remarks>
    /// L'index sauvegardé est chargé d'abord : l'attente a lieu une fois pour toutes plutôt qu'une
    /// fois par session. Bloquant, pour les mêmes raisons que <see cref="AssurerFichiers"/>.
    /// </remarks>
    public static MailIndex AssurerCourrier(Action<string>? journal)
    {
        if (_courrier.Count > 0) return _courrier;

        lock (_verrou)
        {
            if (_courrier.Count > 0) return _courrier;

            try
            {
                _courrier = MailIndex.Load(journal);

                if (_courrier.Count == 0 && MailIndexBuilder.AnythingToIndex())
                {
                    _courrier = MailIndexBuilder.Build(journal);
                    _courrier.Save(journal);
                }
            }
            catch (Exception exception)
            {
                journal?.Invoke($"index: courrier échoué : {exception.GetType().Name}: {exception.Message}");
                _courrier = MailIndex.Empty;
            }
        }

        return _courrier;
    }
}
