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
        // Ce qui ne peut pas repondre n'est pas propose.
        //
        // Les deux recherches distantes dependent d'une instance configuree dans les reglages, et
        // c'est un fait sur la machine, pas sur le modele : aucune consigne ne rattrape une
        // capacite declaree qui echoue a chaque appel. Mesure dans la session du 7 septembre 2026 —
        // demande « cherche sur le web », recherche_web refusee, recherche_documents refusee,
        // recherche_fichiers hors sujet, contexte plein, main rendue.
        //
        // Les retirer rend deux tours et, ce qui compte davantage ici, la place que leur
        // declaration occupait dans un contexte qui saturait.
        var reglages = SearchPolicyStore.Load(journal);

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

            .. reglages.Web.IsUsable ? new[] { new AssistantLocal.Capacite(
                "recherche_web",
                "Cherche sur le web via l'instance configuree dans les reglages. Rend au plus "
                + MaximumResultats + " resultats, chacun avec [web], son titre, son adresse et un "
                + "extrait. Utilise-la quand la reponse n'est pas sur la machine. Si la recherche "
                + "web n'est pas configuree ou a ete desactivee, le dit clairement : ne conclus "
                + "alors pas que le sujet n'existe pas.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher sur le web.", [])],
                args => Web(args.GetValueOrDefault("termes") ?? "", journal)) } : [],

            .. reglages.Docs.IsUsable ? new[] { new AssistantLocal.Capacite(
                "recherche_documents",
                "Cherche dans le corpus documentaire indexe (Meilisearch), par le CONTENU des "
                + "documents et non par leur nom. Rend au plus " + MaximumResultats + " resultats, "
                + "chacun avec [document], son titre, son chemin et l'extrait qui correspond. "
                + "A preferer a recherche_fichiers quand tu cherches ce qu'un document DIT plutot "
                + "que comment il s'appelle.",
                [new AssistantLocal.Parametre("termes", "Les mots a chercher dans le contenu des documents.", [])],
                args => Documents(args.GetValueOrDefault("termes") ?? "", journal)) } : [],

            new AssistantLocal.Capacite(
                "ouvrir",
                "Ouvre un document, un dossier ou une adresse web, par son chemin complet — celui "
                + "que rend recherche_fichiers. C'est ce qui te permet d'AGIR sur ce que tu as "
                + "trouve au lieu de seulement l'annoncer. "
                + "Ouvre aussi une [application] : c'est ce qu'on attend de toi quand on te demande "
                + "d'ouvrir un logiciel. "
                + "REFUSE en revanche ce qui installe ou execute du code — .msi, .bat, .cmd, .ps1, "
                + "et les .exe dont le nom annonce un installeur : ceux-la passent par "
                + "proposer_choix. "
                + "Si le chemin n'existe pas, le dit au lieu de faire semblant.",
                [new AssistantLocal.Parametre("chemin", "Le chemin complet du document ou du dossier, ou une adresse http/https.", [])],
                args => Ouvrir(args.GetValueOrDefault("chemin") ?? "", journal)),
        ];
    }

    /// <summary>Ce qui installe ou execute du code arbitraire, et qu'on n'ouvre donc pas.</summary>
    /// <remarks>
    /// <b>Ce qui n'y est pas, et pourquoi.</b> Le <c>.exe</c> ordinaire en est absent. La premiere
    /// version refusait toute extension executable, et c'etait une impasse : l'utilisateur
    /// demandait « ouvre LibreOffice », passait par proposer_choix, acceptait le plan — et le verbe
    /// refusait encore, parce que le fichier finissait par <c>.exe</c>. Le garde ne pouvait pas
    /// savoir que l'accord avait ete donne, donc il bloquait pour toujours ; l'assistant a boucle
    /// puis renonce.
    ///
    /// <para>Lancer une application que l'utilisateur a nommee n'est pas installer un logiciel. Le
    /// contrat du produit le dit dans l'autre sens : « aucun verbe qui n'existe pas deja pour
    /// l'utilisateur » — or il ouvre cette application d'un double-clic. Ce qui reste refuse, c'est
    /// ce qui MODIFIE la machine plutot que de s'y executer : les installeurs, et les scripts, qui
    /// sont du code arbitraire sous une extension anodine.</para>
    /// </remarks>
    private static readonly string[] ExtensionsRefusees =
        [".msi", ".msix", ".appx", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".reg"];

    /// <summary>Un executable dont le nom annonce qu'il installe.</summary>
    /// <remarks>
    /// Un installeur se distingue mal d'une application par son extension — les deux sont des
    /// <c>.exe</c> — mais tres bien par son nom, que celui qui le publie choisit pour etre lu.
    /// C'est faillible dans les deux sens, et c'est assumé : le cas qui compte est celui d'un
    /// installeur trouve par une recherche, comme le LibreOffice_26.2.5_Win_x86-64.msi des
    /// Telechargements, et non d'un piege qu'on chercherait a dejouer.
    /// </remarks>
    private static bool RessembleAUnInstalleur(string nomFichier)
    {
        var nom = nomFichier.ToLowerInvariant();

        return nom.Contains("setup", StringComparison.Ordinal)
            || nom.Contains("install", StringComparison.Ordinal)
            || nom.Contains("uninstall", StringComparison.Ordinal)
            || nom.Contains("updater", StringComparison.Ordinal);
    }

    private static string Ouvrir(string chemin, Action<string>? journal)
    {
        var cible = chemin.Trim().Trim('"');

        if (cible.Length == 0)
        {
            return "ouvrir : donne un chemin.";
        }

        var estAdresseWeb =
            cible.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || cible.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (!estAdresseWeb)
        {
            var extension = System.IO.Path.GetExtension(cible);
            var nomFichier = System.IO.Path.GetFileName(cible);

            if (ExtensionsRefusees.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return $"Refuse : « {nomFichier} » installe ou execute du code, et ca appartient a "
                    + "l'utilisateur. Propose-le-lui avec proposer_choix en disant ce que ca "
                    + "ferait, et laisse-le lancer lui-meme.";
            }

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && RessembleAUnInstalleur(nomFichier))
            {
                return $"Refuse : « {nomFichier} » a tout d'un installeur, et installer un logiciel "
                    + "appartient a l'utilisateur. Propose-le-lui avec proposer_choix en disant ce "
                    + "que ca ferait.";
            }

            if (!System.IO.File.Exists(cible) && !System.IO.Directory.Exists(cible))
            {
                return $"Rien a ouvrir : « {cible} » n'existe pas.";
            }
        }

        try
        {
            if (estAdresseWeb && SenSÉ.Mcp.Bus.ChromiumEmbarque.Exe() is { } chromium)
            {
                var departWeb = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = chromium,
                    UseShellExecute = false,
                };
                departWeb.ArgumentList.Add(cible);
                System.Diagnostics.Process.Start(departWeb)?.Dispose();

                return $"Ouvert dans le navigateur du projet : {cible}";
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(cible) { UseShellExecute = true })?.Dispose();

            return $"Ouvert : {cible}";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"ouvrir « {cible} » : {exception.GetType().Name}: {exception.Message}");
            return $"Impossible d'ouvrir « {cible} » : {exception.Message}";
        }
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

        // Normalement jamais atteint, puisque la capacite n'est plus declaree quand elle ne peut
        // pas repondre. Gardee pour le cas ou les reglages changent au milieu d'une session, et
        // formulee pour que le modele s'arrete et le dise au lieu d'essayer autre chose.
        if (!reglages.IsUsable)
        {
            return "La recherche web ne peut pas repondre ici : aucune instance n'est configuree "
                + "dans les reglages. ARRETE-TOI et dis-le a l'utilisateur — n'essaie pas de la "
                + "remplacer par une autre recherche, et ne conclus pas que le sujet n'existe pas.";
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
