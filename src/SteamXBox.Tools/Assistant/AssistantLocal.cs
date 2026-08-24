using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SteamXBox.Plugins;

namespace SteamXBox.Tools.Assistant;

/// <summary>
/// L'assistant local : il comprend une demande, et appelle les outils du produit pour y répondre.
/// </summary>
/// <remarks>
/// <b>La liste des outils vient des manifestes, jamais d'une seconde description.</b> C'est la
/// règle que le vocabulaire des plugins pose déjà — décrit une fois, lu par le chargeur, par
/// l'éditeur et par le modèle. Une liste écrite à la main pour le modèle divergerait au premier
/// outil ajouté, et c'est toujours la copie oubliée qui garde le défaut.
///
/// <para>
/// <b>Le modèle ne reçoit aucun pouvoir nouveau.</b> Il ne peut nommer que les outils installés,
/// avec les réglages que leur manifeste déclare — exactement ce qu'un utilisateur pourrait cliquer.
/// Il ne compose pas de commande, ne touche pas au disque, et l'hôte exécute. Ce qui rendait un
/// manifeste sûr à installer rend ici l'assistant sûr à écouter.
/// </para>
///
/// <para>
/// Le modèle installé raisonne avant de répondre et écrit sa réflexion dans un champ séparé. Elle
/// est écartée : montrer à l'utilisateur le brouillon d'un modèle n'aide personne, et la garder
/// dans l'historique gonflerait un contexte déjà réduit.
/// </para>
/// </remarks>
public sealed class AssistantLocal
{
    /// <summary>
    /// Le fil de la conversation, gardé d'un tour à l'autre.
    /// </summary>
    /// <remarks>
    /// <b>Sans lui, ce n'était pas une conversation.</b> Chaque question repartait de la consigne
    /// seule : à « oui », l'assistant répondait « Bonjour, que souhaitez-vous faire ? », et à
    /// « oui guide-moi » il ouvrait une fenêtre au hasard. Observé en usage réel — le défaut ne se
    /// voit pas sur une question isolée, seulement dès qu'on enchaîne.
    /// </remarks>
    private readonly JsonArray _messages =
    [
        new JsonObject { ["role"] = "system", ["content"] = Consigne },
    ];

    /// <summary>
    /// Au-delà, l'historique mange le contexte que le modèle doit garder pour les outils.
    /// </summary>
    /// <remarks>
    /// Huit mille jetons en tout, dont la déclaration des outils prend déjà une bonne part. Les
    /// tours les plus anciens sont oubliés en premier ; la consigne, elle, ne s'oublie jamais.
    /// </remarks>
    private const int MessagesGardes = 16;

    /// <summary>Au-delà, une demande tourne en rond plutôt qu'elle n'aboutit.</summary>
    /// <remarks>
    /// La borne était à quatre, et quatre suffit à une demande simple : appeler un outil, puis
    /// répondre. Elle ne suffit pas à un enchaînement. « Fais une image, anime-la, puis agrandis-la »
    /// demande trois appels et une réponse finale, soit exactement quatre — aucune marge pour une
    /// question de précision, ni pour un outil qui se plaint et qu'il faut rappeler autrement.
    ///
    /// <para>
    /// Huit laisse cette marge sans ouvrir la porte à la boucle : chaque tour coûte une génération
    /// complète, et le modèle qui n'a rien conclu au huitième ne conclura pas au douzième. La borne
    /// atteinte, c'est le dernier résultat d'outil qui est rendu — il dit au moins ce qui a
    /// réellement été fait, et où.
    /// </para>
    /// </remarks>
    private const int ToursMaximum = 8;

    /// <summary>
    /// Le modèle raisonne avant de répondre : sans marge, il s'arrête en pleine réflexion.
    /// </summary>
    /// <remarks>
    /// Mesuré : à 1024, la réponse qui suit l'exécution d'un outil revenait vide, toute la marge
    /// étant partie dans la réflexion. Le symptôme trompe — on croit le modèle muet alors qu'il a
    /// été coupé.
    /// </remarks>
    private const int JetonsMaximum = 2048;

    private const string Consigne =
        "Tu assistes l'utilisateur de SteamXBox tout entier, en français — quel que soit le domaine "
        + "des outils installés : images, documents, courrier, jeu, web. Tu disposes des outils "
        + "présents sur cette machine, chacun déclarant lui-même ce qu'il sait faire : sers-t'en dès "
        + "qu'une demande y correspond, au lieu de décrire ce qu'il faudrait faire. "
        + "Un chemin de fichier doit venir de l'utilisateur, ne l'invente jamais : s'il en manque "
        + "un, demande-le : il a les boutons « Fichier… » et « Dossier… » sous la conversation, et "
        + "Ctrl+V colle un fichier copié dans l'explorateur. "
        + "Quand il veut voir ou choisir lui-même, ouvre le panneau de l'outil plutôt que de lancer "
        + "le travail ; tu peux ensuite y régler les options sous ses yeux. Réponds court.\n\n"

        + "TU VOIS LES IMAGES. Quand l'utilisateur désigne un fichier image, elle t'est montrée avec "
        + "son message : regarde-la et dis ce que tu y vois. Ne réponds jamais que tu ne sais pas "
        + "lire une image.\n\n"

        + "AGIS, N'ÉNUMÈRE PAS. Une liste d'options numérotées n'est pas une réponse : elle renvoie "
        + "à l'utilisateur le travail de choisir à ta place. S'il te manque UNE information, pose "
        + "UNE question courte. Ne repose jamais la même question sous une autre forme, et ne "
        + "propose pas deux fois la même liste — s'il a déjà répondu, tiens sa réponse pour acquise "
        + "et sers-toi de l'outil qui convient.\n\n"

        + "FAIS, PLUTÔT QUE DE NOTER. Le cas normal est d'exécuter la demande tout de suite : "
        + "appelle l'outil, réponds, et n'écris aucun carnet. Un carnet coûte un tour d'attente à "
        + "l'utilisateur et ne lui apprend rien quand le travail tient en deux ou trois gestes.\n\n"

        + "CARNET. N'en ouvre un que lorsque la demande déborde : plusieurs outils à enchaîner, "
        + "plusieurs fichiers, des étapes qui dépendent les unes des autres, ou une demande qui "
        + "s'est étoffée au fil de la discussion au point que tu risques d'en perdre un morceau. "
        + "Quand tu en ouvres un, couvre TOUT ce que l'utilisateur a demandé depuis le début de la "
        + "discussion, et pas seulement son dernier message. Si un travail ouvert ci-dessous "
        + "correspond déjà, continue-le et coche ce qui est fait ; si tu hésites à rattacher une "
        + "demande à un travail existant, DEMANDE plutôt que de deviner.\n\n"

        + "MES TÂCHES. Le carnet intitulé « Mes tâches » est écrit par l'utilisateur lui-même, pas "
        + "par toi. Ne le réécris pas, ne l'efface jamais, et ne demande pas d'accord pour lui : il "
        + "l'a déjà donné en l'écrivant. Coche ce que tu as réellement fait, et propose de t'en "
        + "occuper quand le moment s'y prête — sans interrompre ce que l'utilisateur te demande "
        + "maintenant.\n\n"

        + "ACCORD. Après avoir noté un carnet, ÉNONCE le plan en une phrase par étape, puis demande "
        + "à l'utilisateur s'il veut COMMENCER, MODIFIER ou ABANDONNER. N'exécute aucune étape "
        + "avant qu'il ait choisi. S'il accepte, appelle travail_accepter puis commence ; s'il "
        + "demande un changement, réécris le carnet avec travail_noter et redemande. Une génération "
        + "d'image prend cinq minutes : six étapes lancées sur une intention mal comprise coûtent "
        + "une demi-heure, et cela ne se découvre qu'à la fin.";

    /// <summary>
    /// La consigne, suivie des travaux ouverts.
    /// </summary>
    /// <remarks>
    /// <b>Les carnets sont mis sous ses yeux, jamais laissés à sa mémoire.</b> Une capacité
    /// « relis ton carnet » supposait qu'un modèle de quatre milliards de paramètres pense à
    /// l'appeler ; il n'y pense pas de façon fiable, et l'oubli ne se voit qu'après coup, quand il
    /// recommence une étape déjà faite. Les réinjecter à chaque tour coûte une centaine de jetons
    /// et supprime le problème au lieu de l'espérer résolu.
    ///
    /// <para>
    /// C'est ce qui fait du travail un principe de fonctionnement plutôt qu'un outil parmi
    /// d'autres : le modèle ne peut pas répondre sans avoir sous les yeux ce qui est en cours.
    /// </para>
    /// </remarks>
    public static string Regles => Consigne;

    /// <summary>Les travaux ouverts, tels qu'ils sont rappelés au modèle avant chaque demande.</summary>
    /// <remarks>
    /// <b>Le silence serait ambigu.</b> Sans rappel, le modèle ne saurait pas s'il n'y a rien en
    /// cours ou si on ne le lui a pas dit — et dans le doute il rattacherait une demande neuve à un
    /// travail imaginaire. La phrase « Aucun travail ouvert » est donc dite, et pas seulement sous-
    /// entendue par une absence.
    /// </remarks>
    public static string RappelTravaux(Action<string>? journal)
    {
        var carnets = FichierTravail.Lister(journal);

        if (carnets.Count == 0)
        {
            return MarqueTravaux + " Aucun travail ouvert.";
        }

        var texte = new StringBuilder(MarqueTravaux).AppendLine(" TRAVAUX OUVERTS :");

        foreach (var carnet in carnets)
        {
            texte.AppendLine(FichierTravail.Resumer(carnet));
        }

        return texte.ToString();
    }

    /// <summary>
    /// Ce qui marque le rappel des travaux, pour le retrouver et le remplacer.
    /// </summary>
    /// <remarks>
    /// Une marque en clair plutôt qu'un index mémorisé : la liste des messages est élaguée par le
    /// début quand elle s'allonge, et tout numéro de position devient faux au premier élagage —
    /// silencieusement, en désignant le message du voisin.
    /// </remarks>
    private const string MarqueTravaux = "[travaux]";

    /// <summary>Remplace le rappel des travaux ouverts en queue de conversation.</summary>
    /// <remarks>
    /// <b>Pourquoi en queue et non dans la consigne.</b> Le serveur garde en cache le début du
    /// dialogue d'un tour à l'autre ; c'est ce qui évite de retraiter des milliers de jetons à
    /// chaque question. Ce cache porte sur un préfixe : il tient tant que le début ne bouge pas.
    /// Écrire les carnets dans le message système les plaçait en tête, donc le début changeait à
    /// chaque cochage, donc le cache tombait entièrement. Placé en queue, le rappel n'invalide que
    /// ce qui le suit.
    ///
    /// <para>
    /// Rien n'est perdu de ce que la consigne apportait : le modèle a toujours les travaux ouverts
    /// sous les yeux avant de répondre, et il les a même plus près de la question qu'auparavant.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Le texte d'un contenu de message, ou rien s'il n'en est pas un.
    /// </summary>
    /// <remarks>
    /// <b>Depuis que l'assistant voit, un contenu n'est plus toujours une chaîne.</b> Un message
    /// qui porte une image est un tableau — du texte, puis l'image — et lire ce tableau comme une
    /// chaîne lève « The node must be of type 'JsonValue' ». C'est exactement ce qui est arrivé le
    /// 23 août : la première image a été décrite correctement, et la demande suivante a échoué sur
    /// cette exception, parce que le balayage des rappels relisait tout l'historique.
    ///
    /// <para>
    /// Le tableau rend une chaîne vide plutôt que sa partie texte, et c'est voulu : le seul appelant
    /// cherche la marque des travaux, qu'un message d'utilisateur ne porte jamais. Extraire le texte
    /// d'une image pour l'y chercher aurait ajouté un chemin d'erreur pour un cas qui ne se présente
    /// pas.
    /// </para>
    /// </remarks>
    private static string Ecrit(JsonNode? contenu)
        => contenu is JsonValue valeur && valeur.TryGetValue<string>(out var texte) ? texte : "";

    private void Rappeler(Action<string>? journal)
    {
        for (var rang = _messages.Count - 1; rang >= 1; rang--)
        {
            var texte = Ecrit(_messages[rang]?["content"]);

            if (texte.StartsWith(MarqueTravaux, StringComparison.Ordinal))
            {
                _messages.RemoveAt(rang);
            }
        }

        _messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = RappelTravaux(journal),
        });
    }

    /// <summary>Ce que l'hôte doit savoir faire pour qu'un outil nommé par le modèle s'exécute.</summary>
    /// <param name="manifeste">L'outil désigné.</param>
    /// <param name="reglages">Les valeurs, nommées comme les identifiants du manifeste.</param>
    public delegate string Executeur(PluginManifest manifeste, IReadOnlyDictionary<string, string> reglages);

    /// <summary>Un paramètre d'une capacité de l'hôte.</summary>
    /// <param name="Nom">Son identifiant, tel que le modèle le renverra.</param>
    /// <param name="Description">Ce qu'il désigne, en une phrase.</param>
    /// <param name="Valeurs">Les valeurs admises, ou vide si elles sont libres.</param>
    public sealed record Parametre(string Nom, string Description, IReadOnlyList<string> Valeurs);

    /// <summary>
    /// Quelque chose que l'hôte sait faire, au-delà de lancer un outil.
    /// </summary>
    /// <remarks>
    /// Lancer un outil ne suffit pas à faire un assistant : ouvrir son panneau, régler une option
    /// sous les yeux de l'utilisateur, ouvrir une fenêtre du produit sont des gestes que personne
    /// ne peut décrire dans un manifeste — ils appartiennent à l'environnement.
    ///
    /// <para>
    /// Elles restent bornées comme le reste : chacune est déclarée ici avec ses paramètres et ses
    /// valeurs admises, et le modèle ne peut nommer qu'elles. Il ne gagne aucun pouvoir qu'un
    /// utilisateur n'aurait pas en cliquant.
    /// </para>
    /// </remarks>
    /// <param name="Nom">Le nom que le modèle emploie.</param>
    /// <param name="Description">À quoi elle sert.</param>
    /// <param name="Parametres">Ce qu'elle attend.</param>
    /// <param name="Faire">Ce que l'hôte exécute, sur son fil d'affichage s'il le faut.</param>
    /// <param name="Interne">
    /// Vrai pour la tenue de carnet et les autres gestes que l'utilisateur n'a pas demandés.
    /// </param>
    /// <remarks>
    /// <b>Ce que <c>Interne</c> empêche.</b> Quand le modèle se tait après avoir lancé un outil, on
    /// rend à sa place ce que l'outil a répondu : c'est en général ce que l'utilisateur voulait
    /// savoir — un chemin de fichier produit, un compte d'images. Mais un carnet coché ne lui
    /// apprend rien, et un carnet <em>manqué</em> lui apprend pire : il a reçu « Carnet ou étape
    /// introuvable. Relis le carnet pour voir ce qu'il porte. » comme réponse à « montage vidéo
    /// d'après plusieurs images dans un dossier ». Une consigne adressée au modèle, servie à
    /// l'utilisateur comme s'il devait y obéir.
    /// </remarks>
    public sealed record Capacite(
        string Nom,
        string Description,
        IReadOnlyList<Parametre> Parametres,
        Func<IReadOnlyDictionary<string, string>, string> Faire,
        bool Interne = false);

    /// <summary>Répond à une demande, en appelant les outils si besoin.</summary>
    public string Repondre(
        string demande,
        IReadOnlyList<PluginManifest> outils,
        IReadOnlyList<Capacite> capacites,
        Executeur executeur,
        Action<string>? journal,
        CancellationToken arret = default)
    {
        // Avant le chargement du modèle, et pas après.
        //
        // Le chargement prend 90 secondes à chaud et plus de cinq minutes à froid. Vérifier l'arrêt
        // seulement une fois le serveur prêt aurait rendu le bouton inerte pendant toute cette
        // attente — c'est-à-dire précisément au moment où l'on regrette d'avoir lancé la demande.
        if (arret.IsCancellationRequested)
        {
            return Interrompu("");
        }

        if (ServeurModele.Demarrer(journal, arret) is { } echec)
        {
            return echec;
        }

        // Les travaux ouverts sont rappelés en queue de conversation, pas dans la consigne.
        //
        // Ils y ont vécu une journée, et cela coûtait dix secondes par tour. Mesuré dans une vraie
        // session : 6 579 jetons de consigne à 1,46 ms le jeton, soit 9,6 s avant que le modèle
        // n'écrive un mot. Le serveur garde pourtant en cache le début du dialogue d'un tour à
        // l'autre — mais réécrire le message système change le premier jeton, et un cache de
        // préfixe qui perd son premier jeton les perd tous.
        //
        // Le rappel dit la même chose au même moment ; il est seulement placé là où il ne détruit
        // rien. L'ancien est retiré avant que le nouveau ne s'ajoute, sinon le modèle lirait l'état
        // des carnets à trois tours d'écart et croirait à trois travaux différents.
        Rappeler(journal);

        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = Vue.Contenu(demande, journal) });
        Elaguer();

        var declares = Declarer(outils, capacites);

        // Ce que le dernier outil a répondu. Si le modèle se tait après l'avoir lancé — il lui
        // arrive d'épuiser sa marge en réflexion — c'est cette phrase qui vaut réponse : elle dit
        // ce qui a réellement été fait, et où.
        var dernierResultat = "";

        // Une seule relance quand le modèle se tait sans rien avoir produit.
        var relance = false;

        for (var tour = 0; tour < ToursMaximum; tour++)
        {
            // L'arrêt est vérifié entre les tours, et non pendant.
            //
            // Un tour, c'est une réflexion du modèle puis au plus un outil lancé. Ce qui a déjà été
            // envoyé au générateur continue — cinq minutes de carte graphique ne se rappellent pas —
            // mais l'enchaînement s'arrête là. C'est ce qui manquait quand l'assistant est parti sur
            // un diagnostic faux : il n'y avait aucun moyen de l'interrompre avant sa dernière
            // étape, et six étapes lancées sur une erreur coûtent une demi-heure.
            if (arret.IsCancellationRequested)
            {
                return Interrompu(dernierResultat);
            }

            var reponse = Demander(_messages, declares, arret);

            if (reponse is null)
            {
                return "L'assistant n'a pas répondu.";
            }

            var choix = reponse["choices"]?[0];
            var message = choix?["message"] as JsonObject;

            if (message is null)
            {
                return "Réponse inattendue de l'assistant.";
            }

            if (message["tool_calls"] is not JsonArray appels || appels.Count == 0)
            {
                var texte = Ecrit(message["content"]).Trim();

                if (texte.Length == 0 && dernierResultat.Length == 0 && !relance)
                {
                    // Un tour de plus, une seule fois, plutôt qu'un cul-de-sac.
                    //
                    // Le modèle a dépensé son tour en tenue de carnet, puis s'est tu : il n'avait
                    // rien à dire parce qu'il n'avait rien fait pour l'utilisateur. Celui-ci
                    // recevait alors « L'assistant n'a rien répondu de lisible » en réponse à une
                    // demande parfaitement claire — ou, pire, la consigne interne destinée au
                    // modèle. Lui rappeler à qui il parle coûte un tour et récupère l'échange.
                    relance = true;

                    _messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = "Réponds maintenant à ma demande, en français, sans appeler "
                            + "d'outil. Si tu as besoin d'une information de ma part — un dossier, "
                            + "un fichier, un choix — demande-la.",
                    });

                    continue;
                }

                var dit = texte.Length > 0
                    ? texte
                    : dernierResultat.Length > 0
                        ? dernierResultat
                        : "L'assistant n'a rien répondu de lisible.";

                _messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = dit });

                return dit;
            }

            // La réflexion est écartée de l'historique : elle ne sert pas la suite et coûte du contexte.
            message.Remove("reasoning_content");
            _messages.Add(JsonNode.Parse(message.ToJsonString())!);

            // Le modèle émet parfois deux fois le même appel dans un seul tour — constaté :
            // « L'assistant lance Générateur multimédia » affiché deux fois de suite. Exécuter le
            // doublon relancerait le travail. Une réponse lui est quand même rendue pour chaque
            // appel : il en attend une par identifiant, et un manquant casse l'échange suivant.
            var dejaFaits = new HashSet<string>(StringComparer.Ordinal);

            foreach (var appel in appels)
            {
                var signature = (appel?["function"]?["name"]?.GetValue<string>() ?? "")
                    + "|" + (appel?["function"]?["arguments"]?.GetValue<string>() ?? "");

                if (!dejaFaits.Add(signature))
                {
                    _messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = appel?["id"]?.GetValue<string>() ?? "",
                        ["content"] = "Déjà fait à l'instant, dans ce même tour.",
                    });

                    continue;
                }

                // Vérifié aussi avant chaque outil, et pas seulement entre les tours : le modèle
                // peut en demander plusieurs d'un coup, et le troisième d'une série est justement
                // celui qu'on veut empêcher quand on a vu le premier partir de travers.
                if (arret.IsCancellationRequested)
                {
                    _messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = appel?["id"]?.GetValue<string>() ?? "",
                        ["content"] = "Arrêté par l'utilisateur avant exécution.",
                    });

                    continue;
                }

                var resultat = Executer(appel, outils, capacites, executeur, journal);

                // La tenue de carnet ne peut jamais valoir réponse : voir Capacite.Interne.
                var appele = appel?["function"]?["name"]?.GetValue<string>() ?? "";

                if (!capacites.Any(c => Nom(c.Nom) == appele && c.Interne))
                {
                    dernierResultat = Ecrit(resultat["content"]) is { Length: > 0 } rendu ? rendu : dernierResultat;
                }

                _messages.Add(resultat);
            }

            if (arret.IsCancellationRequested)
            {
                return Interrompu(dernierResultat);
            }
        }

        return dernierResultat.Length > 0
            ? dernierResultat
            : $"L'assistant n'a pas abouti en {ToursMaximum.ToString(CultureInfo.InvariantCulture)} échanges.";
    }

    /// <summary>Ce qu'on rend quand l'utilisateur a demandé l'arrêt.</summary>
    /// <remarks>
    /// Ce qui a déjà été fait est rappelé plutôt qu'effacé : l'utilisateur arrête souvent parce que
    /// la suite part de travers, pas parce que le début était inutile, et il doit savoir où le
    /// travail s'est interrompu pour décider quoi reprendre.
    /// </remarks>
    private static string Interrompu(string dernierResultat)
        => dernierResultat.Length > 0
            ? "Arrêté à votre demande. Dernière étape effectuée : " + dernierResultat
            : "Arrêté à votre demande. Rien n'avait encore été lancé.";

    /// <summary>
    /// Oublie les tours les plus anciens quand le fil devient trop long.
    /// </summary>
    /// <remarks>
    /// La consigne est gardée quoi qu'il arrive : c'est elle qui dit au modèle ce qu'il est. Le
    /// reste est taillé par la fin, la plus récente étant la plus utile.
    /// </remarks>
    private void Elaguer()
    {
        while (_messages.Count > MessagesGardes + 1)
        {
            _messages.RemoveAt(1);
        }

        // Un resultat d'outil ne survit jamais a l'appel qui l'a demande.
        //
        // La taille se comptait en messages, sans regarder ce qu'ils etaient. Un tour d'outil en
        // occupe deux — l'assistant qui appelle, puis le resultat qui porte son identifiant — et la
        // coupe pouvait tomber entre les deux : l'historique commencait alors par un message de role
        // « tool » repondant a un appel que plus rien ne declarait. Un serveur qui verifie cette
        // structure refuse la requete, et l'utilisateur lit « L'assistant n'a pas repondu » au
        // milieu d'une conversation qui marchait.
        while (_messages.Count > 1
               && _messages[1]?["role"]?.GetValue<string>() is "tool")
        {
            _messages.RemoveAt(1);
        }
    }

    /// <summary>Exécute un appel d'outil et rend le message de résultat à renvoyer au modèle.</summary>
    private static JsonObject Executer(
        JsonNode? appel,
        IReadOnlyList<PluginManifest> outils,
        IReadOnlyList<Capacite> capacites,
        Executeur executeur,
        Action<string>? journal)
    {
        var identifiant = appel?["id"]?.GetValue<string>() ?? "";
        var nom = appel?["function"]?["name"]?.GetValue<string>() ?? "";
        var arguments = appel?["function"]?["arguments"]?.GetValue<string>() ?? "{}";

        var manifeste = outils.FirstOrDefault(o => Nom(o.Id) == nom);
        var capacite = capacites.FirstOrDefault(c => Nom(c.Nom) == nom);

        string resultat;

        if (capacite is not null)
        {
            // Avec ses arguments, et pas seulement son nom.
            //
            // « L'assistant fait travail_cocher » suivi de « étape introuvable » ne dit pas quelle
            // étape il a cherchée : la panne est visible et indiagnosticable à la fois. Le journal
            // sert à savoir ce qui s'est passé, ce qui suppose de savoir ce qui a été demandé.
            journal?.Invoke($"L'assistant fait « {capacite.Nom} » {Ecourter(arguments, 200)}");
            resultat = capacite.Faire(Lire(arguments));
        }
        else if (manifeste is null)
        {
            resultat = $"Outil inconnu : {nom}";
        }
        else
        {
            // Avec ses réglages, pour la même raison que les capacités.
            //
            // « L'assistant lance Créer une image » suivi de « le générateur a refusé le flux » ne
            // dit pas quel réglage était fautif. Le 23 août, il a fallu deviner — et il n'y avait
            // rien à deviner, l'information n'existait nulle part.
            journal?.Invoke($"L'assistant lance « {manifeste.Name} » {Ecourter(arguments, 200)}");
            resultat = executeur(manifeste, Lire(arguments));
        }

        // L'issue au journal, comme pour les verbes du répartiteur.
        //
        // Sans elle, une session relue le lendemain montre « L'assistant fait flux_lancer » puis
        // rien : impossible de savoir si le flux a abouti, ni où le fichier est allé. Écourtée,
        // parce qu'une réponse d'outil peut faire un millier de caractères — un catalogue de nœuds,
        // par exemple — et que le journal sert à diagnostiquer, pas à transcrire.
        journal?.Invoke($"issue de « {nom} » : {Ecourter(resultat, 400)}");

        // Le fichier produit est nommé à part, sous une forme stable.
        //
        // C'est ce qui rend l'enchaînement possible. Chaque verbe annonce sa réussite dans sa propre
        // langue, et le modèle devrait deviner lequel de ces mots précède un chemin, puis le
        // recopier sans faute. Il s'en sort mal, et l'erreur ne se voit qu'à l'outil suivant, qui se
        // plaint d'un fichier introuvable dont l'utilisateur n'a jamais entendu parler.
        //
        // La ligne ajoutée ici a toujours la même forme, et le chemin qu'elle porte a été confirmé
        // par le disque. Le modèle n'a plus qu'à le recopier — ce qu'il fait bien — au lieu de le
        // reconstruire.
        if (FichierProduit.Trouver(resultat) is { } produit)
        {
            resultat = resultat
                + "\n\nFICHIER PRODUIT : " + produit
                + "\nCe fichier existe. Pour continuer le travail, donne ce chemin tel quel à un "
                + "autre outil.";
        }

        return new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = identifiant,
            ["content"] = resultat,
        };
    }

    /// <summary>Une phrase ramenée à une ligne et à une longueur lisible.</summary>
    private static string Ecourter(string texte, int combien)
    {
        var propre = (texte ?? "").ReplaceLineEndings(" ").Trim();

        return propre.Length > combien ? string.Concat(propre.AsSpan(0, combien), "…") : propre;
    }

    /// <summary>Les outils installés, traduits dans le dialecte que le modèle attend.</summary>
    public static JsonArray Declarer(IReadOnlyList<PluginManifest> outils, IReadOnlyList<Capacite> capacites)
    {
        var declares = new JsonArray();

        foreach (var capacite in capacites)
        {
            var proprietes = new JsonObject();
            var obligatoires = new JsonArray();

            foreach (var parametre in capacite.Parametres)
            {
                var decrit = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = parametre.Description,
                };

                if (parametre.Valeurs.Count > 0)
                {
                    var valeurs = new JsonArray();
                    foreach (var valeur in parametre.Valeurs)
                    {
                        valeurs.Add(valeur);
                    }

                    decrit["enum"] = valeurs;
                }

                proprietes[parametre.Nom] = decrit;
                obligatoires.Add(parametre.Nom);
            }

            declares.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = Nom(capacite.Nom),
                    ["description"] = capacite.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = proprietes,
                        ["required"] = obligatoires,
                    },
                },
            });
        }

        foreach (var outil in outils)
        {
            var proprietes = new JsonObject();
            var obligatoires = new JsonArray();

            foreach (var champ in outil.Content)
            {
                if (champ.Id.Length == 0 || champ.Kind.Equals("action", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                proprietes[champ.Id] = Champ(champ);

                // Obligatoire seulement si le manifeste ne donne pas de valeur par défaut.
                //
                // Tout déclarer obligatoire rendait les outils utiles inappelables. « Anime cette
                // image » réclamait au modèle sept valeurs d'un coup, dont un chemin de fichier
                // qu'il n'a aucun moyen de connaître ; devant ce mur, il se rabattait sur la seule
                // chose qu'il pouvait faire sans rien inventer — ouvrir l'outil — et l'utilisateur
                // voyait un assistant qui ne sait qu'ouvrir des fenêtres.
                //
                // L'exécution retombait déjà sur la valeur par défaut pour tout champ omis : ce qui
                // est corrigé ici, c'est la déclaration, qui exigeait ce dont personne n'avait
                // besoin. Il ne reste d'obligatoire que ce que le modèle doit réellement obtenir de
                // l'utilisateur, c'est-à-dire en pratique le fichier sur lequel travailler.
                if (champ.Value.Length == 0)
                {
                    obligatoires.Add(champ.Id);
                }
            }

            declares.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = Nom(outil.Id),
                    ["description"] = outil.Hint.Length > 0 ? outil.Hint : outil.Name,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = proprietes,
                        ["required"] = obligatoires,
                    },
                },
            });
        }

        return declares;
    }

    /// <summary>Un réglage du manifeste, décrit pour le modèle.</summary>
    private static JsonObject Champ(PluginContentItem champ)
    {
        var decrit = new JsonObject { ["description"] = Description(champ) };

        switch (champ.Kind.ToLowerInvariant())
        {
            case "choice":
                decrit["type"] = "string";
                var valeurs = new JsonArray();

                // Les libellés, pas les valeurs techniques.
                //
                // Une option s'écrit « 180=Ample » : déclarer 180 au modèle lui demanderait de
                // savoir ce que vaut 180 sur une échelle de mouvement, ce que personne ne sait.
                // Il dit « Ample », l'hôte traduit — exactement comme l'utilisateur qui choisit
                // dans une liste.
                foreach (var option in OptionsOutil.Lire(champ.Options))
                {
                    valeurs.Add(option.Libelle);
                }

                decrit["enum"] = valeurs;
                break;

            case "number":
                decrit["type"] = "integer";
                decrit["minimum"] = champ.Min;
                decrit["maximum"] = champ.Max;
                break;

            default:
                decrit["type"] = "string";
                break;
        }

        return decrit;
    }

    private static string Description(PluginContentItem champ)
    {
        var texte = new StringBuilder(champ.Label.Length > 0 ? champ.Label : champ.Id);

        if (champ.Kind.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            texte.Append(" — chemin complet du fichier");

            if (champ.Options.Count > 0)
            {
                texte.Append(", extensions acceptées : ").Append(string.Join(", ", champ.Options));
            }

            // Un chemin ne se devine pas, et un modèle à qui l'on n'a rien dit en invente un. Le
            // fichier inventé n'existe pas, l'outil échoue, et l'échec parle d'un fichier que
            // l'utilisateur n'a jamais nommé — impossible à relier à sa demande. Lui demander le
            // chemin est la seule issue correcte, et il faut le lui dire ici : c'est le seul
            // endroit qu'il lise à propos de ce champ.
            texte.Append(
                ". Demande ce chemin à l'utilisateur s'il ne l'a pas donné ; ne l'invente jamais");
        }

        if (champ.Value.Length > 0)
        {
            // La valeur par défaut est annoncée sous le nom que le modèle emploiera, pas sous celle
            // que le manifeste stocke : lui dire « par défaut : 127 » quand la liste ne propose que
            // « Léger, Modéré, Ample » l'inviterait à répondre 127, qui n'est dans aucune énumération.
            texte.Append(". Valeur par défaut : ")
                .Append(champ.Options.Count > 0
                    ? OptionsOutil.Libelle(champ.Options, champ.Value)
                    : champ.Value);
        }

        // L'explication du manifeste, s'il y en a une. C'est ce qui sépare une étiquette d'un sens :
        // « Mouvement » ne dit pas qu'au-delà de deux cents l'image se déforme, et un modèle qui ne
        // peut pas essayer pour voir n'a que cette phrase pour le savoir.
        if (champ.Hint.Length > 0)
        {
            texte.Append(". ").Append(champ.Hint.TrimEnd('.', ' ')).Append('.');
        }

        return texte.ToString();
    }

    /// <summary>Les arguments produits par le modèle, en valeurs nommées.</summary>
    /// <remarks>
    /// Un objet mal formé rend un dictionnaire vide plutôt qu'une exception : l'outil s'exécutera
    /// avec ses valeurs par défaut, ce qui vaut mieux que de faire échouer toute la conversation.
    /// </remarks>
    private static Dictionary<string, string> Lire(string arguments)
    {
        var lus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (JsonNode.Parse(arguments) is JsonObject objet)
            {
                foreach (var paire in objet)
                {
                    lus[paire.Key] = paire.Value?.ToString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
        }

        return lus;
    }

    /// <summary>Un identifiant d'outil, en un nom que le dialecte accepte.</summary>
    private static string Nom(string identifiant) => identifiant.Replace('-', '_');

    private static JsonObject? Demander(JsonArray messages, JsonArray outils, CancellationToken arret)
    {
        var corps = new JsonObject
        {
            ["messages"] = JsonNode.Parse(messages.ToJsonString()),
            ["max_tokens"] = JetonsMaximum,
        };

        if (outils.Count > 0)
        {
            corps["tools"] = JsonNode.Parse(outils.ToJsonString());
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var contenu = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json");

            var reponse = client
                .PostAsync(ServeurModele.Adresse + "/v1/chat/completions", contenu, arret)
                .GetAwaiter().GetResult();

            if (!reponse.IsSuccessStatusCode)
            {
                return null;
            }

            var lu = reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return JsonNode.Parse(lu) as JsonObject;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
