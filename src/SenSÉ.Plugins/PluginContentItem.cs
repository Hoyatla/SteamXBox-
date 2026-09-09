namespace SenSÉ.Plugins;

/// <summary>
/// Une façon de faire le travail d'un outil : un graphe, ses liaisons, et ce qu'elle réclame.
/// </summary>
/// <param name="Id">Ce que le panneau retient, et ce que la cible d'une action nomme.</param>
/// <param name="Label">Ce que l'utilisateur lit dans le menu.</param>
/// <param name="Exige">
/// Les fichiers de modèle, relatifs au dossier des modèles du générateur, sans lesquels la recette
/// ne peut pas tourner. Vide veut dire « toujours disponible ».
/// </param>
/// <param name="Target">
/// La cible que l'action emploiera si cette recette est choisie : le graphe, puis ses réglages,
/// exactement comme une cible ordinaire. Elle peut nommer les valeurs du panneau en <c>{id}</c>.
/// </param>
public sealed class RecetteOutil
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public List<string> Exige { get; set; } = [];

    public string Target { get; set; } = "";
}

/// <summary>One element of what the host draws for a tool.</summary>
/// <remarks>
/// A vocabulary, not a layout language. The tool names what it contains; where those things go is
/// the host's business, which is what lets a tool written by a user be navigable with a controller
/// without its author having thought about it.
///
/// <para>
/// It grows when a real tool asks for something, never in anticipation. The line not to cross is
/// written in <c>Plugins/README.md</c>: the moment a tool needs to <i>compute</i> something the host
/// cannot name, it belongs to the next level and does not get one more <c>does</c>.
/// </para>
/// </remarks>
public sealed class PluginContentItem
{
    /// <summary>What it is: <c>text</c>, <c>number</c>, <c>choice</c> or <c>action</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Names the value, for <c>remembers</c> and for the action that reads it.</summary>
    public string Id { get; set; } = "";

    /// <summary>Shown beside it.</summary>
    public string Label { get; set; } = "";

    /// <summary>Starting value, as text whatever the kind.</summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// Ce que ce réglage fait vraiment, pour qui le lit — l'utilisateur comme le modèle.
    /// </summary>
    /// <remarks>
    /// Les manifestes en portaient déjà treize, écrits avec soin, et aucun n'était lu : la
    /// propriété n'existait pas, et un champ inconnu est ignoré en silence à la lecture du JSON. Le
    /// panneau montrait donc « Mouvement » sans dire qu'au-delà de 200 l'image se déforme, et le
    /// modèle recevait le même mot nu.
    ///
    /// <para>
    /// C'est ce qui distingue une étiquette d'une explication, et c'est ce dont un modèle à contexte
    /// réduit a le plus besoin : il ne peut pas essayer pour voir.
    /// </para>
    /// </remarks>
    public string Hint { get; set; } = "";

    /// <summary>
    /// Les recettes entre lesquelles ce choix arbitre : un graphe et ses liaisons par famille.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige, et il était structurel.</b> Un outil de génération nommait UN
    /// graphe, avec les numéros de ses nœuds recopiés dans la cible. Ce graphe est propre à une
    /// famille de modèles — celui de « Animer une image » charge par
    /// <c>ImageOnlyCheckpointLoader</c> et conditionne par <c>SVD_img2vid_Conditioning</c>, ce qui
    /// n'a de sens que pour SVD. Le menu « Modèle » à côté ne pouvait donc échanger qu'un SVD
    /// contre un autre SVD : les soixante gigaoctets de MiniMax et de Wan posés sur ce disque
    /// n'étaient atteignables par aucun outil de la grille.
    ///
    /// <para>
    /// Une recette déplace le graphe du côté du choix. Choisir « Wan 2.2 » ne change plus un nom de
    /// fichier dans un graphe SVD : cela change le graphe. C'est la seule forme qui rende le menu
    /// honnête, et elle ne coûte rien à la propriété qui compte — le manifeste décrit toujours, et
    /// l'hôte exécute toujours.
    /// </para>
    ///
    /// <para>
    /// <b>Ce que la machine décide, et pas le manifeste.</b> Une recette déclare ce qu'elle exige ;
    /// l'hôte regarde le disque et n'offre que celles qui peuvent tourner. Un client à douze
    /// gigaoctets et un client à vingt-quatre ouvrent le même outil et n'y voient pas la même
    /// liste — sans qu'aucun fichier ait été édité, et sans qu'on lui propose jamais un modèle qui
    /// échouera au chargement.
    /// </para>
    /// </remarks>
    public List<RecetteOutil> Recettes { get; set; } = [];

    public int Min { get; set; }

    public int Max { get; set; } = 100;

    /// <summary>The options, for a <c>choice</c>.</summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// Où l'hôte va chercher les options d'un <c>choice</c>, au lieu de les lire dans le manifeste.
    /// </summary>
    /// <remarks>
    /// <b>Le champ existe pour que le produit survive au changement de modèles.</b> Un manifeste qui
    /// énumère des noms de fichiers gèle l'installation du jour où il a été écrit : le client qui
    /// remplace un modèle par un autre doit éditer des fichiers, et celui qui oublie découvre des
    /// mois plus tard que l'outil réclamait un fichier disparu. C'est arrivé, et sans bruit — deux
    /// flux de cette machine nommaient encore des modèles retirés depuis longtemps.
    ///
    /// <para>
    /// S'écrit <c>NomDuNoeud.nom_de_l_entree</c>, par exemple
    /// <c>CheckpointLoaderSimple.ckpt_name</c>. L'hôte demande alors au générateur ce qu'il accepte
    /// réellement pour cette entrée, ce qui est la liste des fichiers présents sur ce disque.
    /// </para>
    ///
    /// <para>
    /// Les <c>options</c> restent utiles à côté : elles servent de repli quand le générateur ne
    /// répond pas. Un panneau qui s'ouvre vide serait pire qu'un panneau qui s'ouvre périmé.
    /// </para>
    /// </remarks>
    public string From { get; set; } = "";

    /// <summary>
    /// Une conséquence plutôt qu'un choix : rangée derrière « Réglages avancés ».
    /// </summary>
    /// <remarks>
    /// <b>Un réglage dont on écrit « à laisser tel quel » n'a rien à faire au premier plan.</b> Le
    /// panneau de « Créer une image » posait neuf questions, dont trois portaient cette phrase :
    /// l'encodeur de texte, le second encodeur, le VAE. Ce ne sont pas des choix — si l'on prend
    /// Flux, il n'existe qu'une combinaison valide sur la machine. Les montrer au même rang que la
    /// description invitait à casser quelque chose sans rien offrir en échange.
    ///
    /// <para>
    /// Rangé, jamais supprimé. Le champ reste une liste vivante lue sur la machine, donc le garde
    /// contre les noms figés continue de s'appliquer, et l'assistant continue de le voir. Seul
    /// l'affichage change : celui qui sait ce qu'il fait déplie et règle.
    /// </para>
    /// </remarks>
    public bool Avance { get; set; }

    /// <summary>
    /// Pour une <c>action</c> : le réglage à retirer au sort avant de lancer.
    /// </summary>
    /// <remarks>
    /// <b>Le bouton « Autre proposition ».</b> Mesuré sur cinq clips : à longueur et mouvement
    /// égaux, deux graines différentes donnent l'une une animation intacte, l'autre un personnage
    /// dissous. C'est le levier qui agit le plus sur ce qui casse réellement — bien plus que le
    /// mouvement, qui ne changeait presque rien.
    ///
    /// <para>
    /// Or une graine est le réglage le plus incompréhensible du panneau : « 42 » ne veut rien dire
    /// à personne. Le besoin derrière, lui, se dit en trois mots — « refais-en un autre » — et
    /// c'est un bouton, pas un champ. Le champ reste, rangé en avancé, pour qui veut retrouver
    /// exactement une image déjà obtenue.
    /// </para>
    /// </remarks>
    public string Hasard { get; set; } = "";

    /// <summary>For an <c>action</c>: what the host does, and to what.</summary>
    public string Does { get; set; } = "";

    public string Target { get; set; } = "";
}

/// <summary>
/// What a declarative tool may ask the host to do.
/// </summary>
/// <remarks>
/// Every entry answers the question the contract sets: <i>does the host already do this for
/// itself?</i> Opening a Windows settings page, launching an application, opening a path and
/// clearing the screen are all things the environment does on its own tiles and shortcuts, so
/// exposing them takes nothing new into the manifest.
///
/// <para>
/// Nothing here computes. A tool that needs a value worked out — a timer counting down, a
/// calculator evaluating an expression — is level two, and adding a <c>does</c> for it is how a
/// manifest turns into a language with no designer.
/// </para>
/// </remarks>
public static class PluginActions
{
    /// <summary>Opens a Windows settings page; the target is its <c>ms-settings</c> name.</summary>
    public const string WindowsSetting = "windows-setting";

    /// <summary>Launches an application; the target is its executable name.</summary>
    public const string Application = "application";

    /// <summary>Opens a file, a folder or an address; the target is the path.</summary>
    public const string Path = "path";

    /// <summary>Opens the search launcher.</summary>
    public const string Search = "search";

    /// <summary>Clears the screen, or puts the windows back.</summary>
    public const string ClearScreen = "clear-screen";

    /// <summary>
    /// Converts a document the user designated into another format.
    /// </summary>
    /// <remarks>
    /// The first action of the second level, and it obeys the same test as the first level's: the
    /// host already converts documents — the indexer has read the old Office and OpenDocument
    /// formats through LibreOffice for weeks, with filter names measured on real hardware rather
    /// than taken from documentation.
    ///
    /// <para>
    /// So "pdf to word" needs no code from the tool and no arbitrary program to run. That matters
    /// more than the convenience: the property that makes a downloaded manifest safe to install —
    /// it can do no more than what it shows to whoever reads it — survives intact.
    /// </para>
    /// </remarks>
    public const string Convert = "convert";

    /// <summary>
    /// Lance un script Python detecte sur la machine ; la cible est
    /// <c>chemin\du\script.py|arguments</c>.
    /// </summary>
    /// <remarks>
    /// Le premier verbe qui execute un programme que le produit ne fournit pas. Il existe parce que
    /// les outils qui comptent aujourd'hui — un agrandisseur d'images, une interface de generation —
    /// sont des projets Python, et qu'aucun des verbes precedents ne sait exprimer « cet interpreteur,
    /// ce script, ces arguments ».
    ///
    /// <para>
    /// <b>Ce qu'il coute a la propriete qui rendait un manifeste sur.</b> Un manifeste ne pouvait
    /// jusqu'ici rien faire de plus que ce qu'il montrait a qui le lisait ; celui-ci nomme un script
    /// que le lecteur doit aller lire lui-meme. C'est assume et borne : le script doit exister, finir
    /// par .py, et l'appel est journalise. Un outil qui nomme ce verbe est un outil auquel on accorde
    /// plus de confiance qu'aux autres, et cela doit se voir.
    /// </para>
    ///
    /// <para>
    /// Rien n'est embarque : ni Python ni les projets appeles. Ils sont detectes, comme LibreOffice
    /// et tesseract, et le verbe se refuse en le disant quand l'interpreteur manque. C'est aussi ce
    /// qui garde ComfyUI, sous licence GPL, du bon cote de la frontiere : appele, jamais livre.
    /// </para>
    /// </remarks>
    public const string Python = "python";

    /// <summary>
    /// Agrandit une vidéo et change sa cadence ; la cible est
    /// <c>video|modele|echelle|cadence|qualite|format</c>.
    /// </summary>
    /// <remarks>
    /// Le chemin inverse de <see cref="Python"/>, et c'est tout l'intérêt. Le même travail passait
    /// par un script que le lecteur du manifeste devait aller lire lui-même, et par un interpréteur
    /// de neuf gigaoctets et demi installé par l'utilisateur. Il est maintenant dans le produit :
    /// le manifeste ne nomme plus que des réglages, la propriété qui rendait un manifeste sûr —
    /// il ne peut rien faire de plus que ce qu'il montre — est rendue à cet outil.
    ///
    /// <para>
    /// Les deux moteurs qu'il appelle sont compilés, autonomes et livrés avec le produit : BSD-3
    /// pour Real-ESRGAN et ncnn, MIT pour RIFE. Aucun n'exige quoi que ce soit de la machine.
    /// </para>
    /// </remarks>
    public const string Video = "video";

    /// <summary>
    /// Agrandit une image ; la cible est <c>image|modele|echelle|format</c>.
    /// </summary>
    /// <remarks>
    /// Le pendant de <see cref="Video"/>, et pour la même raison. Les formats que le moteur ne lit
    /// pas — bmp, tiff, avif — sont convertis avant et reconvertis après, pour que la liste
    /// d'extensions affichée soit la liste des extensions réellement traitées.
    /// </remarks>
    public const string Image = "image";

    /// <summary>
    /// Démarre le serveur de génération d'images et ouvre son interface. Sans cible.
    /// </summary>
    /// <remarks>
    /// Le seul verbe qui ne produit pas un fichier mais une page. Il existe parce que démarrer un
    /// serveur puis deviner quand il répond n'est pas un travail d'utilisateur : le produit attend
    /// à sa place, puis ouvre l'interface. ComfyUI reste appelé comme programme séparé et n'est
    /// jamais livré — sa licence est GPL v3.
    /// </remarks>
    public const string Generation = "generation";

    /// <summary>
    /// Ouvre la conversation avec l'assistant local. Sans cible.
    /// </summary>
    /// <remarks>
    /// Le verbe qui referme la boucle : l'assistant nomme les outils, et les outils sont décrits
    /// par les manifestes que ce même vocabulaire définit. Rien ne lui est accordé au-delà — il
    /// remplit les réglages d'un outil installé, l'hôte exécute, exactement comme si l'utilisateur
    /// avait cliqué.
    /// </remarks>
    public const string Assistant = "assistant";

    /// <summary>
    /// Ouvre le moniteur d'activité : ce qui tourne au nom du produit. Sans cible.
    /// </summary>
    /// <remarks>
    /// Le produit lance des programmes qui lui survivent et qui héritent de ses droits élevés :
    /// ni le gestionnaire de tâches ordinaire ni une console normale ne peuvent les arrêter. Ce
    /// verbe donne la seule vue où ils sont nommés, classés, et arrêtables.
    /// </remarks>
    public const string Activite = "activite";

    /// <summary>
    /// Exécute un flux de travail de génération ; la cible est le flux, puis ses réglages.
    /// </summary>
    /// <remarks>
    /// <b>Choisir un flux plutôt que le composer.</b> Un modèle de langage local ne sait pas
    /// assembler un graphe de génération — choisir les nœuds, les câbler, prendre le bon
    /// échantillonneur — et ce qu'il produit alors est du JSON vraisemblable qui ne s'exécute pas.
    /// Il sait en revanche très bien lire une déclaration et remplir des champs : c'est ce que fait
    /// déjà tout manifeste. Ce verbe déplace donc le flux du côté des outils, où il est éprouvé une
    /// fois pour toutes, et ne laisse au modèle que la sélection et les réglages.
    ///
    /// <para>
    /// La cible s'écrit <c>chemin\du\flux.json|nœud.entrée=valeur|…</c>, un réglage par segment. Le
    /// préfixe <c>!</c> rend le réglage obligatoire ; sans lui, un champ laissé vide conserve
    /// simplement la valeur inscrite dans le flux. Les correspondances sont donc <i>dans</i> le
    /// manifeste, lisibles par qui l'ouvre — la propriété qui rend un manifeste sûr est conservée.
    /// </para>
    ///
    /// <para>
    /// Le flux est au format API de ComfyUI, celui qu'accepte son point d'entrée <c>/prompt</c> :
    /// des nœuds numérotés portant chacun un <c>class_type</c> et ses <c>inputs</c>. C'est ce
    /// qu'exporte « Save (API format) », et non le format de l'éditeur.
    /// </para>
    /// </remarks>
    public const string Flux = "flux";

    /// <summary>
    /// Ouvre le choix des jeux de modèles. Sans cible.
    /// </summary>
    /// <remarks>
    /// Le produit dépend de modèles, et ces modèles changent au besoin des clients : celui qui a
    /// une carte de 8 Go et celui qui en a 24 n'installeront pas les mêmes, et aucun des deux ne
    /// doit avoir à le deviner. Sans cet écran il ne reste que deux issues — un produit qui ne
    /// tourne que sur la machine de son auteur, ou un client qui télécharge treize gigaoctets pour
    /// découvrir que sa carte ne les prend pas.
    /// </remarks>
    public const string Modeles = "modeles";

    /// <summary>
    /// Réunit plusieurs outils en un classeur à onglets ; la cible est leurs identifiants,
    /// séparés par <c>|</c>.
    /// </summary>
    /// <remarks>
    /// <b>Le seul verbe qui parle d'autres outils.</b> Créer une image, l'animer, l'agrandir sont
    /// trois moments d'un même travail et non trois outils qu'on choisit indépendamment ; trois
    /// tuiles obligeaient à fermer l'une pour ouvrir la suivante, et à retrouver à la main le
    /// fichier que la précédente venait de produire.
    ///
    /// <para>
    /// Les outils réunis restent des manifestes ordinaires, installables et désinstallables un par
    /// un : seul l'endroit où ils s'affichent change. Ils disparaissent en revanche de la grille,
    /// puisque le classeur les y remplace — et c'est la cible qui dit lesquels, de sorte qu'il n'y
    /// a pas deux listes à tenir d'accord.
    /// </para>
    /// </remarks>
    public const string Atelier = "atelier";

    /// <summary>Lance l'editeur de texte SenSÉ (Process.Start sur Outils/Editeur/SenSÉ.Editeur.exe).</summary>
    public const string Editor = "editeur";

    /// <summary>
    /// Anime chaque image d'un dossier et recolle les clips ; la cible est
    /// <c>dossier|ponts|mouvement|rendu|prefixe|flux</c>.
    /// </summary>
    /// <remarks>
    /// <b>Le seul verbe qui boucle.</b> Les autres exécutent une chose ; celui-ci en exécute une par
    /// image, puis assemble. C'est ce qui permet de dépasser les deux secondes et demie qu'un clip
    /// SVD tient : plusieurs images choisies, chacune animée brièvement, mises bout à bout.
    ///
    /// <para>
    /// Le pont entre deux images est volontairement le plus court possible. Ce que le modèle invente
    /// après une image, il l'invente sans viser la suivante — aucun modèle d'aujourd'hui ne sait
    /// rejoindre une image cible. Plus le pont est court, moins la dérive s'installe, et plus la
    /// coupe passe inaperçue. Deux maillons au maximum : le troisième a été généré et regardé, il
    /// perdait le visage et l'anatomie.
    /// </para>
    /// </remarks>
    public const string Sequence = "sequence";

    /// <summary>Les identifiants qu'une cible de classeur réunit, dans l'ordre d'affichage.</summary>
    /// <remarks>
    /// L'ordre compte : c'est celui dans lequel on travaille — créer, animer, agrandir — et le
    /// manifeste du classeur est le seul endroit où quelqu'un l'a décidé.
    /// </remarks>
    public static IReadOnlyList<string> Reunis(string? cible)
        => (cible ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Every action a manifest may name.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        WindowsSetting, Application, Path, Search, ClearScreen, Convert, Python, Video, Image, Generation,
        Assistant, Activite, Editor, Flux, Modeles, Atelier, Sequence,
    };

    /// <summary>Whether the action needs something to act on.</summary>
    public static bool NeedsTarget(string does)
        => does.Equals(WindowsSetting, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Application, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Path, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Convert, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Python, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Video, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Image, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Flux, StringComparison.OrdinalIgnoreCase)

           // Un classeur sans les outils qu'il réunit serait une fenêtre à onglets vide.
           || does.Equals(Atelier, StringComparison.OrdinalIgnoreCase)

           // Une séquence sans dossier d'images n'aurait rien à animer.
           || does.Equals(Sequence, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The actions that only a tool with a panel can name.
    /// </summary>
    /// <remarks>
    /// Converting needs a document, and a document is designated by the user in a panel. A tile has
    /// no panel, so a tile that named this action would be a button that could never do anything.
    /// Enlarging a video is the same case, for the same reason.
    /// </remarks>
    public static bool NeedsPanel(string does)
        => does.Equals(Convert, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Video, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Image, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Flux, StringComparison.OrdinalIgnoreCase)
           || does.Equals(Sequence, StringComparison.OrdinalIgnoreCase);
}
