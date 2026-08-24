using SteamXBox.Plugins;

namespace SteamXBox.Tools.Generation;

/// <summary>
/// Remplit les choix d'un manifeste avec ce qui est réellement installé.
/// </summary>
/// <remarks>
/// <b>Le problème que cela résout est celui de la maintenance, pas celui du confort.</b> Un
/// manifeste qui énumère des noms de modèles gèle l'installation du jour où il a été écrit. Les
/// modèles, eux, changent au gré des besoins : un client en retire un, en ajoute deux, et l'outil
/// réclame toujours un fichier disparu. Il ne se plaint pas au démarrage — il échoue au premier
/// clic, des mois plus tard, sur un message qui parle d'un nom que personne ne reconnaît.
///
/// <para>
/// Ce n'est pas une crainte : deux flux de cette machine nommaient encore <c>wan2.1_i2v_480p_14B</c>,
/// <c>umt5_xxl</c> et <c>wan_2.1_vae</c>, tous retirés du disque depuis longtemps, et rien ne
/// l'avait signalé.
/// </para>
///
/// <para>
/// <b>Rien n'est modifié sur place.</b> Un manifeste chargé est partagé par la grille, les
/// panneaux et l'assistant ; le réécrire depuis l'un d'eux ferait dépendre ce que voient les autres
/// de l'ordre dans lequel on a cliqué. Une copie est rendue, et le manifeste d'origine reste ce
/// qu'il était sur le disque.
/// </para>
/// </remarks>
public static class OptionsVivantes
{
    /// <summary>Le port du générateur.</summary>
    private const int Port = 8188;

    /// <summary>
    /// Résout une volée de manifestes en n'interrogeant le générateur qu'une seule fois.
    /// </summary>
    /// <param name="manifestes">Tout ce que l'on s'apprête à déclarer d'un coup.</param>
    /// <param name="journal">Reçoit ce qui a été résolu, et ce qui ne l'a pas été.</param>
    /// <remarks>
    /// <b>Sonder par manifeste coûtait une attente par manifeste.</b> Sur cette machine, un refus
    /// de connexion en boucle locale met deux secondes à revenir — mesuré, et identique sur un port
    /// témoin libre, donc c'est un filtre réseau et non le générateur. Les quatre outils de
    /// génération ajoutaient ainsi huit secondes à <em>chaque</em> question posée à l'assistant,
    /// générateur éteint, avant même que le modèle ne commence à réfléchir.
    ///
    /// <para>
    /// Une volée, une sonde. Le choix d'une réponse partagée plutôt que d'une mémoire à durée de
    /// vie est délibéré : une mémoire aurait pu répondre « éteint » sur un générateur que
    /// l'utilisateur venait d'allumer, et ce mensonge-là ne se voit pas — il se lit comme une liste
    /// de modèles périmée.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PluginManifest> Resoudre(
        IEnumerable<PluginManifest> manifestes,
        Action<string>? journal)
        => Resoudre(
            manifestes,
            () => ComfyServer.Repond() ? Catalogue.Demander(Port, journal) : null,
            journal);

    /// <summary>La même volée, avec le générateur fourni — c'est par là que l'épreuve compte.</summary>
    /// <param name="manifestes">Tout ce que l'on s'apprête à déclarer d'un coup.</param>
    /// <param name="generateur">Rend le catalogue de la machine, ou rien si elle ne répond pas.</param>
    /// <param name="journal">Reçoit ce qui a été résolu, et ce qui ne l'a pas été.</param>
    /// <remarks>
    /// Le paramètre existe pour une raison précise : la faute que l'on corrige ici — sonder une
    /// fois par manifeste — est invisible au résultat. Les manifestes rendus étaient corrects ; ce
    /// qui était faux, c'était le nombre d'appels. Sans une couture pour les compter, la régression
    /// reviendrait sans rien casser, et se paierait de nouveau en secondes d'attente.
    /// </remarks>
    public static IReadOnlyList<PluginManifest> Resoudre(
        IEnumerable<PluginManifest> manifestes,
        Func<Catalogue?> generateur,
        Action<string>? journal)
    {
        var tous = manifestes.ToList();

        // La grande majorité des outils n'a aucun choix dynamique. Si aucun de la volée n'en a, la
        // machine n'a pas à être dérangée du tout.
        if (!tous.Exists(m => m.Content.Exists(c => c.From.Length > 0)))
        {
            return tous;
        }

        var catalogue = generateur();

        if (catalogue is null)
        {
            journal?.Invoke(
                "options dynamiques : le générateur ne répond pas, "
                + "les valeurs des manifestes sont conservées.");

            return tous;
        }

        return [.. tous.Select(m => Resoudre(m, catalogue.Options, journal))];
    }

    /// <summary>
    /// Rend le manifeste avec ses choix dynamiques résolus, ou tel quel s'il n'en a pas.
    /// </summary>
    /// <param name="manifeste">Ce qui a été lu sur le disque.</param>
    /// <param name="journal">Reçoit ce qui a été résolu, et ce qui ne l'a pas été.</param>
    /// <remarks>
    /// <b>Le générateur n'est jamais démarré pour cela.</b> Ouvrir un panneau ne doit pas coûter les
    /// deux minutes d'un démarrage de serveur, et surtout pas quand l'utilisateur venait seulement
    /// regarder. S'il ne répond pas déjà, on garde ce que le manifeste proposait : un panneau qui
    /// s'ouvre périmé vaut mieux qu'un panneau qui met deux minutes, et infiniment mieux qu'un
    /// panneau vide.
    /// </remarks>
    public static PluginManifest Resoudre(PluginManifest manifeste, Action<string>? journal)
    {
        if (!manifeste.Content.Exists(c => c.From.Length > 0))
        {
            return manifeste;
        }

        var catalogue = ComfyServer.Repond() ? Catalogue.Demander(Port, journal) : null;

        if (catalogue is null)
        {
            journal?.Invoke(
                $"options de « {manifeste.Id} » : le générateur ne répond pas, "
                + "les valeurs du manifeste sont conservées.");

            return manifeste;
        }

        return Resoudre(manifeste, catalogue.Options, journal);
    }

    /// <summary>
    /// La même chose, avec une source de valeurs quelconque.
    /// </summary>
    /// <remarks>
    /// Séparé pour que la règle — quelles options, et quelle valeur par défaut quand l'ancienne a
    /// disparu — soit éprouvable sans générateur qui tourne. C'est la partie qui décide, et c'est
    /// celle qui doit être sûre.
    /// </remarks>
    public static PluginManifest Resoudre(
        PluginManifest manifeste,
        Func<string, IReadOnlyList<string>> source,
        Action<string>? journal)
    {
        // La grande majorité des outils n'a aucun choix dynamique : les recopier tous à chaque
        // ouverture de panneau et à chaque question posée à l'assistant serait du travail pour rien.
        if (!manifeste.Content.Exists(c => c.From.Length > 0))
        {
            return manifeste;
        }

        var copie = Copier(manifeste);

        foreach (var champ in copie.Content)
        {
            if (champ.From.Length == 0)
            {
                continue;
            }

            var vivantes = source(champ.From);

            if (vivantes.Count == 0)
            {
                // Le nœud a disparu, ou n'a jamais existé sur cette installation. Le repli du
                // manifeste est conservé et le fait est dit : c'est une dérive à corriger, pas une
                // situation normale.
                journal?.Invoke(
                    $"options de « {champ.Id} » : {champ.From} ne rend rien sur cette machine.");

                continue;
            }

            champ.Options = [.. vivantes];

            if (champ.Value.Length > 0 && vivantes.Contains(champ.Value, StringComparer.Ordinal))
            {
                continue;
            }

            // La valeur par défaut n'est plus installée : elle glisse sur ce qui l'est.
            //
            // C'est tout l'intérêt du mécanisme. Sans cela, l'outil s'ouvrirait sur un modèle absent
            // et échouerait au clic, en nommant un fichier que l'utilisateur n'a jamais choisi. Ici
            // il s'ouvre sur ce qui existe, et le remplacement est écrit dans le journal pour qui
            // s'étonnerait que le réglage ait changé tout seul.
            if (champ.Value.Length > 0)
            {
                journal?.Invoke(
                    $"options de « {champ.Id} » : « {champ.Value} » n'est plus installé, "
                    + $"remplacé par « {vivantes[0]} ».");
            }

            champ.Value = vivantes[0];
        }

        return copie;
    }

    /// <summary>Une copie que l'on peut remplir sans toucher au manifeste partagé.</summary>
    private static PluginManifest Copier(PluginManifest manifeste)
    {
        var copie = new PluginManifest
        {
            Id = manifeste.Id,
            Name = manifeste.Name,
            Category = manifeste.Category,
            Version = manifeste.Version,
            Author = manifeste.Author,
            Licence = manifeste.Licence,
            Source = manifeste.Source,
            Revertible = manifeste.Revertible,
            Entry = manifeste.Entry,
            Glyph = manifeste.Glyph,
            Icon = manifeste.Icon,
            Hint = manifeste.Hint,
            Surface = manifeste.Surface,
            Does = manifeste.Does,
            Target = manifeste.Target,
            Enabled = manifeste.Enabled,
            Directory = manifeste.Directory,
            Remembers = [.. manifeste.Remembers],
        };

        foreach (var champ in manifeste.Content)
        {
            copie.Content.Add(new PluginContentItem
            {
                Kind = champ.Kind,
                Id = champ.Id,
                Label = champ.Label,
                Value = champ.Value,
                Hint = champ.Hint,
                Min = champ.Min,
                Max = champ.Max,
                Options = [.. champ.Options],
                From = champ.From,
                Does = champ.Does,
                Target = champ.Target,
            });
        }

        return copie;
    }
}
