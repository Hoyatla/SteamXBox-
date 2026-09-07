using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Le modèle de langage local, servi depuis le dossier du produit.
/// </summary>
/// <remarks>
/// <b>Rien n'est installé sur la machine.</b> Le serveur et le modèle vivent dans
/// <c>Outils\llama.cpp</c> et <c>Outils\Modeles</c> : le produit se déplace avec son assistant, et
/// l'environnement Windows de l'utilisateur n'est pas touché. C'est la même règle que pour les
/// moteurs d'agrandissement.
///
/// <para>
/// <b>Vulkan plutôt que CUDA</b>, et c'est un arbitrage assumé : la version CUDA réclame 513 Mo et
/// ne sert que les cartes NVIDIA, celle-ci pèse 102 Mo et fonctionne aussi sur AMD et Intel. Le
/// produit est destiné à des machines qu'on ne choisit pas. Les deux moteurs d'agrandissement sont
/// déjà en Vulkan, la pile reste la même partout.
/// </para>
///
/// <para>
/// <b>Jamais la carte graphique. Jamais.</b> Le modèle est chargé sur le processeur, en mémoire
/// vive, quoi qu'il arrive — <c>-ngl 0</c>. Une version précédente choisissait selon ce qui
/// occupait la carte, et ce conditionnel a produit exactement ce qu'on pouvait craindre : elle
/// déchargeait le générateur que l'utilisateur venait d'ouvrir, le relançait, puis le redéchargeait
/// à la question suivante. Une invariante simple se raisonne encore dans cinq ans ; une politique
/// conditionnelle devient impossible à tenir dès que le produit s'étoffe.
/// </para>
///
/// <para>
/// Le prix est la vitesse, et il est assumé : la carte appartient aux modèles génératifs, qui n'ont
/// pas d'autre endroit où aller. L'assistant, lui, a trente-quatre gigaoctets de mémoire vive et
/// vingt-huit cœurs à sa disposition. Si les réponses deviennent trop lentes, la réponse est un
/// modèle plus petit — l'installeur en propose déjà un de 2,3 Go — et non un retour à la carte.
/// </para>
///
/// <para>
/// Le drapeau <c>--jinja</c> n'est pas décoratif : sans lui, le serveur ignore le gabarit de
/// conversation du modèle et l'appel d'outils ne fonctionne pas. Or c'est tout ce qui distingue un
/// assistant d'une boîte à dialogue.
/// </para>
/// </remarks>
public static class ServeurModele
{
    private const int Port = 8081;

    /// <summary>La clé de cette ressource au registre.</summary>
    private const string Ressource = "assistant-modele";

    /// <summary>
    /// Chargement de plusieurs gigaoctets depuis le disque : la première fois est la plus longue.
    /// </summary>
    /// <remarks>
    /// Mesuré sur la machine de développement : 9 secondes quand le fichier est déjà en cache
    /// disque, mais <b>plus de cinq minutes</b> à froid pour 5,4 Go. La borne était à trois
    /// minutes et le produit déclarait forfait pendant que le chargement se poursuivait — le
    /// symptôme trompe, on croit à un échec là où il n'y a qu'une attente.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(12);

    private static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils");

    private static string Programme => Path.Combine(Racine, "llama.cpp", "llama-server.exe");

    private static string DossierModeles => Path.Combine(Racine, "Modeles");

    /// <summary>L'adresse du serveur, compatible avec le dialecte OpenAI.</summary>
    public static string Adresse => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// La place de travail du modèle, en jetons.
    /// </summary>
    /// <remarks>
    /// Déclarée ici parce que c'est ici qu'elle est imposée au serveur, et lue par l'assistant qui
    /// doit savoir quand il en approche. Les deux se sont déjà contredits : l'assistant élaguait sa
    /// conversation pour tenir dans huit mille jetons, commentaire à l'appui, des semaines après
    /// que le serveur eut été porté à trente-deux mille. Il jetait donc ce qu'il venait
    /// d'apprendre pour faire de la place dont il disposait déjà.
    ///
    /// <para>
    /// <b>Soixante-cinq mille, et cela coûte moins que trente-deux mille en coûtait.</b> Mesuré le
    /// 7 septembre 2026 sur cette machine, serveur lancé à vide : 32 768 jetons en f16 tenaient
    /// 3,39 Gio engagés et 4,66 Gio résidents ; 65 536 avec le cache KV en <c>q8_0</c> et
    /// flash-attention en tiennent 2,10 et 2,80. Deux fois la place de travail pour 1,3 Gio de
    /// moins — voir les drapeaux dans <see cref="Demarrer"/>, qui sont ce qui rend l'échange
    /// possible.
    /// </para>
    ///
    /// <para>
    /// Le modèle, lui, en déclare 262 144. Ce n'est pas lui qui borne : à 808 jetons par seconde
    /// mesurés en lecture d'invite, une invite pleine de 200 000 jetons demanderait quatre minutes
    /// avant le premier mot écrit. Sur processeur, le plafond utile est celui du temps, pas celui
    /// du modèle.
    /// </para>
    /// </remarks>
    public const int Contexte = 65536;

    /// <summary>Le modèle retenu : le premier <c>.gguf</c> qui n'est pas un projecteur d'images.</summary>
    /// <remarks>
    /// Les fichiers <c>mmproj-</c> accompagnent un modèle pour lui donner la vue ; chargés seuls,
    /// ils ne répondent à rien. Les écarter ici évite une erreur incompréhensible plus loin.
    ///
    /// <para>
    /// <b>Un seul modèle vit à la racine du dossier ; les autres attendent dans un sous-dossier.</b>
    /// La recherche ne descend pas, donc <c>Modeles\autres\</c> est ignoré sans qu'aucune règle ne
    /// l'énonce — c'est là qu'on range celui dont on ne se sert plus, pour le reprendre en le
    /// remontant. Départager deux modèles à la racine par l'ordre alphabétique reviendrait à faire
    /// dépendre le choix de la ponctuation d'un nom de fichier : entre <c>Qwen3.5-4B</c> et
    /// <c>Qwen3VL-8B</c>, c'est le point qui l'emporte sur la lettre, et personne ne le devinerait.
    /// </para>
    /// </remarks>
    public static string? Modele()
    {
        if (!Directory.Exists(DossierModeles))
        {
            return null;
        }

        var fichiers = Directory.GetFiles(DossierModeles, "*.gguf");
        Array.Sort(fichiers, StringComparer.OrdinalIgnoreCase);

        return Array.Find(
            fichiers,
            f => !Path.GetFileName(f).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Le projecteur qui donne la vue au modèle, s'il y en a un à côté.
    /// </summary>
    /// <remarks>
    /// <b>Un fichier <c>mmproj-</c> n'est pas un modèle, c'est ce qui lui apprend à regarder.</b>
    /// Chargé seul il ne répond à rien, d'où son exclusion dans <see cref="Modele"/> ; passé au
    /// serveur à côté du modèle, il ouvre l'entrée d'images.
    ///
    /// <para>
    /// Il était téléchargé et inutilisé : le produit posait les deux fichiers dans
    /// <c>Outils\Modeles</c> puis n'en donnait qu'un au serveur. Six cent trente mégaoctets sur le
    /// disque, et un assistant aveugle devant un outil dont tout le propos est de produire des
    /// images.
    /// </para>
    ///
    /// <para>
    /// Sans projecteur, rien n'est passé et le modèle reste ce qu'il était : la vue est un supplément
    /// quand le fichier est là, jamais une exigence.
    /// </para>
    /// </remarks>
    public static string? Projecteur()
    {
        if (!Directory.Exists(DossierModeles) || Modele() is not { } modele)
        {
            return null;
        }

        var fichiers = Directory.GetFiles(DossierModeles, "mmproj*.gguf");
        Array.Sort(fichiers, StringComparer.OrdinalIgnoreCase);

        return Apparier(modele, fichiers);
    }

    /// <summary>
    /// Choisit, parmi des projecteurs, celui qui accompagne ce modèle.
    /// </summary>
    /// <param name="modele">Le chemin du modèle retenu.</param>
    /// <param name="projecteurs">Les <c>mmproj*.gguf</c> présents.</param>
    /// <returns>Le projecteur apparié, ou <c>null</c> si aucun ne va avec.</returns>
    /// <remarks>
    /// <b>Le projecteur est apparié au modèle, jamais pris au hasard de l'ordre alphabétique.</b>
    /// Les deux étaient choisis séparément : le premier <c>.gguf</c> d'un côté, le premier
    /// <c>mmproj-*.gguf</c> de l'autre. Tant qu'un seul modèle vivait dans le dossier, cela tombait
    /// juste par accident. Dès qu'il y en a deux, l'ordre alphabétique peut donner le corps de l'un
    /// et les yeux de l'autre — le serveur démarre, et ce qu'il croit voir dans une image n'a plus
    /// de rapport avec elle. Une panne muette, qui ne ressemble pas à une erreur de configuration.
    ///
    /// <para>
    /// L'appariement se fait sur le plus long début commun, et non sur une règle de nommage : les
    /// noms varient d'un éditeur à l'autre — <c>Qwen3.5-4B-Q4_K_M</c> face à
    /// <c>mmproj-Qwen3.5-4B-F16</c>, où seule la quantification diffère et pas au même endroit.
    /// Découper les suffixes aurait demandé de connaître la liste des quantifications, qui s'allonge
    /// à chaque version de llama.cpp.
    /// </para>
    ///
    /// <para>
    /// Quatre caractères au minimum : sans ce plancher, un projecteur sans le moindre rapport
    /// gagnerait par défaut dès qu'il est seul de son espèce. Pas de vue vaut mieux que la mauvaise,
    /// et l'invariante tient — la vue est un supplément quand le fichier est là, jamais une exigence.
    /// </para>
    /// </remarks>
    public static string? Apparier(string modele, IReadOnlyList<string> projecteurs)
    {
        var attendu = Path.GetFileNameWithoutExtension(modele);

        string? meilleur = null;
        var meilleure = 0;

        foreach (var fichier in projecteurs)
        {
            var nom = Path.GetFileNameWithoutExtension(fichier);

            nom = nom.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)
                ? nom["mmproj".Length..].TrimStart('-', '_', '.')
                : nom;

            var commun = Commun(attendu, nom);

            if (commun > meilleure)
            {
                meilleure = commun;
                meilleur = fichier;
            }
        }

        return meilleure >= 4 ? meilleur : null;
    }

    /// <summary>Combien de caractères de tête deux noms partagent.</summary>
    private static int Commun(string un, string deux)
    {
        var jusqua = Math.Min(un.Length, deux.Length);
        var compte = 0;

        while (compte < jusqua
               && char.ToUpperInvariant(un[compte]) == char.ToUpperInvariant(deux[compte]))
        {
            compte++;
        }

        return compte;
    }

    /// <summary>Ce que le serveur est en train de faire.</summary>
    public enum Etat
    {
        /// <summary>Personne n'écoute : il faut le lancer.</summary>
        Absent,

        /// <summary>Il écoute, mais le modèle n'est pas encore en mémoire.</summary>
        Charge,

        /// <summary>Prêt à répondre.</summary>
        Pret,
    }

    /// <summary>
    /// L'état du serveur, et pas seulement « prêt ou non ».
    /// </summary>
    /// <remarks>
    /// La distinction compte : un serveur qui charge répond <c>503</c> avec « Loading model ».
    /// Confondre cette réponse avec l'absence de serveur menait à en lancer un second par-dessus le
    /// premier — deux processus se disputant la même mémoire vidéo pour charger le même modèle.
    /// </remarks>
    public static Etat Ou()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var reponse = client.GetAsync(Adresse + "/health").GetAwaiter().GetResult();
            var lu = reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (reponse.IsSuccessStatusCode && lu.Contains("\"ok\"", StringComparison.Ordinal))
            {
                return Etat.Pret;
            }

            // Il a répondu quelque chose : quelqu'un écoute bien sur ce port.
            return Etat.Charge;
        }
        catch (HttpRequestException)
        {
            return Etat.Absent;
        }
        catch (TaskCanceledException)
        {
            return Etat.Absent;
        }
    }

    /// <summary>Le serveur répond-il, modèle chargé ?</summary>
    public static bool Pret()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var reponse = client.GetAsync(Adresse + "/health").GetAwaiter().GetResult();

            return reponse.IsSuccessStatusCode
                   && reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("\"ok\"", StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Démarre le serveur s'il dort, et attend qu'il soit réellement prêt.</summary>
    /// <returns>Une phrase à montrer en cas d'échec, ou null si tout va bien.</returns>
    public static string? Demarrer(Action<string>? journal, CancellationToken arret = default)
    {
        var etat = Ou();

        if (etat == Etat.Pret)
        {
            Adopter(journal);

            return null;
        }

        var journalier = Path.Combine(Racine, "llama.cpp", "serveur.log");

        // Un serveur est là et charge encore : on l'attend au lieu d'en lancer un deuxième. Deux
        // processus chargeant le même modèle se disputeraient la mémoire vidéo, et aucun des deux
        // n'irait plus vite.
        if (etat == Etat.Charge)
        {
            journal?.Invoke("Le modèle est déjà en cours de chargement, patience...");

            return Attendre(journalier, journal, arret);
        }

        if (!File.Exists(Programme))
        {
            return $"Le serveur du modèle est absent : {Programme}";
        }

        if (Modele() is not { } modele)
        {
            return $"Aucun modèle dans {DossierModeles}. Un fichier .gguf est attendu.";
        }

        journal?.Invoke(
            $"Démarrage de l'assistant, chargement de {Path.GetFileName(modele)}. "
            + "Compter plusieurs minutes la première fois.");

        // Moitié des cœurs, jamais tous : l'assistant tourne pendant qu'autre chose travaille —
        // un encodage vidéo, un flux de génération — et prendre la machine entière pour répondre à
        // « ouvre la calculatrice » ralentirait le travail qui compte.
        var fils = Math.Max(4, Environment.ProcessorCount / 2)
            .ToString(CultureInfo.InvariantCulture);

        var lance = SenSÉ.Tools.Serveurs.ServeurLocal.Demarrer(
            Programme,
            [
                "-m", modele,
                "--host", "127.0.0.1",
                "--port", Port.ToString(CultureInfo.InvariantCulture),
                "-ngl", "0",
                "-t", fils,

                // Soixante-cinq mille jetons, et un seul emplacement de conversation.
                //
                // Le produit étouffait à huit mille. Mesuré dans une vraie session : la
                // consigne, les carnets ouverts et la déclaration de tous les outils installés
                // pèsent 6 579 jetons — 80 % du contexte avant que le modèle n'écrive un mot. Il
                // restait 1 613 jetons pour la conversation, la réflexion et la réponse, alors que
                // JetonsMaximum en réclame 2 048 pour la seule réponse. Tout ce qu'on prenait pour
                // des faiblesses du modèle en découlait : il se taisait après un outil, perdait le
                // fil de son plan, dépensait un tour à cocher au lieu de travailler.
                //
                // « --parallel 1 » est ce qui rend ces jetons atteignables. Par défaut le serveur
                // ouvre quatre emplacements et « -c » vaut par emplacement : une conversation
                // seule plafonnait à 8 192 pendant que la machine en réservait 32 768. Mesuré sur
                // le 4B : 3,18 Go par défaut, 3,81 Go ici. Six cent trente mégaoctets pour
                // quadrupler ce que l'assistant peut retenir.
                "-c", Contexte.ToString(CultureInfo.InvariantCulture),
                "--parallel", "1",

                // Le cache KV en huit bits, et l'attention qui n'a plus besoin de le matérialiser.
                //
                // Ces deux drapeaux sont ce qui rend les 65 536 jetons abordables : le cache est la
                // seule chose qui grandisse avec le contexte, et le passer de seize à huit bits le
                // divise par deux, pendant que flash-attention supprime les tampons intermédiaires
                // que l'attention classique alloue par tête et par couche.
                //
                // Mesuré à vide sur cette machine, processus complet : 32 768 en f16 tenaient
                // 3,39 Gio engagés et 4,66 Gio résidents ; 65 536 ainsi en tiennent 2,10 et 2,80.
                // Doubler la place de travail rend 1,3 Gio au lieu d'en prendre.
                //
                // Ce qui n'est pas mesuré, et qu'il faut donc dire : l'effet de la quantification du
                // cache sur la qualité des réponses. Elle est réputée quasi sans perte à huit bits
                // — c'est la raison de ne pas être descendu à quatre, où elle ne l'est plus.
                "-fa", "on",
                "-ctk", "q8_0",
                "-ctv", "q8_0",

                // Que la machine reste utilisable pendant qu'il repond.
                //
                // Par defaut les fils du serveur attendent le travail en tournant a vide
                // (« --poll 50 ») et a priorite normale : quatorze fils qui se disputent
                // l'ordonnanceur avec l'interface de l'utilisateur, pour une generation limitee par
                // la bande passante memoire et non par le calcul. Les endormir et les faire passer
                // en dernier ne coute presque rien puisqu'ils n'attendaient rien d'utile.
                //
                // Mesure sur cette machine, un temoin monofil chronometre pendant que le modele
                // ecrit : 2 107 ms machine au repos, 3 171 ms avec le serveur tel qu'il etait,
                // 2 838 ms ainsi. Un tiers de la gene rendu pour 2 % de debit — 12,34 jetons par
                // seconde contre 12,09.
                "--poll", "0",
                "--prio", "-1",

                // Sans --jinja, le serveur ignore le gabarit de conversation du modèle et l'appel
                // d'outils ne fonctionne pas. C'est tout ce qui sépare un assistant d'une boîte à
                // dialogue.
                "--jinja",

                // La vue, si le projecteur est là. Sur le processeur comme le reste : l'invariante
                // « jamais la carte » ne souffre pas d'exception pour une pièce de plus.
                .. (Projecteur() is { } vue ? new[] { "--mmproj", vue, "--no-mmproj-offload" } : []),
            ],
            Path.Combine(Racine, "llama.cpp"),
            journalier,
            environnement: null,
            identite: new Serveurs.ServeurLocal.Identite(
                Ressource,
                "Modèle de langage",
                "assistant",
                Serveurs.Poids.Leger,
                CoutVideoMo: 0,
                Serveurs.ALaFermeture.Tuer));

        if (lance is null)
        {
            return "Le serveur du modèle n'a pas démarré.";
        }

        return Attendre(journalier, journal, arret);
    }

    /// <summary>
    /// Décharge le modèle. À n'appeler qu'à la fermeture de la fenêtre de l'assistant.
    /// </summary>
    /// <remarks>
    /// <b>Le moment est tout le sujet.</b> Le modèle pèse plusieurs gigaoctets de mémoire vive et
    /// met de neuf secondes à cinq minutes à revenir selon l'état du cache disque : le décharger au
    /// mauvais moment ne se rattrape pas. Il l'était trop tôt — l'ouverture du générateur l'évinçait
    /// pour libérer une carte qu'il n'occupe pas, en pleine conversation.
    ///
    /// <para>
    /// La règle est donc simple et tenue à un seul endroit : il vit tant que la fenêtre de
    /// l'assistant est ouverte, et il s'arrête quand elle se ferme. Rien d'autre ne le décharge.
    /// </para>
    /// </remarks>
    public static void Arreter(Action<string>? journal)
    {
        if (Serveurs.Ressources.Arreter(Ressource))
        {
            journal?.Invoke("Modèle de langage déchargé : la fenêtre de l'assistant est fermée.");
        }
    }

    /// <summary>
    /// Inscrit au registre un serveur déjà en route que nous n'avons pas lancé.
    /// </summary>
    /// <remarks>
    /// <b>Un serveur repris n'était connu de personne.</b> Quand il répond déjà, on s'en sert sans
    /// le lancer — donc sans l'inscrire. Il n'apparaissait pas dans la fenêtre Activité, et la
    /// fermeture du produit ne l'arrêtait pas puisqu'elle n'arrête que ce qui est inscrit. Il
    /// survivait donc à la session, et se retrouvait repris à la suivante : la fuite s'entretenait
    /// toute seule, et c'est ainsi qu'un llama-server tournait déjà à l'ouverture du 22 août.
    ///
    /// <para>
    /// Reconnu par le <b>chemin exact</b> de son programme sous <c>Outils</c>, jamais par son nom.
    /// C'est la même règle qu'à l'éviction, et pour la même raison : un <c>llama-server.exe</c>
    /// installé ailleurs sur la machine ne nous appartient pas, et l'adopter reviendrait à le tuer
    /// à notre fermeture.
    /// </para>
    /// </remarks>
    private static void Adopter(Action<string>? journal)
    {
        if (Serveurs.Ressources.Lister().Any(r => r.Id == Ressource))
        {
            return;
        }

        var attendu = Path.Combine(Racine, "llama.cpp", "llama-server.exe");

        foreach (var processus in Process.GetProcesses())
        {
            var notre = false;

            try
            {
                notre = processus.MainModule?.FileName is { Length: > 0 } chemin
                        && string.Equals(attendu, chemin, StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            if (!notre)
            {
                processus.Dispose();

                continue;
            }

            journal?.Invoke("Le modèle de langage tournait déjà : repris et inscrit.");

            Serveurs.Ressources.Inscrire(new Serveurs.Ressource(
                Ressource,
                "Modèle de langage",
                "assistant",
                Serveurs.Poids.Leger,
                CoutVideoMo: 0,
                Serveurs.ALaFermeture.Tuer,
                processus,
                processus.StartTime));

            return;
        }
    }

    /// <summary>Attend que le modèle soit en mémoire.</summary>
    /// <remarks>
    /// L'avancement est annoncé toutes les trente secondes : sans cela, une attente de plusieurs
    /// minutes est indiscernable d'un blocage, et l'utilisateur relance — ce qui aggrave tout.
    /// </remarks>
    private static string? Attendre(string journalier, Action<string>? journal, CancellationToken arret)
    {
        var montre = Stopwatch.StartNew();
        var dit = 0;

        while (montre.Elapsed < Patience)
        {
            Thread.Sleep(1500);

            // Le chargement dure de neuf secondes à plus de cinq minutes. Sans cette sortie, le
            // bouton « Arrêter » restait inerte pendant toute l'attente — au moment exact où
            // l'utilisateur regrette sa demande. Le serveur, lui, continue de charger : il servira
            // à la question suivante plutôt que d'être perdu.
            if (arret.IsCancellationRequested)
            {
                return "Chargement interrompu à votre demande ; il se poursuit en arrière-plan.";
            }

            if (Pret())
            {
                journal?.Invoke($"Assistant prêt en {montre.Elapsed.TotalSeconds:F0} s.");

                return null;
            }

            var tranches = (int)(montre.Elapsed.TotalSeconds / 30);

            if (tranches > dit)
            {
                dit = tranches;
                journal?.Invoke($"Chargement en cours, {montre.Elapsed.TotalSeconds:F0} s écoulées...");
            }
        }

        return $"Le modèle n'a pas fini de charger en {Patience.TotalMinutes:F0} minutes. Voir {journalier}";
    }
}
