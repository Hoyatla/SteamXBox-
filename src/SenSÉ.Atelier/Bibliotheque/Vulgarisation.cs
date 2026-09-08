using System.Collections.Generic;

namespace SenSÉ.Atelier.Bibliotheque;

/// <summary>
/// Mapping des labels vulgarises et des tooltips explicatifs pour les
/// noeuds et les parametres de l'Atelier.
/// </summary>
/// <remarks>
/// <b>Le parti pris.</b> Le nom technique (<c>executer_code</c>) reste la
/// verite de la serialisation JSON, de l'API HTTP et de la Capacite
/// Assistant. Mais cote UI, l'utilisateur voit un label en francais
/// (<c>"Executer du code"</c>) et un tooltip qui explique ce que ca fait
/// sans jargon. C'est ce mapping qui fait la transition.
///
/// <para><b>Ou vit la verite.</b> Pour ajouter un nouveau noeud, ajouter
/// aussi son entree ici. Le fallback (Nom + Description + NomVulgarise
/// = Nom) marche mais laisse l'UI sans tooltip : c'est volontaire, pour
/// que les oublis soient visibles.</para>
///
/// <para><b>Ton.</b> On parle a un humain. Pas de jargon, pas de phrase
/// d'accroche marketing, pas de troisieme personne corporate. Les
/// phrases d'exemple sont concretes (un fichier, un dossier, une voix)
/// plutot qu'abstraites (un actif, une ressource, un parametre).</para>
/// </remarks>
public static class Vulgarisation
{
    /// <summary>
    /// Retourne le label vulgarise et le tooltip d'un type de noeud a partir
    /// de son id technique. Renvoie null si l'id n'est pas reference : le
    /// rendu UI fallback alors sur Nom + Description.
    /// </summary>
    public static (string NomVulgarise, string DescriptionLongue)? LookupNoeud(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _noeuds.TryGetValue(id, out var v) ? v : null;
    }

    /// <summary>
    /// Retourne le label vulgarise et le tooltip d'un parametre a partir
    /// de son nom technique (celui qu'on met dans <c>noeud.Params["..."]</c>).
    /// </summary>
    public static (string NomVulgarise, string DescriptionLongue)? LookupParametre(string nom)
    {
        if (string.IsNullOrEmpty(nom)) return null;
        return _params.TryGetValue(nom, out var v) ? v : null;
    }

    // ============== NOEUDS ==============

    private static readonly Dictionary<string, (string, string)> _noeuds = new()
    {
        // ----- Codage : primitives -----
        ["texte"] = (
            "Note",
            "Une boite ou tu ecris du texte libre. Sert a donner une consigne ou une phrase aux noeuds suivants."),
        ["nombre"] = (
            "Nombre",
            "Une valeur numerique. Tu peux choisir entre un minimum et un maximum."),
        ["booleen"] = (
            "Interrupteur",
            "Un interrupteur : oui ou non. Sert a activer ou desactiver une branche du graphe."),
        ["fichier_lire"] = (
            "Ouvrir un fichier",
            "Ouvre un fichier sur ton disque et donne son contenu aux noeuds suivants."),
        ["fichier_ecrire"] = (
            "Sauver dans un fichier",
            "Ecrit le texte qu'il recoit dans un fichier sur ton disque. Tu choisis ou."),
        ["lister_fichiers"] = (
            "Lister des fichiers",
            "Liste tous les fichiers d'un dossier, avec un motif pour filtrer (ex: *.txt)."),
        ["concatener"] = (
            "Fusionner des textes",
            "Colle plusieurs textes bout a bout, avec un separateur entre chaque."),

        // ----- Codage : execution code -----
        ["executer_code"] = (
            "Lancer du code",
            "Fait tourner du code source. Choisis le langage, ecris ton code, et appuie sur F5 pour executer. Le resultat (sortie, erreurs, duree) apparait dans la console en bas du canvas."),
        ["executer_commande"] = (
            "Lancer une commande",
            "Execute une commande systeme (ligne de commande). Avance : peut faire beaucoup de choses, mais une coquille et c'est un autre programme qui tourne."),

        // ----- Codage : LLM (10) -----
        ["llm_generer_code"] = (
            "Ecrire du code",
            "Decris ce que tu veux que le code fasse, le modele local l'ecrit pour toi dans le langage que tu choisis."),
        ["llm_completer_code"] = (
            "Completer du code",
            "Le modele local complete ton code la ou il s'arrete, en respectant le style et le langage."),
        ["llm_expliquer_code"] = (
            "Expliquer du code",
            "Le modele local te dit en francais ce que fait ce code, ligne par ligne ou en bloc."),
        ["llm_reviser_code"] = (
            "Relire du code",
            "Le modele local relit ton code et propose une version amelioree (lisibilite, perfs, securite)."),
        ["llm_generer_tests"] = (
            "Ecrire des tests",
            "Le modele local ecrit des tests pour ton code, dans le langage et le framework que tu choisis."),
        ["llm_traduire_code"] = (
            "Traduire du code",
            "Le modele local traduit ton code d'un langage a un autre (Python vers Rust, par exemple)."),
        ["llm_documenter"] = (
            "Documenter du code",
            "Le modele local ajoute des commentaires et de la documentation a ton code."),
        ["llm_refactorer"] = (
            "Ameliorer du code",
            "Le modele local reorganise ton code sans changer ce qu'il fait, pour le rendre plus propre."),
        ["llm_generer_nom"] = (
            "Suggerer un nom",
            "Le modele local propose un nom de variable ou de fonction adapte a ce que tu decris."),
        ["llm_repondre_question"] = (
            "Demander a l'IA",
            "Le modele local repond a ta question, en utilisant le contexte que tu lui donnes."),

        // ----- Codage : MCP saisie / CDP (6) -----
        ["mcp_saisie_screenshot"] = (
            "Capturer l'ecran",
            "Prend une capture d'ecran (ou d'une fenetre precise) et la transmet aux noeuds suivants."),
        ["mcp_saisie_cliquer"] = (
            "Cliquer",
            "Clique a un endroit precis de l'ecran. Sert a interagir avec une autre application qui n'a pas d'API."),
        ["mcp_saisie_taper"] = (
            "Taper du texte",
            "Tape du texte au clavier dans la fenetre qui a le focus. Pour remplir un formulaire, par exemple."),
        ["mcp_cdp_naviguer"] = (
            "Ouvrir une page web",
            "Ouvre une URL dans un navigateur (Chrome, Edge). Pour interagir avec un site web."),
        ["mcp_cdp_eval_js"] = (
            "Lancer du JavaScript",
            "Execute du code JavaScript dans la page web courante et recupere le resultat."),
        ["mcp_cdp_screenshot"] = (
            "Capturer une page web",
            "Prend une capture d'ecran de la page web courante."),

        // ----- Codage : workflows -----
        ["workflow_code_complet"] = (
            "Code complet (auto)",
            "Enchaine : generer du code + le relire + le sauver. Un graphe tout pret pour avoir du code propre."),
        ["workflow_tests_unitaires"] = (
            "Tests (auto)",
            "Enchaine : generer les tests + les sauver + les executer. Verifie qu'un code marche."),
        ["workflow_refactor_securise"] = (
            "Refactor (auto)",
            "Enchaine : refactorer + relire + sauver. Ameliore un code en gardant son fonctionnement."),

        // ----- Multimedia : generation (4) -----
        ["texte_vers_image"] = (
            "Creer une image",
            "Decris ce que tu veux voir, le modele te le dessine. Choisis un style si tu veux."),
        ["texte_vers_video"] = (
            "Creer une video",
            "Decris une scene, le modele te cree une video de quelques secondes."),
        ["image_vers_video"] = (
            "Animer une image",
            "Prends une image et genere une video ou elle prend vie. Precise le mouvement desire."),
        ["charger_modele"] = (
            "Charger un modele",
            "Force le pre-chargement d'un modele (utile en debut de graphe pour eviter l'attente au premier noeud qui l'utilise)."),

        // ----- Multimedia : TTS / ASR / vision (3) -----
        ["texte_vers_son"] = (
            "Creer une voix-off",
            "Ecris un texte, le modele le dit a voix haute. Choisis la voix si tu veux."),
        ["audio_vers_texte"] = (
            "Transcrire l'audio",
            "Transforme un fichier audio en texte. Sert a retranscrire une reunion, par exemple."),
        ["image_vers_texte"] = (
            "Decrire une image",
            "Le modele regarde une image et te dit ce qu'il y voit en francais."),

        // ----- Multimedia : transformation (4) -----
        ["extraire_frames"] = (
            "Extraire des images",
            "Prends une video et extrait ses images cles (autant que tu veux par seconde)."),
        ["fusionner_videos"] = (
            "Fusionner des videos",
            "Colle plusieurs videos bout a bout pour en faire une seule."),
        ["decouper_video"] = (
            "Decouper une video",
            "Garde seulement un morceau d'une video, entre un debut et une fin."),
        ["redimensionner_image"] = (
            "Redimensionner une image",
            "Change la taille d'une image. Utile pour adapter a un site web ou un emailing."),

        // ----- Phase 1 : 28 noeuds algorithmiques -----
        // Math
        ["math_operation"] = (
            "Calculer",
            "Fait une operation entre 2 nombres. +, -, *, /, %, exposant. La division par zero renvoie NaN."),
        ["math_fonction"] = (
            "Fonction math",
            "Applique une fonction a un nombre : sinus, cosinus, racine, log, arrondi, etc."),
        ["math_statistiques"] = (
            "Statistiques",
            "Sur une liste de nombres : somme, moyenne, minimum, maximum, mediane. La liste est passee en JSON."),
        ["math_aleatoire"] = (
            "Nombre aleatoire",
            "Tire un nombre entier au hasard entre un minimum (inclus) et un maximum (exclus)."),
        ["math_arrondir"] = (
            "Arrondir",
            "Arrondit un nombre au plus proche, en dessous, au-dessus, ou en tronquant. Tu choisis le nombre de decimales."),

        // Texte avance
        ["texte_regex"] = (
            "Chercher par motif",
            "Cherche (ou remplace) un motif dans un texte. La syntaxe est celle des expressions regulieres (regex)."),
        ["texte_split"] = (
            "Decouper un texte",
            "Coupe un texte en morceaux autour d'un separateur. La sortie est une liste JSON, branche-la sur un noeud qui consomme des listes."),
        ["texte_join"] = (
            "Coller une liste",
            "Inverse de Decouper : recolle une liste JSON en un seul texte, avec un separateur entre chaque element."),
        ["texte_formater"] = (
            "Formater un texte",
            "Remplace les balises {nom}, {age}, etc. dans un modele par les valeurs d'un dictionnaire JSON."),
        ["texte_casse"] = (
            "Changer la casse",
            "Met un texte en MAJUSCULES, minuscules, Title Case (Majuscule A Chaque Mot), Sentence case (Majuscule Apres Le Point)."),

        // Dates
        ["date_maintenant"] = (
            "Date actuelle",
            "Donne la date et l'heure du moment, dans le format que tu choisis (ISO 8601, francais, timestamp Unix, lisible)."),
        ["date_parser"] = (
            "Lire une date",
            "Convertit une date ecrite en texte (plusieurs formats possibles) en ISO 8601 exploitable."),

        // Listes
        ["liste_filtrer"] = (
            "Filtrer une liste",
            "Garde ou retire les elements d'une liste qui repondent a un critere (egal, different, superieur a, inferieur a, contient)."),
        ["liste_trier"] = (
            "Trier une liste",
            "Trie une liste de dicts par une cle, en ordre ascendant ou descendant. Detecte tout seul si la cle est numerique."),
        ["liste_unique"] = (
            "Dedoublonner",
            "Enleve les doublons d'une liste, en gardant l'ordre de la premiere occurrence."),
        ["liste_grouper"] = (
            "Grouper par cle",
            "Regroupe les elements d'une liste de dicts par la valeur d'une cle. Sortie : un dict de listes."),

        // Logique
        ["logique_si"] = (
            "Si ... alors",
            "Routeur conditionnel. Selon un booleen en entree, fait passer la donnee vers la sortie 'alors' ou 'sinon'."),
        ["logique_comparer"] = (
            "Comparer",
            "Compare 2 chaines : egal, different, plus petit, plus grand, contient, commence par, finit par. Sortie booleenne."),
        ["logique_et_ou"] = (
            "ET / OU",
            "Combine plusieurs booleens avec un ET logique (tous vrais) ou un OU logique (au moins un vrai)."),

        // Donnees
        ["json_lire"] = (
            "Lire du JSON",
            "Navigue dans un texte JSON via un chemin (data.user.name) et recupere la valeur pointee. Sans chemin, retourne le JSON complet."),
        ["json_ecrire"] = (
            "Creer du JSON",
            "Wrap une valeur en JSON : string, nombre, booleen, tableau, objet. Donne le type en parametre."),
        ["csv_parser"] = (
            "Parser du CSV",
            "Transforme un texte CSV (Comma Separated Values) en liste de dicts. La premiere ligne peut servir d'en-tetes."),

        // Reseau
        ["http_get"] = (
            "Appeler une URL (GET)",
            "Envoie une requete HTTP GET a une URL et recupere la reponse, le code de statut, et un booleen 'ok'."),
        ["http_post"] = (
            "Envoyer (POST)",
            "Envoie une requete HTTP POST avec un body et un content-type, recupere la reponse et le code de statut."),
        ["webhook"] = (
            "Reserver un webhook",
            "Genere une URL de webhook unique. La reception reelle (POST entrant) sera cablee ulterieurement ; pour l'instant le noeud sert a reserver l'identifiant et stocker le payload pour inspection."),

        // Variables
        ["variable_set"] = (
            "Stocker une variable",
            "Enregistre une valeur dans le store de variables (cle + valeur). Accessible plus tard dans le meme processus."),
        ["variable_get"] = (
            "Lire une variable",
            "Recupere la valeur precedemment stockee. Renvoie une valeur par defaut si la cle n'existe pas."),
        ["variable_compteur"] = (
            "Compteur",
            "Incremente (ou decremente avec un pas negatif) un compteur nomme. Premier appel = la valeur de depart."),
    

        // ----- Phase 2 -----
        ["memoire_set"] = ("Stocker dans la memoire partagee", "Enregistre une valeur dans la memoire partagee. Survit aux redemarrages."),
        ["memoire_get"] = ("Lire la memoire partagee", "Recupere une valeur de la memoire partagee, ou la valeur par defaut."),
        ["memoire_lister"] = ("Lister les cles memoire", "Liste toutes les cles de la memoire partagee (sortie JSON liste)."),
        ["boucle_for"] = ("Repeter N fois", "Repete N fois. iteration = [0..N-1], corps_sortie = entree repetee N fois."),
        ["boucle_while"] = ("Repeter tant que", "Repete tant que la condition est vraie, plafonne par max_iterations."),
        ["boucle_foreach"] = ("Pour chaque element", "Pour chaque element d'une liste JSON. element = [...], index = [0..N-1]."),
        ["controle_si"] = ("Executer si...", "Si la condition matche, propage l'entree. Sinon skip les noeuds en aval."),
        ["planificateur_ajouter"] = ("Planifier ce graphe", "Ajoute ce graphe au planificateur avec une expression cron (5 champs)."),
        ["sous_graphe_appel"] = ("Appeler un graphe", "Execute un autre graphe comme sous-programme. Sortie : JSON des sorties terminales."),

        // ----- Phase 3 -----
        ["version_creer"] = ("Sauvegarder une version", "Cree un snapshot git-like du graphe dans Outils/Atelier/Versionning/."),
        ["version_lister"] = ("Versions precedentes", "Liste les versions du graphe (sortie JSON array)."),
        ["version_restaurer"] = ("Restaurer une version", "Restaure le graphe depuis un snapshot precedent. Ecrase le fichier actuel."),
        ["layout_ranger"] = ("Ranger le graphe", "Range les noeuds en grille (BFS, 250px entre rangees, 200px entre noeuds)."),};

    // ============== PARAMETRES ==============

    private static readonly Dictionary<string, (string, string)> _params = new()
    {
        ["code"] = (
            "Ton code",
            "Le code source que tu veux executer ou faire analyser par le modele."),
        ["langage"] = (
            "Langage",
            "Le langage de programmation : Python, Rust, etc."),
        ["prompt"] = (
            "Description",
            "Decris ce que tu veux. Sois precis : le modele fait avec ce que tu lui donnes."),
        ["description"] = (
            "Description",
            "Decris la tache a faire. Plus c'est detaille, meilleur c'est."),
        ["url"] = (
            "Adresse web",
            "L'URL complete, ex: https://example.com"),
        ["temperature"] = (
            "Originalite",
            "0 = le modele repond toujours la meme chose. 1 = le modele improvise. 0.2 par defaut."),
        ["limite_s"] = (
            "Duree max (s)",
            "L'execution est tuee apres ce nombre de secondes."),
        ["limite"] = (
            "Limite",
            "Une borne haute : nombre max d'elements, duree max, etc. Voir le tooltip du noeud pour l'unite."),
        ["image_initiale"] = (
            "Image de depart",
            "L'image que le modele va animer ou transformer."),
        ["fichier"] = (
            "Fichier",
            "Le chemin vers un fichier sur ton disque."),
        ["chemin"] = (
            "Fichier",
            "Le chemin vers un fichier sur ton disque."),
        ["dossier"] = (
            "Dossier",
            "Le chemin vers un dossier sur ton disque."),
        ["entrees"] = (
            "Fichiers joints",
            "Les fichiers mis a disposition du code (lisibles depuis le code)."),
        ["sequence"] = (
            "Description de la sequence",
            "Ce que tu vas faire, pour le bandeau d'avertissement affiche aux autres applications."),
        ["modele"] = (
            "Modele",
            "Le modele d'IA a utiliser (image, video, voix...). Vide = modele par defaut installe."),
        ["modele_id"] = (
            "Modele",
            "Le modele d'IA a utiliser. Vide = modele par defaut installe."),
        ["format"] = (
            "Format",
            "Le format de sortie : png, jpg, mp4, etc."),
        ["fps"] = (
            "Images par seconde",
            "Combien d'images extraire par seconde de video."),
        ["largeur"] = (
            "Largeur",
            "La taille en pixels."),
        ["hauteur"] = (
            "Hauteur",
            "La taille en pixels."),
        ["debut"] = (
            "Debut",
            "Ou couper (en secondes depuis le debut)."),
        ["fin"] = (
            "Fin",
            "Ou couper (en secondes depuis le debut)."),
        ["motif"] = (
            "Motif",
            "Le motif de filtrage des fichiers (ex: *.txt, *.cs). Vide = tous les fichiers."),
        ["separateur"] = (
            "Separateur",
            "Le caractere ou la chaine placee entre chaque texte colle. Par defaut : saut de ligne."),
        ["contenu"] = (
            "Texte",
            "Le contenu textuel a ecrire ou a utiliser comme entree."),
        ["valeur"] = (
            "Valeur",
            "La valeur numerique ou booleenne du noeud."),
        ["titre"] = (
            "Titre",
            "Le titre de la fenetre a cibler. Vide = ecran entier."),
        ["bouton"] = (
            "Bouton",
            "Le bouton de la souris : gauche, droit ou milieu."),
        ["x"] = (
            "Position X",
            "Position horizontale en pixels."),
        ["y"] = (
            "Position Y",
            "Position verticale en pixels."),
        ["question"] = (
            "Question",
            "La question a poser au modele."),
        ["contexte"] = (
            "Contexte",
            "Le contexte a donner au modele pour qu'il reponde a la question."),
        ["reponse"] = (
            "Reponse",
            "La reponse generee par le modele (sortie)."),
        ["code_partiel"] = (
            "Code a completer",
            "Un bout de code la ou le modele doit continuer."),
        ["code_source"] = (
            "Code source",
            "Le code a analyser, traduire ou documenter."),
        ["code_revise"] = (
            "Code revise",
            "La sortie : le code ameliore par le modele."),
        ["code_traduit"] = (
            "Code traduit",
            "La sortie : le code dans le langage cible."),
        ["code_documente"] = (
            "Code documente",
            "La sortie : le code avec docstrings et commentaires."),
        ["code_refactore"] = (
            "Code refactore",
            "La sortie : le code reorganise, meme comportement."),
        ["tests"] = (
            "Tests",
            "La sortie : le code de tests genere par le modele."),
        ["nom"] = (
            "Nom",
            "Le nom a proposer (variable, fonction, etc.)."),
        ["image"] = (
            "Image",
            "Une image, en entree ou en sortie."),
        ["video"] = (
            "Video",
            "Une video, en entree ou en sortie."),
        ["audio"] = (
            "Audio",
            "Un fichier audio, en entree ou en sortie."),
        ["voix"] = (
            "Voix",
            "La voix a utiliser pour la synthese vocale."),
        ["liste"] = (
            "Liste",
            "Une liste de fichiers, de resultats, ou d'elements."),
        ["stdout"] = (
            "Sortie standard",
            "Ce que le programme a ecrit sur stdout."),
        ["stderr"] = (
            "Sortie d'erreur",
            "Ce que le programme a ecrit sur stderr (les erreurs, en general)."),
        ["code_retour"] = (
            "Code de retour",
            "Le code d'exit du programme : 0 = succes, autre = erreur."),
        ["duree_ms"] = (
            "Duree (ms)",
            "Combien de millisecondes l'execution a pris."),
        ["textes"] = (
            "Textes",
            "Les textes a coller, separes par des sauts de ligne ou un autre separateur."),
        ["dossier_travail"] = (
            "Dossier de travail",
            "Le dossier dans lequel le code s'execute. Vide = un dossier temporaire par lancement."),
        ["modele_id"] = (
            "Modele",
            "L'identifiant du modele a utiliser."),
    };

    /// <summary>
    /// Texte court affichant la categorie de duree estimee (utilise pour
    /// le tooltip de l'indicateur visuel). Le <c>~X</c> est un ordre de
    /// grandeur, pas une promesse -- voir les commentaires de
    /// <see cref="DureeNoeuds"/> pour le pourquoi.
    /// </summary>
    public static string DureeTexte(CategorieDuree d) => d switch
    {
        CategorieDuree.Rapide => "Rapide (~1s)",
        CategorieDuree.Moyen  => "Moyen (~10s)",
        CategorieDuree.Long   => "Long (~1min)",
        _ => "Inconnu"
    };
}
