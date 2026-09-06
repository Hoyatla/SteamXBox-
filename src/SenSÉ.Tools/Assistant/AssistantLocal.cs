using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Plugins;

namespace SenSÉ.Tools.Assistant;

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
    /// Les tours les plus anciens sont oubliés en premier ; la consigne, elle, ne s'oublie jamais.
    ///
    /// <para>
    /// <b>Seize, et le commentaire parlait de huit mille jetons.</b> Le serveur en offre trente-deux
    /// mille depuis des semaines. L'élagage jetait donc ce que le modèle venait d'apprendre pour
    /// faire de la place dont il disposait déjà : c'est ce qui lui faisait redemander trois fois le
    /// même nœud, et redemander à l'utilisateur un sujet donné dix messages plus tôt.
    /// </para>
    ///
    /// <para>
    /// La vraie borne est désormais en jetons — voir <see cref="PartPleine"/>, qui déclenche une
    /// reprise sur un contexte neuf plutôt qu'un oubli silencieux. Ce compte-ci n'est plus qu'un
    /// garde-fou contre un fil qui s'allongerait sans jamais peser, et il est fixé assez haut pour
    /// ne plus jamais couper au milieu d'un travail.
    /// </para>
    /// </remarks>
    private const int MessagesGardes = 48;

    /// <summary>Au-delà, une demande tourne en rond plutôt qu'elle n'aboutit.</summary>
    /// <remarks>
    /// La borne était à quatre, et quatre suffit à une demande simple : appeler un outil, puis
    /// répondre. Elle ne suffit pas à un enchaînement. « Fais une image, anime-la, puis agrandis-la »
    /// demande trois appels et une réponse finale, soit exactement quatre — aucune marge pour une
    /// question de précision, ni pour un outil qui se plaint et qu'il faut rappeler autrement.
    ///
    /// <para>
    /// Huit laisse cette marge sans ouvrir la porte à la boucle : chaque tour coûte une génération
    /// complète, et le modèle qui n'a rien conclu au huitième ne conclura pas au douzième <i>dans
    /// ce fil-ci</i>.
    /// </para>
    ///
    /// <para>
    /// <b>Ce n'est plus une fin, c'est une respiration.</b> La borne atteinte, on ne jette plus
    /// tout en disant « n'a pas abouti en 8 échanges » : le modèle note où il en est, et repart sur
    /// un contexte neuf — voir <see cref="Consolider"/>. Huit tours bornent donc ce qu'un fil peut
    /// accumuler avant d'être résumé, et <see cref="ReprisesMaximum"/> borne le tout.
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

    /// <summary>
    /// La part du contexte au-delà de laquelle on renouvelle plutôt que de continuer.
    /// </summary>
    /// <remarks>
    /// <b>Soixante-dix pour cent, et pas quatre-vingt-quinze.</b> Ce qui reste doit suffire à la
    /// consolidation elle-même : relire le fil, écrire les étapes restantes et les acquis. Attendre
    /// d'être au bord, c'est découvrir qu'il n'y a plus la place de préparer la reprise — et perdre
    /// alors tout ce qu'on voulait sauver.
    ///
    /// <para>
    /// La borne est en jetons réellement consommés, rendus par le serveur à chaque réponse, et non
    /// en nombre de messages : deux tours qui lisent un catalogue de nœuds coûtent plus que vingt
    /// tours de conversation, et c'est le premier cas qui remplit le contexte.
    /// </para>
    /// </remarks>
    private const double PartPleine = 0.70;

    /// <summary>
    /// Combien de fois une même demande peut repartir sur un contexte neuf.
    /// </summary>
    /// <remarks>
    /// Trois. Une demande qui n'aboutit pas en trois contextes pleins ne butte pas sur la place
    /// mais sur autre chose, et continuer coûterait un quart d'heure de carte graphique pour rendre
    /// la même impasse. Ce qui a été fait reste dans le carnet, et l'utilisateur reprend la main.
    /// </remarks>
    private const int ReprisesMaximum = 3;

    /// <summary>Ce que le serveur a compté de jetons au dernier échange.</summary>
    private int _jetons;

    private const string Consigne =
        "Tu assistes l'utilisateur de SenSÉ tout entier, en français — quel que soit le domaine "
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

        + "PARS DU RÉSULTAT DEMANDÉ, JAMAIS D'UN OUTIL. Lis la demande, dis-toi quel FICHIER ou "
        + "quel effet l'utilisateur veut obtenir, puis cherche par quels outils on y arrive. "
        + "« Une vidéo d'un arbre dans le vent » veut dire : un fichier vidéo montrant cela. "
        + "Ce n'est pas une question, c'est une commande — fabrique-la.\n\n"

        + "LES ÉTAPES SONT TON TRAVAIL, PAS LE SIEN. Beaucoup de résultats demandent d'enchaîner "
        + "deux outils. Fabriquer une vidéo à partir d'un texte, par exemple, c'est créer une image "
        + "d'après la description PUIS l'animer — deux appels, un seul travail. Enchaîne-les "
        + "toi-même en passant le fichier produit par le premier au second. Demander à "
        + "l'utilisateur de choisir entre « créer une image » et « animer une image » quand il a "
        + "demandé une vidéo, c'est lui rendre la plomberie qu'il te confie.\n\n"

        + "AGIS, N'ÉNUMÈRE PAS. Une liste d'options écrite dans ta réponse n'est pas une réponse : "
        + "elle renvoie à l'utilisateur le travail de choisir à ta place. S'il te manque UNE "
        + "information, pose UNE question courte. Ne repose jamais la même question sous une autre "
        + "forme, et ne propose pas deux fois la même liste — s'il a déjà répondu, tiens sa réponse "
        + "pour acquise et sers-toi de l'outil qui convient.\n\n"

        + "proposer_choix ne sert QUE devant une vraie bifurcation : deux routes dont les RÉSULTATS "
        + "diffèrent, et dont tu ne peux pas décider à sa place. Choisir entre deux moteurs qui "
        + "rendent la même chose n'en est pas une, et les étapes d'un même travail encore moins. "
        + "Dans le doute, fais — un résultat qu'il n'aime pas se refait ; une question de trop lui "
        + "coûte un aller-retour et lui donne l'impression de piloter à ta place.\n\n"

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
        + "une demi-heure, et cela ne se découvre qu'à la fin."
        + "\n\n"
        + "DEBUG UIA. Tu as CINQ Capacites qui parlent au pc-agent (un subprocess Rust sur 127.0.0.1:8765 qui expose l'API UI Automation de Windows) : "
        + "debug_uia_dump (l'arbre UIA de la fenetre au premier plan : nom, type, automationId, rectangle), "
        + "debug_uia_invoke (clic logique sur un bouton par automationId, 100% fiable, pas de coordonnees), "
        + "debug_uia_set_text (ecrire dans un champ par automationId), "
        + "debug_uia_select (selectionner dans une ComboBox/ListBox par automationId), "
        + "debug_uia_press (combinaison de touches, ex: 'Ctrl+S', 'Return'). "
        + "QUAND l'utilisateur demande une 'capture', un 'screenshot', un 'dump de l'ecran', 'regarde mon ecran', "
        + "ou toute action sur une autre fenetre : utilise debug_uia_dump en premier, "
        + "PAS ouvrir_outil qui ouvre l'outil de capture SenSÉ (different). "
        + "Le pipeline : debug_uia_dump -> lire automationId -> debug_uia_invoke(automationId) ou debug_uia_press(keys). "
        + "C'est 5 a 20 fois plus rapide qu'un clic souris."
        + "\n\n"
        + "CAPTURE FENETRE. Pour 'capture de cette fenetre', 'screenshot de la fenetre X', 'montre-moi la fenetre active': "
        + "utilise debug_uia_screenshot_window avec le titre de la fenetre, "
        + "PAS debug_uia_press(Impr) qui prend tout l'ecran. "
        + "Apres le screenshot, appelle afficher_image avec le chemin recu, "
        + "pour que l'image apparaisse dans la conversation."
        + "Les Capacites qui retournent [IMAGE:chemin] injectent AUTOMATIQUEMENT l'image dans la conversation. "
        + "Tu vois l'image (toi = le modele multimodal), l'user voit l'image (le PNG affiche dans le chat). "
        + "Apres un screenshot, tu peux raisonner sur ce que tu as capture.";

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
    public static string RappelTravaux(Action<string>? journal, bool autonome = false)
    {
        var carnets = FichierTravail.Lister(journal);
        var texte = new StringBuilder(MarqueTravaux).Append(' ').AppendLine(Mode(autonome));

        if (carnets.Count == 0)
        {
            return texte.AppendLine("Aucun travail ouvert.").ToString();
        }

        texte.AppendLine("TRAVAUX OUVERTS :");

        foreach (var carnet in carnets)
        {
            texte.AppendLine(FichierTravail.Resumer(carnet));
        }

        // Un travail commence se reprend, il ne se recommence pas et il ne se redemande pas.
        //
        // Mesure du 24 aout : a « reprends le travail », l'assistant a redemande le sujet de la
        // video que l'utilisateur venait de donner. Le carnet etait sous ses yeux, ses etapes
        // aussi ; ce qui manquait, c'etait la consigne de s'en servir plutot que de repartir de la
        // question.
        var encours = carnets.FirstOrDefault(c => c.Accepte && c.Taches.Exists(t => !t.Faite));

        if (encours is not null)
        {
            var suivante = encours.Taches.Find(t => !t.Faite)?.Texte ?? "";

            texte.AppendLine(
                $"REPRISE : « {encours.Titre} » est commence et accepte. Reprends-le maintenant a "
                + $"l'etape « {suivante} », sans rien redemander de ce que ses ACQUIS portent deja "
                + "et sans refaire une etape cochee. Ne repose une question que si la reponse ne "
                + "figure ni dans les acquis ni dans le fil.");
        }

        return texte.ToString();
    }

    /// <summary>Ce que l'utilisateur attend de l'assistant pour cette demande.</summary>
    /// <remarks>
    /// <b>Deux facons de servir, et l'utilisateur choisit laquelle.</b> Guide, l'assistant montre
    /// les outils qui conviennent et le laisse decider ; autonome, il decide lui-meme et rend
    /// compte. Ce n'est pas un reglage de confort : la meme demande — « une video d'apres un
    /// texte » — appelle un panneau ouvert devant quelqu'un qui veut voir, et un enchainement
    /// silencieux devant quelqu'un qui veut le resultat.
    ///
    /// <para>
    /// Dit a chaque tour, dans le rappel, et non dans la consigne du systeme : celle-ci est
    /// identique d'un tour a l'autre, et c'est ce qui permet au serveur de garder son cache de
    /// prefixe. Un mode qui bascule dans le message systeme invaliderait tout le cache a chaque
    /// changement.
    /// </para>
    /// </remarks>
    private static string Mode(bool autonome)
        => autonome
            ? "MODE AUTONOME. L'utilisateur t'a demandé de faire seul : ne lui repose pas la "
              + "question de l'outil, choisis-le. Avant de commencer, estime l'ampleur — si la "
              + "demande réclame plus de trois appels d'outil, ou plusieurs fichiers, ou des étapes "
              + "qui dépendent les unes des autres, écris d'abord le plan avec travail_noter. Tu "
              + "n'as pas à attendre d'accord dans ce mode : l'utilisateur l'a donné en te confiant "
              + "le travail. Retiens avec travail_retenir tout ce que tu établis en chemin, sinon "
              + "tu le redemanderas. N'interromps l'utilisateur que pour un choix que tu lui "
              + "recommandes, avec proposer_choix."
            : "MODE GUIDÉ. Une demande claire s'exécute, elle ne se met pas aux voix : « une vidéo "
              + "d'un arbre dans le vent » dit exactement ce qu'il veut, alors fabrique-la. "
              + "N'appelle proposer_choix que devant une vraie bifurcation — deux routes dont les "
              + "RÉSULTATS diffèrent, et dont tu ne peux pas décider à sa place — ou s'il manque "
              + "une information que rien ne te permet de déduire. Les étapes d'un même travail ne "
              + "sont jamais une bifurcation. N'écris pas l'option « fais-le toi-même », l'hôte "
              + "l'ajoute lui-même à chaque choix.";

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

    private void Rappeler(Action<string>? journal, bool autonome)
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
            ["content"] = RappelTravaux(journal, autonome),
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
    /// <param name="autonome">
    /// Vrai quand l'utilisateur a demandé que l'assistant fasse seul : il choisit l'outil au lieu
    /// de le proposer, et n'attend pas d'accord sur un plan qu'on lui a déjà confié.
    /// </param>
    public string Repondre(
        string demande,
        IReadOnlyList<PluginManifest> outils,
        IReadOnlyList<Capacite> capacites,
        Executeur executeur,
        Action<string>? journal,
        CancellationToken arret = default,
        bool autonome = false)
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
        Rappeler(journal, autonome);

        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = Vue.Contenu(demande, journal) });
        Elaguer();

        var declares = Declarer(outils, capacites);

        // Ce que le dernier outil a répondu. Si le modèle se tait après l'avoir lancé — il lui
        // arrive d'épuiser sa marge en réflexion — c'est cette phrase qui vaut réponse : elle dit
        // ce qui a réellement été fait, et où.
        var dernierResultat = "";

        // Une seule relance quand le modèle se tait sans rien avoir produit.
        var relance = false;

        // Les tours de ce fil-ci, et le nombre de fils déjà consommés par cette demande.
        var tour = 0;
        var reprises = 0;

        while (true)
        {
            // Les tours épuisés ou la place presque prise : on note où on en est, et on repart.
            //
            // Auparavant la boucle rendait « L'assistant n'a pas abouti en 8 échanges » et jetait
            // tout — le fil, ce qu'il avait appris, le travail à moitié fait. Il n'avait pas
            // échoué, il avait manqué de place ; et la place perdue l'était pour rien.
            if (tour >= ToursMaximum || Plein)
            {
                if (reprises >= ReprisesMaximum || !Consolider(journal, arret, autonome))
                {
                    break;
                }

                reprises++;
                tour = 0;
                relance = false;

                continue;
            }

            tour++;

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

        // Ce qui reste a faire est dans le carnet, et c'est ce qui change tout : la phrase ne dit
        // plus « je n'y arrive pas », elle dit ou en est le travail et comment le relancer.
        var ouvert = FichierTravail.Lister(journal)
            .FirstOrDefault(c => c.Accepte && c.Taches.Exists(t => !t.Faite));

        if (ouvert is not null)
        {
            var faites = ouvert.Taches.Count(t => t.Faite).ToString(CultureInfo.InvariantCulture);
            var total = ouvert.Taches.Count.ToString(CultureInfo.InvariantCulture);

            return (dernierResultat.Length > 0 ? dernierResultat + "\n\n" : "")
                + $"Je m'arrête là pour cette fois : « {ouvert.Titre} » en est à {faites}/{total}. "
                + "Ce qui reste et ce que j'ai établi sont dans le carnet — dites-moi « reprends le "
                + "travail » et je repars de la première étape non cochée.";
        }

        return dernierResultat.Length > 0
            ? dernierResultat
            : $"L'assistant n'a pas abouti en {ToursMaximum.ToString(CultureInfo.InvariantCulture)} échanges.";
    }

    /// <summary>Le contexte est-il assez plein pour qu'il faille le renouveler ?</summary>
    private bool Plein => _jetons > ServeurModele.Contexte * PartPleine;

    /// <summary>
    /// Écrit ce qui reste à faire, puis repart sur un contexte neuf.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Une demande qui n'aboutissait pas en huit échanges rendait
    /// « L'assistant n'a pas abouti en 8 échanges » et jetait tout : le fil, ce qu'il avait appris,
    /// et le travail à moitié fait. Mesuré le 24 août sur la composition d'un flux vidéo — deux fois
    /// de suite, après une vingtaine d'appels d'outil corrects. Le modèle n'avait pas échoué, il
    /// avait manqué de place ; et la place perdue l'était pour rien, puisque tout était à refaire.
    ///
    /// <para>
    /// <b>Ce que la reprise sauve.</b> Le modèle relit son propre fil une dernière fois et en tire
    /// deux choses : ce qui reste à faire, et ce qui est établi. Les deux vont au carnet, qui
    /// survit au contexte parce qu'il est sur le disque. Le fil est alors jeté et remplacé par la
    /// consigne, le carnet, et l'ordre de reprendre — quelques centaines de jetons là où il y en
    /// avait vingt-cinq mille.
    /// </para>
    ///
    /// <para>
    /// C'est la seule compression dont un modèle soit réellement capable ici : lui demander de
    /// résumer sa conversation rendrait un texte, joli et inutilisable ; lui demander l'état de son
    /// travail rend une liste, qu'on peut cocher.
    /// </para>
    ///
    /// <para>
    /// Le carnet est accepté d'office. On ne renouvelle un contexte que sur un travail déjà en
    /// cours : redemander l'accord d'un plan que l'utilisateur a lancé lui-même l'obligerait à
    /// approuver deux fois la même chose, et arrêterait net une reprise censée être invisible.
    /// </para>
    /// </remarks>
    /// <returns>Vrai si la reprise est préparée et que la boucle peut repartir.</returns>
    private bool Consolider(Action<string>? journal, CancellationToken arret, bool autonome)
    {
        if (arret.IsCancellationRequested)
        {
            return false;
        }

        journal?.Invoke("Contexte plein : l'assistant note où il en est et repart sur un fil neuf.");

        var demande = new JsonArray();

        foreach (var message in _messages)
        {
            demande.Add(JsonNode.Parse(message!.ToJsonString())!);
        }

        demande.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] =
                "Ta place de travail est presque pleine et va etre renouvelee. N'appelle aucun "
                + "outil. Rends exactement trois lignes, et rien d'autre :\n"
                + "TITRE: le titre du travail en cours, ou un titre court pour ce qui est demande\n"
                + "RESTE: les etapes qu'il reste a faire, separees par des points-virgules\n"
                + "ACQUIS: ce qui est deja etabli et qu'il ne faudra pas redemander — ce que "
                + "l'utilisateur a dit, les noms exacts trouves, les valeurs choisies — separes par "
                + "des points-virgules",
        });

        var reponse = Demander(demande, [], arret);
        var dit = Ecrit((reponse?["choices"]?[0]?["message"] as JsonObject)?["content"]);

        if (dit.Length == 0)
        {
            return false;
        }

        var titre = Ligne(dit, "TITRE:");
        var reste = Morceaux(Ligne(dit, "RESTE:"));
        var acquis = Morceaux(Ligne(dit, "ACQUIS:"));

        if (titre.Length == 0 || reste.Count == 0)
        {
            // Sans etapes restantes, il n'y a rien a reprendre : mieux vaut rendre la main avec ce
            // qui a ete fait que repartir sur un fil neuf pour tourner en rond dedans.
            return false;
        }

        FichierTravail.Noter(titre, reste, journal);
        FichierTravail.Accepter(titre, journal);

        if (acquis.Count > 0)
        {
            FichierTravail.Retenir(titre, acquis, journal);
        }

        // Le fil est jete, la consigne gardee : c'est elle qui dit au modele ce qu'il est, et elle
        // ne depend d'aucun tour.
        var consigne = _messages[0];

        _messages.Clear();
        _messages.Add(consigne!);
        _jetons = 0;

        // Puis le carnet, et l'ordre de reprendre. Sans eux le fil neuf serait muet : le modele
        // recevrait sa consigne et rien a quoi repondre. C'est le rappel qui porte les etapes
        // restantes, les acquis et la ligne REPRISE ; le message qui suit ne fait que rendre la
        // main, parce qu'un tour se termine par une demande et non par un constat.
        Rappeler(journal, autonome);

        _messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = "Reprends maintenant, a la premiere etape non cochee. Ne redemande rien "
                + "de ce que les acquis portent deja, et n'annonce pas que tu reprends : fais-le.",
        });

        journal?.Invoke($"Reprise sur « {titre} » : {reste.Count} etape(s) restante(s).");

        return true;
    }

    /// <summary>La suite d'une ligne qui commence par cette etiquette, dans un texte.</summary>
    private static string Ligne(string texte, string etiquette)
    {
        foreach (var ligne in texte.Split('\n'))
        {
            var propre = ligne.Trim();

            if (propre.StartsWith(etiquette, StringComparison.OrdinalIgnoreCase))
            {
                return propre[etiquette.Length..].Trim();
            }
        }

        return "";
    }

    /// <summary>Une liste separee par des points-virgules, nettoyee de ses vides.</summary>
    private static IReadOnlyList<string> Morceaux(string ligne)
        => [.. ligne.Split(';').Select(m => m.Trim()).Where(m => m.Length > 0)];

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

        // Detection d'un marqueur [IMAGE:chemin] dans le resultat d'une
        // Capacite. Si trouve, on charge le PNG, on l'encode en base64, et
        // on transforme le "content" du message tool en un tableau
        // multimodal (text + image_url data:base64) que le modele
        // multimodal Qwen VL peut voir et que le client peut afficher.
        System.Text.Json.Nodes.JsonNode? contentNode = resultat;
        if (resultat.Contains("[IMAGE:"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(resultat, @"\[IMAGE:(.+?)\]");
            if (m.Success)
            {
                var chemin = m.Groups[1].Value.Trim();
                if (System.IO.File.Exists(chemin))
                {
                    try
                    {
                        var bytes = System.IO.File.ReadAllBytes(chemin);
                        var base64 = Convert.ToBase64String(bytes);
                        var mime = (chemin.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                            || chemin.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                            ? "image/jpeg" : "image/png";
                        var texteRestant = resultat.Replace(m.Value, "").Trim();
                        contentNode = new System.Text.Json.Nodes.JsonArray
                        {
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = string.IsNullOrEmpty(texteRestant)
                                    ? "image capturee par l'Assistant"
                                    : texteRestant,
                            },
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new System.Text.Json.Nodes.JsonObject
                                {
                                    ["url"] = $"data:{mime};base64,{base64}",
                                },
                            },
                        };
                    }
                    catch (Exception ex)
                    {
                        journal?.Invoke($"[IMAGE] injection echouee pour {chemin}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        return new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = identifiant,
            ["content"] = contentNode,
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
                // Un choix qui porte des recettes tire ses valeurs de LEURS libellés.
                //
                // Sans cela l'énumération sortait vide — les recettes ne sont pas des options — et
                // le modèle inventait : « SVD », « Wan 2.2 », puis un nom de fichier de modèle,
                // trois essais dont aucun ne pouvait aboutir. Une énumération vide est pire que pas
                // d'énumération du tout : elle promet une contrainte qu'elle n'exprime pas.
                if (champ.Recettes.Count > 0)
                {
                    foreach (var recette in champ.Recettes)
                    {
                        valeurs.Add(recette.Label.Length > 0 ? recette.Label : recette.Id);
                    }
                }
                else
                {
                    foreach (var option in OptionsOutil.Lire(champ.Options))
                    {
                        valeurs.Add(option.Libelle);
                    }
                }

                if (valeurs.Count > 0)
                {
                    decrit["enum"] = valeurs;
                }

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

    /// <remarks>
    /// Relève au passage ce que l'échange a coûté. Le serveur rend ce compte dans <c>usage</c> :
    /// c'est la seule mesure exacte de ce qui reste, et l'estimer à la longueur des messages se
    /// tromperait d'un facteur trois sur un catalogue de nœuds.
    /// </remarks>
    private JsonObject? Demander(JsonArray messages, JsonArray outils, CancellationToken arret)
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

            if (JsonNode.Parse(lu) is not JsonObject rendu)
            {
                return null;
            }

            // Le compte du serveur, quand il le donne. Absent, l'ancien est garde plutot que remis
            // a zero : croire le contexte vide parce qu'une reponse n'a pas porte son compte
            // repousserait la consolidation au moment ou elle n'a plus la place de se faire.
            if (rendu["usage"]?["total_tokens"]?.GetValue<int>() is { } compte and > 0)
            {
                _jetons = compte;
            }

            return rendu;
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

    /// <summary>
    /// Variante UN-tour de <see cref="Repondre"/>, declenchee par un
    /// observateur proactif. Pas de boucle, pas d'outils MCP dans cette
    /// premiere iteration : juste un appel synchrone au modele avec la
    /// demande, qui rend la reponse texte.
    /// </summary>
    /// <remarks>
    /// <b>Methode prevue pour etre etendue.</b> Quand les outils MCP
    /// seront injectes comme <see cref="Capacite"/>, ils prendront leur
    /// place ici sans rien changer au reste.
    /// </remarks>
    public Task<string> RepondreProactifAsync(string raison, CancellationToken arret = default)
    {
        // On reutilise Repondre avec des listes vides : un seul tour,
        // un seul message, l'Assistant rend sa reaction textuelle.
        // La grande consigne systeme reste, elle n'est pas genante pour
        // une reaction courte.
        var reponse = Repondre(
            demande: raison,
            outils: [],
            capacites: [],
            executeur: (_, _) => "",
            journal: null,
            arret: arret,
            autonome: true);

        return Task.FromResult(reponse);
    }

    /// <summary>
    /// Dump de toute la conversation au format Markdown, pour la memoire long terme.
    /// Chaque message est horodate. Le sujet est extrait du premier message user
    /// (premiers 60 caracteres).
    /// </summary>
    public string DumpConversationMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        var messages = _messages;
        var debut = DateTime.Now;
        sb.AppendLine($"# Session {debut:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        string? sujet = null;
        var count = 0;
        foreach (var noeud in messages)
        {
            if (noeud is not System.Text.Json.Nodes.JsonObject obj) continue;
            var role = obj["role"]?.GetValue<string>();
            if (role is null or "system") continue;
            count++;
            var contenu = obj["content"];
            string texte;
            if (contenu is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var t)) texte = t;
            else if (contenu is System.Text.Json.Nodes.JsonArray arr)
            {
                var parts = new List<string>();
                foreach (var item in arr)
                {
                    if (item is System.Text.Json.Nodes.JsonObject io && io["text"] is System.Text.Json.Nodes.JsonValue iv && iv.TryGetValue<string>(out var it)) parts.Add(it);
                }
                texte = string.Join(" ", parts);
            }
            else texte = "";
            if (role == "user" && sujet is null && texte.Length > 0) sujet = texte.Length > 60 ? texte.Substring(0, 60) : texte;
            var label = role == "user" ? "Vous" : "Assistant";
            sb.AppendLine($"**{label}**");
            sb.AppendLine();
            sb.AppendLine(texte);
            sb.AppendLine();
        }
        if (sujet is not null) sb.Insert(sb.ToString().IndexOf('\n') + 1, $"Sujet: {sujet}\n\n");
        sb.Insert(sb.ToString().IndexOf('\n') + 1, $"Messages: {count}\n\n");
        return sb.ToString();
    }
}
