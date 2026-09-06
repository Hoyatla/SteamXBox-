using System.Text;
using SenSÉ.Desktop.Search;
using SenSÉ.Tools.Assistant;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Assistant;

/// <summary>
/// Les Capacités de recherche : les fichiers et applications de la machine, le courrier, le web,
/// le corpus documentaire.
/// </summary>
/// <remarks>
/// <b>Les mêmes sources que la barre double-Shift, pas une deuxième recherche.</b> Tout existait
/// déjà — <see cref="SearchIndexBuilder"/>, <see cref="MailIndexBuilder"/>,
/// <see cref="SearxngClient"/>, <see cref="MeilisearchClient"/> — assemblé par
/// <see cref="SearchLauncherWindow"/>. Il ne manquait que d'y donner accès à l'Assistant. Les index
/// sont partagés par <see cref="IndexRecherche"/> : un seul parcours des disques, deux lecteurs.
///
/// <para><b>Des résultats, pas une fenêtre.</b> On aurait pu faire taper l'Assistant dans la barre
/// et lui faire lire l'écran. Il en aurait tiré des pixels, à redécouvrir à chaque fois. Ici il
/// reçoit des lignes typées : la nature de chaque résultat est écrite dessus, et c'est elle qui lui
/// permet de choisir entre ouvrir un fichier, citer un message ou suivre un lien.</para>
///
/// <para><b>Ce qui ne quitte pas la machine.</b> Fichiers et courrier sont lus localement, sans
/// qu'aucune requête ne parte. Le web et le corpus interrogent une instance que l'utilisateur a
/// nommée dans ses réglages, et disent clairement quand elle n'est pas configurée plutôt que de
/// rendre une liste vide — un « rien trouvé » qui veut dire « rien demandé » est un mensonge que
/// l'Assistant n'a aucun moyen de percer.</para>
/// </remarks>
public static class AssistantRecherche
{
    /// <summary>Au-delà, l'Assistant lit sans rien décider, et le contexte se remplit pour rien.</summary>
    private const int MaximumResultats = 12;

    /// <summary>Un extrait plus long ne se lit pas mieux, et coûte un fil qui déborde.</summary>
    private const int LongueurExtrait = 240;

    public static IReadOnlyList<AssistantLocal.Capacite> Creer(Action<string>? journal)
    {
        return
        [
            new AssistantLocal.Capacite(
                "recherche_fichiers",
                "Cherche un fichier, un dossier, une application ou un lecteur sur cette machine, "
                + "dans l'index de la barre de recherche. Rend au plus " + MaximumResultats
                + " lignes, chacune precedee de sa NATURE entre crochets — [fichier], [dossier], "
                + "[application], [lecteur] — puis son nom et son chemin complet. Sers-toi de la "
                + "nature pour choisir quoi en faire : un [fichier] se donne a un outil par son "
                + "chemin, une [application] se lance. La premiere recherche peut prendre quelques "
                + "secondes, le temps de parcourir les disques une fois pour la session.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher. Un fragment de nom suffit.", [])],
                args => Fichiers(args.GetValueOrDefault("termes") ?? "", journal)),

            new AssistantLocal.Capacite(
                "recherche_courrier",
                "Cherche dans le courrier indexe sur cette machine. Rend au plus " + MaximumResultats
                + " messages, chacun avec [courriel], son sujet, son expediteur, sa date et un "
                + "extrait. Rien ne quitte la machine. Si aucun index n'existe, le dit au lieu de "
                + "rendre une liste vide.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher dans les messages.", [])],
                args => Courrier(args.GetValueOrDefault("termes") ?? "", journal)),

            new AssistantLocal.Capacite(
                "recherche_web",
                "Cherche sur le web via l'instance configuree dans les reglages. Rend au plus "
                + MaximumResultats + " resultats, chacun avec [web], son titre, son adresse et un "
                + "extrait. Utilise-la quand la reponse n'est pas sur la machine. Si la recherche "
                + "web n'est pas configuree ou a ete desactivee, le dit clairement : ne conclus "
                + "alors pas que le sujet n'existe pas.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher sur le web.", [])],
                args => Web(args.GetValueOrDefault("termes") ?? "", journal)),

            new AssistantLocal.Capacite(
                "recherche_documents",
                "Cherche dans le corpus documentaire indexe (Meilisearch), par le CONTENU des "
                + "documents et non par leur nom. Rend au plus " + MaximumResultats + " resultats, "
                + "chacun avec [document], son titre, son chemin et l'extrait qui correspond. "
                + "A preferer a recherche_fichiers quand tu cherches ce qu'un document DIT plutot "
                + "que comment il s'appelle.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher dans le contenu des documents.", [])],
                args => Documents(args.GetValueOrDefault("termes") ?? "", journal)),
        ];
    }

    /// <summary>Le mot que l'Assistant lira devant chaque ligne.</summary>
    private static string Nature(SearchItemKind kind) => kind switch
    {
        SearchItemKind.File => "fichier",
        SearchItemKind.Folder => "dossier",
        SearchItemKind.Application => "application",
        SearchItemKind.Drive => "lecteur",
        SearchItemKind.Web => "web",
        SearchItemKind.Document => "document",
        SearchItemKind.Mail => "courriel",
        _ => "resultat",
    };

    private static string Fichiers(string termes, Action<string>? journal)
    {
        if (string.IsNullOrWhiteSpace(termes))
        {
            return "recherche_fichiers : donne des termes a chercher.";
        }

        // Bloquant, et c'est voulu : mieux vaut attendre le premier parcours que repondre
        // « rien trouve » sur un index vide, ce que rien ne distinguerait d'un vrai echec.
        var fichiers = IndexRecherche.AssurerFichiers(journal);
        var volumes = IndexRecherche.Volumes;

        if (fichiers.Count == 0 && volumes.Count == 0)
        {
            return "L'index des fichiers est vide : rien n'a pu etre parcouru sur cette machine.";
        }

        var brut = termes.Trim();
        var pathMode = PathQuery.LooksLikeAPath(brut);
        var mots = SearchRanking.Terms(pathMode ? PathQuery.Expand(brut) : brut);

        if (mots.Count == 0)
        {
            return "recherche_fichiers : donne des termes a chercher.";
        }

        var maintenant = DateTime.UtcNow;

        var trouves = fichiers.Concat(volumes)
            .Select(item => (item, score: SearchRanking.Score(item, mots, pathMode, maintenant)))
            .Where(paire => paire.score is not null)
            .OrderByDescending(paire => paire.score!.Value)
            .Take(MaximumResultats)
            .ToList();

        if (trouves.Count == 0)
        {
            return $"Aucun resultat pour « {brut} » parmi {fichiers.Count + volumes.Count} entrees indexees.";
        }

        var texte = new StringBuilder();
        texte.Append(trouves.Count).Append(" resultat(s) pour « ").Append(brut).AppendLine(" » :");

        foreach (var (item, _) in trouves)
        {
            texte.Append("[").Append(Nature(item.Kind)).Append("] ")
                 .Append(item.Name).Append(" — ").AppendLine(item.Path);
        }

        return texte.ToString().TrimEnd();
    }

    private static string Courrier(string termes, Action<string>? journal)
    {
        if (string.IsNullOrWhiteSpace(termes))
        {
            return "recherche_courrier : donne des termes a chercher.";
        }

        var index = IndexRecherche.AssurerCourrier(journal);

        if (index.Count == 0)
        {
            return "Aucun courrier indexe sur cette machine : il n'y a rien a chercher, "
                + "ce qui ne veut pas dire que le message n'existe pas.";
        }

        var trouves = index.Search(termes.Trim()).Take(MaximumResultats).ToList();

        if (trouves.Count == 0)
        {
            return $"Aucun message pour « {termes.Trim()} » parmi {index.Count} messages indexes.";
        }

        var texte = new StringBuilder();
        texte.Append(trouves.Count).Append(" message(s) sur ").Append(index.Count).AppendLine(" indexes :");

        foreach (var message in trouves)
        {
            texte.Append("[courriel] ").Append(message.Subject)
                 .Append(" — de ").Append(message.From)
                 .Append(" — ").AppendLine(message.Date);

            if (message.Text.Length > 0)
            {
                texte.Append("    ").AppendLine(Extrait(message.Text));
            }
        }

        return texte.ToString().TrimEnd();
    }

    private static string Web(string termes, Action<string>? journal)
    {
        if (string.IsNullOrWhiteSpace(termes))
        {
            return "recherche_web : donne des termes a chercher.";
        }

        var reglages = SearchPolicyStore.Load(journal).Web;

        if (reglages.Effective.Provider == WebSearchProvider.Disabled)
        {
            return "La recherche web est desactivee dans les reglages. Ne conclus pas que le sujet "
                + "n'existe pas : la question n'a pas ete posee.";
        }

        try
        {
            var reponse = SearxngClient
                .AskAsync(reglages.Effective, termes.Trim(), CancellationToken.None)
                .GetAwaiter().GetResult();

            if (reponse.Problem.Length > 0)
            {
                return $"Recherche web impossible : {reponse.Problem}";
            }

            if (reponse.Results.Count == 0)
            {
                return $"Aucun resultat web pour « {termes.Trim()} ».";
            }

            var texte = new StringBuilder();
            var gardes = reponse.Results.Take(MaximumResultats).ToList();
            texte.Append(gardes.Count).Append(" resultat(s) web pour « ").Append(termes.Trim()).AppendLine(" » :");

            foreach (var resultat in gardes)
            {
                texte.Append("[web] ").Append(resultat.Title).Append(" — ").AppendLine(resultat.Url);

                if (resultat.Snippet.Length > 0)
                {
                    texte.Append("    ").AppendLine(Extrait(resultat.Snippet));
                }
            }

            return texte.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            journal?.Invoke($"recherche_web : {exception.GetType().Name}: {exception.Message}");
            return $"Recherche web impossible : {exception.Message}";
        }
    }

    private static string Documents(string termes, Action<string>? journal)
    {
        if (string.IsNullOrWhiteSpace(termes))
        {
            return "recherche_documents : donne des termes a chercher.";
        }

        var reglages = SearchPolicyStore.Load(journal).Docs;

        if (!reglages.IsUsable)
        {
            return "La recherche documentaire n'est pas configuree dans les reglages. "
                + "Le corpus n'a pas ete interroge.";
        }

        try
        {
            var reponse = MeilisearchClient
                .AskAsync(reglages.Effective, termes.Trim(), CancellationToken.None)
                .GetAwaiter().GetResult();

            if (reponse.Problem.Length > 0)
            {
                return $"Recherche documentaire impossible : {reponse.Problem}";
            }

            if (reponse.Results.Count == 0)
            {
                return $"Aucun document pour « {termes.Trim()} ».";
            }

            var texte = new StringBuilder();
            var gardes = reponse.Results.Take(MaximumResultats).ToList();
            texte.Append(gardes.Count).Append(" document(s) pour « ").Append(termes.Trim()).AppendLine(" » :");

            foreach (var resultat in gardes)
            {
                texte.Append("[document] ").Append(resultat.Title).Append(" — ").AppendLine(resultat.Path);

                if (resultat.Snippet.Length > 0)
                {
                    texte.Append("    ").AppendLine(Extrait(resultat.Snippet));
                }
            }

            return texte.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            journal?.Invoke($"recherche_documents : {exception.GetType().Name}: {exception.Message}");
            return $"Recherche documentaire impossible : {exception.Message}";
        }
    }

    /// <summary>Un extrait ramené à une ligne et à une longueur lisible.</summary>
    private static string Extrait(string texte)
    {
        var propre = texte.ReplaceLineEndings(" ").Trim();

        while (propre.Contains("  ", StringComparison.Ordinal))
        {
            propre = propre.Replace("  ", " ", StringComparison.Ordinal);
        }

        return propre.Length <= LongueurExtrait ? propre : propre[..LongueurExtrait] + "…";
    }
}
