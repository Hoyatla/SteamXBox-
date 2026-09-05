using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace SenSÉ.Tools.Generation;

/// <summary>
/// Démarre le serveur de génération d'images et ouvre son interface.
/// </summary>
/// <remarks>
/// <b>Pourquoi un verbe plutôt que le verbe <c>python</c>.</b> Lancer le script suffisait à
/// démarrer un serveur, pas à rendre un outil : l'utilisateur voyait une console s'ouvrir et
/// devait deviner qu'une adresse l'attendait quelque part. Ici l'attente est tenue par le
/// produit — on démarre, on attend que le serveur réponde vraiment, puis on ouvre l'interface.
/// Un clic, une page, comme l'application que l'utilisateur connaît.
///
/// <para>
/// <b>ComfyUI reste détecté, jamais livré.</b> Sa licence est GPL v3. Le produit l'appelle comme
/// un programme séparé, ne s'y lie pas, et aucun installeur ne le pose. C'est la même frontière
/// que pour poppler, et c'est ce qui permet de le distribuer à part, librement, depuis le site.
/// </para>
///
/// <para>
/// Contrairement aux agrandisseurs, celui-ci a besoin d'un Python : ComfyUI <i>est</i> un
/// programme Python. Ce n'est pas la dépendance qu'on a supprimée ailleurs — c'est la nature de
/// l'outil appelé.
/// </para>
/// </remarks>
public static class ComfyServer
{
    /// <summary>La clé de cette ressource au registre.</summary>
    private const string Ressource = "generation";

    /// <summary>Le port d'origine de ComfyUI ; ses flux et ses extensions l'attendent.</summary>
    /// <summary>
    /// Le port du générateur, et l'unique endroit où il est décidé.
    /// </summary>
    /// <remarks>
    /// <b>Il était écrit en dur à quatre endroits</b> — ici, dans le lecteur de flux, dans les
    /// options vivantes et dans les capacités de l'assistant. Quatre copies d'un même nombre, dont
    /// trois qu'on aurait oubliées le jour où il change. Il change précisément aujourd'hui :
    /// ComfyUI Desktop n'écoute pas sur 8188.
    /// </remarks>
    public static int Port { get; set; } = 8188;

    /// <summary>
    /// Vrai quand le générateur ne nous appartient pas : c'est une installation à part.
    /// </summary>
    /// <remarks>
    /// <b>Ce que ce drapeau interdit, et pourquoi il faut qu'il l'interdise.</b> Le produit sait
    /// démarrer son générateur, l'inscrire au registre des ressources et le tuer à sa fermeture —
    /// c'est ce qui a réglé les dix gigaoctets de mémoire vidéo restés pris après une sortie. Rien
    /// de tout cela n'a de sens face à une application que l'utilisateur a installée, lancée et
    /// gardera ouverte après nous : la démarrer serait en ouvrir une seconde sur un port déjà pris,
    /// et la tuer serait fermer la fenêtre de quelqu'un d'autre.
    ///
    /// <para>
    /// Externe, le produit se contente donc de frapper à la porte et de dire quoi faire si personne
    /// ne répond. C'est moins de pouvoir, et c'est la seule attitude correcte envers un programme
    /// qu'on n'a pas lancé.
    /// </para>
    /// </remarks>
    public static bool Externe { get; set; }

    /// <summary>Ce qu'un démarrage à froid coûte ici, en secondes.</summary>
    /// <remarks>
    /// Mesuré entre 129 et 142 s sur cette machine. Le coût n'est pas ComfyUI mais la lecture à
    /// froid de ses 72 839 fichiers Python depuis le disque externe ; à chaud, le même démarrage
    /// tient en une quinzaine de secondes.
    /// </remarks>
    private const int Estimation = 130;

    /// <summary>
    /// Le serveur charge ses extensions avant de répondre, et cela prend du temps.
    /// </summary>
    /// <remarks>
    /// Mesuré au démarrage : les extensions et le catalogue de nœuds tiennent la main plusieurs
    /// dizaines de secondes. Une patience trop courte rendrait « échec » à un serveur qui démarre
    /// normalement — le défaut le plus coûteux à diagnostiquer, parce qu'il ressemble à une panne.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI");

    private static bool _fluxLivres;

    /// <summary>
    /// Met les flux livrés dans le dossier où le générateur va chercher ceux de l'utilisateur.
    /// </summary>
    /// <remarks>
    /// Les graphes du produit vivent dans <c>Flux\</c>, sous leur forme exécutable, parce que c'est
    /// celle-là que le moteur consomme et qu'elle se relit dans un diff. L'interface, elle, ne sait
    /// ouvrir que l'autre forme, depuis son propre dossier. La publication réconcilie les deux sans
    /// dupliquer la source : le fichier versionné reste l'unique original.
    ///
    /// <para>
    /// Tant que le générateur n'a pas répondu, la conversion se fait sans son catalogue — juste,
    /// mais incapable de deviner un réglage que le graphe laisse à sa valeur par défaut. On ne
    /// retient donc l'affaire comme faite que lorsque le catalogue a pu être lu, de sorte que le
    /// premier passage rende quelque chose d'utilisable et le suivant quelque chose de complet.
    /// </para>
    /// </remarks>
    private static void Livrer(Action<string>? journal)
    {
        if (_fluxLivres)
        {
            return;
        }

        var catalogue = Repond() ? Catalogue.Demander(Port, journal) : null;

        foreach (var dit in WorkflowsLivres.Publier(AppContext.BaseDirectory, Racine, catalogue))
        {
            if (!dit.EndsWith("publié.", StringComparison.Ordinal))
            {
                journal?.Invoke(dit);
            }
        }

        _fluxLivres = catalogue is not null;
    }

    private static string Script => Path.Combine(Racine, "main.py");

    private static string Interpreteur
        => Path.Combine(AppContext.BaseDirectory, "Outils", "Python", "python.exe");

    /// <summary>
    /// Ce que le générateur peut prendre au plus : la carte, moins ce que le bureau garde.
    /// </summary>
    /// <remarks>
    /// <b>Un chiffre relevé sur la machine, et non écrit à la main.</b> La valeur d'avant était
    /// 20000 Mio, décrite comme « le pic annoncé par le plus gros modèle installé » — elle n'a
    /// jamais été confrontée au matériel. Cette carte fait 12 282 Mio, dont 1 404 pris par le
    /// bureau. Un modèle de 16 Go a été téléchargé sur la foi de ce chiffre, puis a tourné quinze
    /// minutes avec la carte à 13 % : il n'échangeait plus qu'avec la mémoire vive.
    ///
    /// <para>
    /// Une déclaration que rien ne confronte au réel finit par décrire une autre machine que celle
    /// qui tourne — et le produit est justement destiné à des machines qu'on ne choisit pas.
    /// </para>
    ///
    /// <para>
    /// Deux gigaoctets au plancher : une carte que <c>nvidia-smi</c> ne sait pas lire — AMD, Intel,
    /// ou pas de pilote — rendrait zéro, et un coût nul ferait passer le plus gros consommateur du
    /// produit pour une ressource légère.
    /// </para>
    /// </remarks>
    private static int Disponible()
    {
        var (utilise, total) = SenSÉ.Tools.Serveurs.Ressources.MemoireVideo();

        return total > 0 ? Math.Max(2000, total - utilise) : 2000;
    }

    /// <summary>Le générateur est-il présent sur cette machine ?</summary>
    /// <remarks>
    /// Sert à ne déclarer les capacités de composition que lorsqu'elles peuvent aboutir. Une
    /// capacité qui échoue à chaque appel est pire que son absence : le modèle la voit, la juge
    /// pertinente, insiste, et la conversation tourne en rond.
    /// </remarks>
    public static bool Installe => File.Exists(Script) && File.Exists(Interpreteur);

    private static string Adresse
        => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Le journal du serveur : la seule trace quand il meurt avant de répondre.</summary>
    private static string Journalier => Path.Combine(Racine, "logs", "serveur.log");

    /// <summary>Démarre le serveur s'il dort, puis ouvre son interface.</summary>
    /// <param name="journal">Reçoit l'avancement : le démarrage n'est pas instantané.</param>
    public static string Ouvrir(Action<string>? journal)
    {
        if (Repond())
        {
            journal?.Invoke("Le serveur répondait déjà.");

            return Montrer()
                ? $"Interface ouverte : {Adresse}"
                : $"Le serveur répond, mais la fenêtre n'a pas pu s'ouvrir. Adresse : {Adresse}";
        }

        // Un second clic pendant le chargement lançait un second serveur, qui mourait sur le port
        // déjà pris et sur la base de données déjà verrouillée. Vu dans le journal : les lignes de
        // démarrage en double, puis « Port 8188 is already in use ». Le premier serveur finissait
        // par répondre, mais l'utilisateur avait vu deux échecs et concluait que la tuile ne faisait
        // rien. Le chargement dure quarante à quatre-vingt-dix secondes : cette fenêtre doit être
        // tenue.
        lock (Verrou)
        {
            if (_demarrage)
            {
                return "Démarrage déjà en cours. L'interface s'ouvrira d'elle-même.";
            }

            _demarrage = true;
        }

        // Le lancement lui-même part sur un autre fil, et pas seulement l'attente.
        //
        // Une tuile exécute son verbe sur le fil d'affichage. Faire de la place — chercher les
        // serveurs d'une session précédente parmi tous les processus de la machine — et interroger
        // la carte pour connaître sa mémoire prenaient à eux deux plus de trois secondes, mesurées
        // dans le journal entre le clic et le premier message. Trois secondes d'environnement figé
        // au moment précis où l'utilisateur juge si son clic a fait quelque chose.
        //
        // Ce qui était déchargé pour faire de la place arrive maintenant par le journal, qui
        // atteint l'écran : rien n'est perdu, tout arrive un peu plus tard et sans rien bloquer.
        Task.Run(() =>
        {
            var place = "";

            if (Lancer(journal, ref place) is { } refus)
            {
                journal?.Invoke(refus);
                Renoncer(refus);

                return;
            }

            if (place.Length > 0)
            {
                journal?.Invoke(place);
            }

            journal?.Invoke("Démarrage du serveur, le chargement des extensions prend un moment...");

            Attendre(journal);
        });

        // La durée annoncée est celle qu'on a mesurée, pas une formule polie.
        //
        // « une minute ou deux » ne dit pas s'il faut attendre ou renoncer. Deux minutes au premier
        // démarrage d'une session et une quinzaine de secondes ensuite, c'est ce que le disque
        // impose — le produit vit sur un disque externe USB, et la première lecture de Python et de
        // ses bibliothèques se paie une fois. Un utilisateur qui sait cela attend ; celui à qui on
        // dit « une minute ou deux » quitte à cent dix-sept secondes.
        return "Démarrage du générateur : compter deux minutes au premier lancement de la session, "
            + $"une quinzaine de secondes ensuite. L'interface s'ouvrira d'elle-même ; sinon "
            + $"l'adresse est {Adresse}.";
    }

    /// <summary>
    /// Assure que le serveur répond, sans ouvrir son interface.
    /// </summary>
    /// <remarks>
    /// La porte du verbe <c>flux</c> : un flux de travail a besoin d'un serveur qui écoute, pas
    /// d'une fenêtre. Ouvrir le navigateur pour une génération lancée depuis un panneau
    /// déposerait une page devant l'utilisateur sans qu'il l'ait demandée.
    ///
    /// <para>
    /// Contrairement à <see cref="Ouvrir"/>, l'attente est tenue ici même : l'appelant est déjà sur
    /// un fil de travail — le panneau exécute ses verbes hors du fil d'affichage — et il a besoin du
    /// serveur avant de continuer.
    /// </para>
    /// </remarks>
    /// <returns>Null quand le serveur répond, sinon la raison pour laquelle il ne répondra pas.</returns>
    public static string? Preparer(Action<string>? journal)
    {
        Livrer(journal);

        if (Repond())
        {
            return null;
        }

        // Un générateur qui ne nous appartient pas ne se démarre pas à sa place : on dit ce qui
        // manque, et l'utilisateur ouvre sa fenêtre. Le message nomme le port pour que « il ne
        // répond pas » ne devienne pas une devinette quand celui-ci a été changé.
        if (Externe)
        {
            return $"Le générateur ne répond pas sur le port {Port.ToString(CultureInfo.InvariantCulture)}. "
                + "Il est réglé comme une installation à part : ouvrez ComfyUI vous-même, puis "
                + "relancez. Si son port n'est pas celui-là, corrigez-le dans les réglages.";
        }

        // Un démarrage déjà en cours appartient à qui l'a commencé : on l'attend au lieu d'en
        // lancer un second, qui mourrait sur le port déjà pris.
        bool mien;

        lock (Verrou)
        {
            mien = !_demarrage;

            if (mien)
            {
                _demarrage = true;
            }
        }

        if (!mien)
        {
            journal?.Invoke("Le générateur démarre déjà, patience...");

            return Guetter(journal);
        }

        try
        {
            var place = "";

            if (Lancer(journal, ref place) is { } refus)
            {
                return refus;
            }

            if (place.Length > 0)
            {
                journal?.Invoke(place);
            }

            journal?.Invoke("Démarrage du générateur, le chargement des extensions prend un moment...");

            return Guetter(journal);
        }
        finally
        {
            lock (Verrou)
            {
                _demarrage = false;
            }
        }
    }

    /// <summary>Met en route le processus du serveur. Ne l'attend pas.</summary>
    /// <param name="journal">Reçoit l'avancement.</param>
    /// <param name="place">Reçoit ce qui a été déchargé pour lui faire de la place.</param>
    /// <returns>Null si le processus est parti, sinon la raison.</returns>
    private static string? Lancer(Action<string>? journal, ref string place)
    {
        if (!File.Exists(Script))
        {
            return $"ComfyUI est absent : {Script}";
        }

        if (!File.Exists(Interpreteur))
        {
            return $"Python est absent : {Interpreteur}. ComfyUI est un programme Python.";
        }

        // La carte ne tient pas deux gros consommateurs : le modèle de langage est déchargé avant,
        // plutôt que de laisser les deux se disputer la mémoire vidéo et ralentir ensemble.
        place = SenSÉ.Tools.Serveurs.Ressources.FairePlace(
            SenSÉ.Tools.Serveurs.Poids.Lourd, Ressource, journal);

        var lance = SenSÉ.Tools.Serveurs.ServeurLocal.Demarrer(
            Interpreteur,
            [
                "-u", Script,
                "--port", Port.ToString(CultureInfo.InvariantCulture),
                "--listen", "127.0.0.1",

                // Le générateur est coupé d'internet, et par un drapeau plutôt que par un fork.
                //
                // Il fait deux choses. Il n'enregistre pas la cinquantaine de nœuds qui appellent
                // des services payants tiers — inutilisables ici, puisqu'ils réclament un compte et
                // une clé, et encombrants pour qui doit choisir dans le catalogue. Et il pose une
                // Content-Security-Policy « self » sur toutes les réponses : le navigateur refuse
                // alors de lui-même toute requête vers un autre serveur.
                //
                // C'est plus sûr que retirer les liens du code. Retirer, c'est espérer n'en avoir
                // oublié aucun, et recommencer à chaque mise à jour ; la politique, elle, bloque
                // aussi ce qu'on aurait manqué. Et rien n'est modifié, donc rien à redistribuer
                // sous GPL.
                "--disable-api-nodes",
            ],
            Racine,
            Journalier,

            // Sortie forcée en UTF-8, et ce n'est pas de la précaution : une extension écrit un
            // emoji au démarrage, la sortie redirigée hérite du cp1252 d'un Windows français, et
            // l'encodage échoue à l'intérieur du journal. Mesuré : le serveur restait bloqué là,
            // sans jamais répondre ni rendre la main.
            new Dictionary<string, string>
            {
                ["PYTHONUTF8"] = "1",
                ["PYTHONIOENCODING"] = "utf-8",
            },
            new SenSÉ.Tools.Serveurs.ServeurLocal.Identite(
                Ressource,
                "Générateur multimédia",
                "comfyui",
                SenSÉ.Tools.Serveurs.Poids.Lourd,

                CoutVideoMo: Disponible(),
                SenSÉ.Tools.Serveurs.ALaFermeture.Tuer));

        if (lance is null)
        {
            return "Le serveur n'a pas démarré.";
        }

        _serveur = lance;

        return null;
    }

    /// <summary>Attend que le serveur réponde, puis ouvre l'interface.</summary>
    private static void Attendre(Action<string>? journal)
    {
        try
        {
            if (Guetter(journal) is { } echec)
            {
                journal?.Invoke(echec);

                return;
            }

            // L'adresse est annoncée dans tous les cas. Un utilisateur à qui l'on promet qu'une
            // interface va s'ouvrir, et devant qui rien n'apparaît, n'a aucun moyen de savoir que
            // le serveur, lui, l'attend.
            journal?.Invoke(Montrer()
                ? "Interface ouverte."
                : $"Le serveur répond, mais la fenêtre n'a pas pu s'ouvrir. "
                  + $"Ouvrez {Adresse} dans un navigateur.");
        }
        finally
        {
            // Quoi qu'il arrive, la porte se rouvre : sinon un démarrage manqué interdirait tout
            // nouvel essai jusqu'au redémarrage du produit.
            lock (Verrou)
            {
                _demarrage = false;
            }
        }
    }

    /// <summary>Attend que le serveur réponde.</summary>
    /// <returns>Null quand il répond, sinon la raison pour laquelle il ne répondra pas.</returns>
    private static string? Guetter(Action<string>? journal)
    {
        var montre = Stopwatch.StartNew();
        var dit = 0;

        while (montre.Elapsed < Patience)
        {
            Thread.Sleep(2000);

            if (Repond())
            {
                journal?.Invoke($"Serveur prêt en {montre.Elapsed.TotalSeconds:F0} s.");

                return null;
            }

            // Le processus a rendu la main sans jamais répondre : inutile d'attendre la fin de la
            // patience, la raison est déjà écrite dans le journal.
            //
            // C'est le PROCESSUS qui décide, jamais le texte du journal.
            //
            // La règle d'avant déclarait le serveur mort dès que son journal contenait les mots
            // « Error » et « Traceback ». Or ComfyUI en écrit à chaque extension facultative qui
            // renonce : le 23 août, KJNodes n'a pas trouvé Triton, l'a signalé en WARNING, a
            // continué, et le serveur a démarré normalement — le journal se termine sur
            // « Starting server ». Nous avons pourtant annoncé « Le serveur s'est arrêté :
            // PatchTritonVAE requires triton », deux secondes après le lancement, en désignant
            // comme cause de mort un avertissement sans rapport. L'assistant a relayé le
            // diagnostic et proposé d'installer Triton pour réparer un problème inexistant.
            if (Mort())
            {
                return Fini(Journalier)
                    ?? $"Le serveur s'est arrêté sans rien écrire. Voir {Journalier}";
            }

            // Toutes les dix secondes, et non toutes les trente.
            //
            // Trente secondes sans un mot devant un écran, c'est long : l'utilisateur a quitté deux
            // fois avant que le serveur ne soit prêt, la seconde fois à 117 secondes sur les 130
            // qu'il fallait. Un compteur qui bouge dit « ça travaille » mieux qu'une phrase juste
            // mais immobile.
            var tranches = (int)(montre.Elapsed.TotalSeconds / 10);

            if (tranches > dit)
            {
                dit = tranches;

                // Passé l'estimation, on cesse de la citer. « 140 s sur 130 environ » se contredit
                // dans la même phrase, et le seul chiffre auquel l'utilisateur pouvait se raccrocher
                // devient la preuve qu'on ne sait pas — juste au moment où il se demande s'il doit
                // encore attendre. Ce démarrage-ci a pris 142 s.
                journal?.Invoke(
                    montre.Elapsed.TotalSeconds < Estimation
                        ? $"Démarrage du générateur : {montre.Elapsed.TotalSeconds:F0} s "
                          + $"sur {Estimation.ToString(CultureInfo.InvariantCulture)} environ..."
                        : $"Démarrage du générateur : {montre.Elapsed.TotalSeconds:F0} s, "
                          + "plus long que d'habitude, mais toujours en cours...");
            }
        }

        return $"Le serveur n'a pas répondu en {Patience.TotalMinutes:F0} minutes. Voir {Journalier}";
    }

    /// <summary>Abandonne le démarrage en rendant la main, et rouvre la porte.</summary>
    private static string Renoncer(string raison)
    {
        lock (Verrou)
        {
            _demarrage = false;
        }

        return raison;
    }

    /// <summary>Le serveur répond-il ?</summary>
    public static bool Repond()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            return client.GetAsync(Adresse + "/system_stats").GetAwaiter().GetResult().IsSuccessStatusCode;
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

    /// <summary>La dernière erreur du journal, si le démarrage s'est arrêté net.</summary>
    /// <summary>Le serveur que nous avons lancé a-t-il rendu la main ?</summary>
    /// <remarks>
    /// Sans processus connu — un générateur lancé hors de nous, ou un second appel pendant que le
    /// premier démarre — on ne conclut rien. Répondre « mort » par ignorance ferait exactement la
    /// faute qu'on corrige, dans l'autre sens.
    /// </remarks>
    private static bool Mort()
    {
        try
        {
            return _serveur is { HasExited: true };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Pourquoi il s'est arrêté, d'après la dernière faute de son journal.
    /// </summary>
    /// <remarks>
    /// <b>À n'appeler qu'une fois la mort constatée.</b> Cette lecture est une heuristique : elle
    /// prend la dernière ligne contenant « Error » et la donne pour cause. C'est raisonnable sur un
    /// processus dont on sait déjà qu'il est mort, et faux sur un processus vivant — ComfyUI écrit
    /// des traces d'erreur complètes pour des extensions facultatives qu'il abandonne sans cesser
    /// de fonctionner.
    /// </remarks>
    private static string? Fini(string fichier)
    {
        if (!File.Exists(fichier))
        {
            return null;
        }

        string contenu;

        try
        {
            using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var lecteur = new StreamReader(flux);
            contenu = lecteur.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }

        var faute = contenu.LastIndexOf("Error", StringComparison.OrdinalIgnoreCase);

        if (faute < 0 || !contenu.Contains("Traceback", StringComparison.Ordinal))
        {
            return null;
        }

        var ligne = contenu[faute..].Split('\n')[0].Trim();

        return $"Le serveur s'est arrêté : {ligne} (journal : {fichier})";
    }

    /// <summary>
    /// Ouvre l'interface dans une fenêtre d'application, pas dans un onglet.
    /// </summary>
    /// <remarks>
    /// Le mode <c>--app</c> donne une fenêtre sans barre d'adresse, sans onglets et sans favoris :
    /// l'allure d'un logiciel, ce que l'utilisateur attend d'une tuile. Ouvert autrement, ComfyUI
    /// arrive comme une page web parmi d'autres, et un serveur local ressemble alors à un service
    /// distant — ce qu'il n'est pas : rien ne sort de la machine.
    ///
    /// <para>
    /// Edge d'abord parce qu'il est présent sur tout Windows, Chrome ensuite, et à défaut le
    /// navigateur par défaut : mieux vaut un onglet que rien.
    /// </para>
    /// </remarks>
    private static bool Montrer()
    {
        // Passage par l'explorateur, et c'est la seule voie fiable ici : SenSÉ demande
        // « requireAdministrator », donc tout ce qu'il lance hérite de ses droits — or Chrome refuse
        // de tourner en administrateur et Edge passe la main à l'instance ordinaire, qui ignore la
        // demande. Résultat constaté : le serveur démarrait parfaitement, le journal annonçait
        // « To see the GUI go to… », et aucune fenêtre n'apparaissait. L'explorateur, lui, tourne
        // sous le compte de l'utilisateur : ce qu'il ouvre s'ouvre normalement.
        //
        // Le prix payé est la fenêtre sans barre d'adresse : l'explorateur ne transmet pas
        // d'arguments, donc plus de mode « --app ». Une fenêtre ordinaire qui s'ouvre vaut mieux
        // qu'une jolie qui reste invisible.
        try
        {
            var depart = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            depart.ArgumentList.Add(Adresse);

            Process.Start(depart)?.Dispose();

            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static readonly object Verrou = new();

    /// <summary>Un démarrage est-il déjà en route ?</summary>
    private static bool _demarrage;

    /// <summary>Le serveur que nous avons lancé, tant qu'il vit.</summary>
    /// <remarks>
    /// C'est la seule autorité sur « le serveur est-il mort ». Le journal, lui, ne dit que pourquoi.
    /// </remarks>
    private static Process? _serveur;

}
