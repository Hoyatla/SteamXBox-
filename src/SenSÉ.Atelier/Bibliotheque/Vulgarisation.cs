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
    };

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
