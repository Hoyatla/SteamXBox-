using System.Collections.Generic;

namespace SenSÉ.Atelier.Bibliotheque;

/// <summary>
/// Estimation de la duree typique d'un noeud, par id technique. Sert a
/// l'indicateur visuel (point colore en haut a droite du noeud) et au
/// DTO HTTP <c>duree_estimee</c>.
/// </summary>
/// <remarks>
/// <b>Les fourchettes sont des ordres de grandeur, pas des promesses.</b>
/// Un noeud <c>Rapide</c> peut prendre 2s sur un reseau lent, un <c>Long</c>
/// peut finir en 20s si le modele est chaud. Ce mapping sert d'aide a
/// l'utilisateur ("ca va etre long, je lance et je vais boire un cafe"),
/// pas d'instrumentation precise.
///
/// <para><b>Ajouter un nouveau noeud :</b> ajouter son entree ici. Le
/// fallback est <c>Rapide</c>, donc un noeud oublie sera vert (le user
/// croit que ca va etre rapide) -- c'est volontaire, c'est le cote
/// sur-averti du defaut.</para>
/// </remarks>
public static class DureeNoeuds
{
    /// <summary>
    /// Retourne la categorie de duree pour un id de noeud, ou null si
    /// pas reference. <see cref="DefinitionNoeud.DureeEstimeeEffective"/>
    /// fait le fallback final sur Rapide.
    /// </summary>
    public static CategorieDuree? Lookup(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _durees.TryGetValue(id, out var v) ? v : (CategorieDuree?)null;
    }

    private static readonly Dictionary<string, CategorieDuree> _durees = new()
    {
        // ----- Codage : primitives (< 1s) -----
        ["texte"] = CategorieDuree.Rapide,
        ["nombre"] = CategorieDuree.Rapide,
        ["booleen"] = CategorieDuree.Rapide,
        ["fichier_lire"] = CategorieDuree.Rapide,
        ["fichier_ecrire"] = CategorieDuree.Rapide,
        ["lister_fichiers"] = CategorieDuree.Rapide,
        ["concatener"] = CategorieDuree.Rapide,

        // ----- Codage : execution code -----
        // executer_code : depend du langage. Les langages compiles (Python, Node,
        // Java, Rust compile) demarrent en < 1s, mais un script qui prend du temps
        // peut faire grimper la moyenne. On garde Rapide, le defaut, et l'utilisateur
        // voit "Long" via le badge si le moteur detecte que l'exec depasse.
        ["executer_code"] = CategorieDuree.Rapide,
        // executer_commande : depend de la commande, mais en general rapide
        // (lance un exe qui finit vite). Mis en Moyen par securite -- une
        // commande peut aussi prendre 1-2 min (build, install).
        ["executer_commande"] = CategorieDuree.Moyen,

        // ----- Codage : LLM (1-30s, depend du modele local et de la taille) -----
        ["llm_generer_code"] = CategorieDuree.Moyen,
        ["llm_completer_code"] = CategorieDuree.Moyen,
        ["llm_expliquer_code"] = CategorieDuree.Moyen,
        ["llm_reviser_code"] = CategorieDuree.Moyen,
        ["llm_generer_tests"] = CategorieDuree.Moyen,
        ["llm_traduire_code"] = CategorieDuree.Moyen,
        ["llm_documenter"] = CategorieDuree.Moyen,
        ["llm_refactorer"] = CategorieDuree.Moyen,
        ["llm_generer_nom"] = CategorieDuree.Moyen,
        ["llm_repondre_question"] = CategorieDuree.Moyen,

        // ----- Codage : MCP saisie / CDP -----
        ["mcp_saisie_screenshot"] = CategorieDuree.Moyen,  // 1-3s (encodage PNG)
        ["mcp_saisie_cliquer"] = CategorieDuree.Rapide,
        ["mcp_saisie_taper"] = CategorieDuree.Rapide,
        ["mcp_cdp_naviguer"] = CategorieDuree.Moyen,  // 1-5s (chargement page)
        ["mcp_cdp_eval_js"] = CategorieDuree.Moyen,  // 1-5s
        ["mcp_cdp_screenshot"] = CategorieDuree.Moyen,

        // ----- Codage : workflows -----
        // Moyen par defaut, mais peut etre Long selon le contenu.
        ["workflow_code_complet"] = CategorieDuree.Moyen,
        ["workflow_tests_unitaires"] = CategorieDuree.Moyen,
        ["workflow_refactor_securise"] = CategorieDuree.Moyen,

        // ----- Multimedia : generation (> 30s en general) -----
        ["texte_vers_image"] = CategorieDuree.Long,  // Flux ~16s, SDXL 30s+
        ["texte_vers_video"] = CategorieDuree.Long,  // Wan 2.2 ~60s
        ["image_vers_video"] = CategorieDuree.Long,  // Wan I2V ~60s
        // charger_modele est un "warm-up" rapide (charge le binaire en memoire),
        // pas une generation.
        ["charger_modele"] = CategorieDuree.Rapide,

        // ----- Multimedia : TTS / ASR / vision -----
        // texte_vers_son (SAPI local) : rapide pour quelques phrases, Long pour un
        // long texte (>30s a 1min). Reste Long par defaut prudent.
        ["texte_vers_son"] = CategorieDuree.Long,
        // audio_vers_texte : Whisper prend ~5-15s pour 1 min d'audio. Moyen.
        ["audio_vers_texte"] = CategorieDuree.Moyen,
        // image_vers_texte (Qwen VL) : 1-3s par image. Moyen.
        ["image_vers_texte"] = CategorieDuree.Moyen,

        // ----- Multimedia : transformation -----
        ["extraire_frames"] = CategorieDuree.Rapide,  // rapide, depend duree video
        ["fusionner_videos"] = CategorieDuree.Moyen,  // 5-20s selon taille
        ["decouper_video"] = CategorieDuree.Rapide,  // rapide, juste un trim
        ["redimensionner_image"] = CategorieDuree.Rapide,

        // ----- Workflow multimedia -----
        // texte vers animation = texte -> image -> video, ~80s total.
        ["workflow_texte_vers_animation"] = CategorieDuree.Long,
        // ----- Phase 1 : 28 noeuds algorithmiques -----
        // Math
        ["math_operation"] = CategorieDuree.Rapide,
        ["math_fonction"] = CategorieDuree.Rapide,
        // Statistiques : peut grimper sur des listes de plusieurs milliers d'elements.
        ["math_statistiques"] = CategorieDuree.Moyen,
        ["math_aleatoire"] = CategorieDuree.Rapide,
        ["math_arrondir"] = CategorieDuree.Rapide,

        // Texte avance
        ["texte_regex"] = CategorieDuree.Rapide,
        ["texte_split"] = CategorieDuree.Rapide,
        ["texte_join"] = CategorieDuree.Rapide,
        ["texte_formater"] = CategorieDuree.Rapide,
        ["texte_casse"] = CategorieDuree.Rapide,

        // Dates
        ["date_maintenant"] = CategorieDuree.Rapide,
        ["date_parser"] = CategorieDuree.Rapide,

        // Listes
        // Filtrer, trier, grouper dependent de la taille de la liste. Moyen par defaut prudent.
        ["liste_filtrer"] = CategorieDuree.Moyen,
        ["liste_trier"] = CategorieDuree.Moyen,
        ["liste_grouper"] = CategorieDuree.Moyen,
        ["liste_unique"] = CategorieDuree.Rapide,

        // Logique
        ["logique_si"] = CategorieDuree.Rapide,
        ["logique_comparer"] = CategorieDuree.Rapide,
        ["logique_et_ou"] = CategorieDuree.Rapide,

        // Donnees
        // csv_parser sur de gros fichiers peut prendre 1-2s.
        ["json_lire"] = CategorieDuree.Rapide,
        ["json_ecrire"] = CategorieDuree.Rapide,
        ["csv_parser"] = CategorieDuree.Moyen,

        // Reseau
        // Endroits lents : 0.1-5s typiquement, peut-etre 30s si timeout.
        ["http_get"] = CategorieDuree.Moyen,
        ["http_post"] = CategorieDuree.Moyen,
        // webhook MVP : generation d'ID instantanee.
        ["webhook"] = CategorieDuree.Rapide,

        // Variables
        ["variable_set"] = CategorieDuree.Rapide,
        ["variable_get"] = CategorieDuree.Rapide,
        ["variable_compteur"] = CategorieDuree.Rapide,
    };
}
