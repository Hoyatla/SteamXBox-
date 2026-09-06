namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les 5 Capacites de pilotage direct par UI Automation (UIA). Elles
/// proxyent vers le pc-agent (Rust) qui parle UIA en natif via le serveur
/// MCP mcp-debugapi.
/// </summary>
/// <remarks>
/// <b>Le pont : pc-agent et mcp-debugapi.</b> pc-agent expose /v1/uia/*
/// en HTTP sur la machine locale. mcp-debugapi est un serveur MCP C# qui
/// traduit les appels MCP en requetes HTTP vers pc-agent. Cette classe
/// appelle directement l'API HTTP de pc-agent (pas MCP) parce qu'elle
/// tourne deja in-process dans l'Assistant.
///
/// <para><b>Synchronous, pour l'instant.</b> Les Capacite sont encore
/// <c>Func&lt;..., string&gt;</c>, pas <c>Func&lt;..., Task&lt;string&gt;&gt;</c>.
/// On utilise <c>GetAwaiter().GetResult()</c> en attendant la migration.</para>
/// </remarks>
public static class AssistantDebug
{
    private static readonly HttpClient Client = new();

    private static readonly string AgentUrl =
        Environment.GetEnvironmentVariable("SENSE_DEBUG_AGENT_URL")
            ?? "http://127.0.0.1:8765";

    private static readonly string? AgentToken =
        Environment.GetEnvironmentVariable("SENSE_DEBUG_AGENT_TOKEN");

    /// <summary>Le PID du pc-agent, pour lui ceder le droit de premier plan.</summary>
    private static readonly int AgentPid =
        int.TryParse(Environment.GetEnvironmentVariable("SENSE_DEBUG_AGENT_PID"), out var pid) ? pid : 0;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>
    /// Cede au pc-agent le droit de mettre une fenetre au premier plan.
    /// </summary>
    /// <remarks>
    /// <b>Pourquoi ce detour est la seule voie propre.</b> Windows refuse
    /// <c>SetForegroundWindow</c> a un processus qui ne possede pas deja le premier plan :
    /// c'est ce qui empeche n'importe quel programme d'arriere-plan de sauter devant celui
    /// qu'on utilise. pc-agent, lance en subprocess, ne le possede jamais — et le 6
    /// septembre 2026 son refus a envoye « Bonjour dans le nouveau systeme
    /// d'exploitation! » dans la fenetre de l'Assistant au lieu de LibreOffice.
    ///
    /// <para>La regle a une porte, prevue pour exactement ce cas : le processus qui
    /// possede le premier plan peut ceder son droit a un autre, par PID. Or quand
    /// l'utilisateur vient de parler a l'Assistant, la fenetre au premier plan est
    /// justement celle de SenSÉ.Desktop — et cette classe tourne dans ce processus-la.
    /// Elle est donc au seul endroit d'ou la cession est possible.</para>
    ///
    /// <para>Le droit cede ne vaut que pour le prochain appel du beneficiaire, et il est
    /// consomme meme s'il ne s'en sert pas : on le redonne avant chaque requete de focus,
    /// jamais une fois pour toutes. Si SenSÉ n'a pas le premier plan, l'appel echoue sans
    /// consequence et pc-agent rendra son refus habituel — ce qui reste la bonne reponse.</para>
    /// </remarks>
    private static void CederLePremierPlan()
    {
        if (AgentPid <= 0) return;
        try { AllowSetForegroundWindow(AgentPid); }
        catch { /* user32 absent ou appel refuse : pc-agent le dira lui-meme. */ }
    }

    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return
        [
            new AssistantLocal.Capacite(
                "debug_uia_dump",
                "Renvoie l'arbre UI Automation de la fenetre au premier plan. Chaque controle a un nom, un type, un automationId, un etat enabled, et un rectangle en pixels. Utilise cette capacite pour savoir quels controles existent avant de les actionner.",
                [],
                args => AppelerAsync("GET", "/v1/uia/dump", null)),

            new AssistantLocal.Capacite(
                "debug_uia_invoke",
                "Invoque un controle (clic logique, pas physique) identifie par son automationId. C'est 100x plus fiable qu'un clic souris parce qu'il n'y a pas de coordonnees. Trouve d'abord l'automationId avec debug_uia_dump, puis appelle cette capacite.",
                [new AssistantLocal.Parametre("automationId", "L'automationId du controle, tel qu'il apparait dans le dump UIA", [])],
                args => AppelerAsync("POST", "/v1/uia/invoke", new { automation_id = args.GetValueOrDefault("automationId") ?? "" })),

            new AssistantLocal.Capacite(
                "debug_uia_invoke_par_nom",
                "A PREFERER a debug_uia_invoke. Actionne un controle par le NOM ecrit dessus (le champ 'name' du dump), pas par son automationId. Dans beaucoup de boites de dialogue l'automationId est un simple numero d'ordre — LibreOffice expose 'Enregistrer' en '1' et 'Annuler' en '2' — et se tromper d'un rang annule au lieu de valider. La correspondance essaie exact, puis casse ignoree, puis sous-chaine, en preferant un controle actif. La reponse renvoie {invoked} : le nom reellement actionne, a relire. Si le nom n'existe pas, l'erreur liste les noms presents dans la fenetre : choisis dedans plutot que de deviner.",
                [
                    new AssistantLocal.Parametre("nom", "Le nom affiche sur le controle, tel qu'il apparait dans le champ 'name' du dump (ex: Enregistrer, Annuler, OK).", []),
                    new AssistantLocal.Parametre("titre", "Titre (ou partie) de la fenetre ou chercher. Vide = fenetre au premier plan, ce qui est le cas d'une boite qui vient de s'ouvrir.", []),
                ],
                args => AppelerAsync("POST", "/v1/uia/invoke-by-name", new
                {
                    name = args.GetValueOrDefault("nom") ?? "",
                    title = args.GetValueOrDefault("titre") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_set_text",
                "Ecrit du texte dans un champ de saisie identifie par son automationId. Pas besoin de cliquer puis taper : c'est un set direct.",
                [
                    new AssistantLocal.Parametre("automationId", "L'automationId du champ Edit", []),
                    new AssistantLocal.Parametre("value", "Le texte a ecrire", []),
                ],
                args => AppelerAsync("POST", "/v1/uia/set_text", new
                {
                    automation_id = args.GetValueOrDefault("automationId") ?? "",
                    value = args.GetValueOrDefault("value") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_select",
                "Selectionne un item dans une liste deroulante ou une zone de liste, identifiee par son automationId.",
                [
                    new AssistantLocal.Parametre("automationId", "L'automationId de la ComboBox ou ListBox", []),
                    new AssistantLocal.Parametre("value", "Le texte ou l'index de l'item a selectionner", []),
                ],
                args => AppelerAsync("POST", "/v1/uia/select", new
                {
                    automation_id = args.GetValueOrDefault("automationId") ?? "",
                    value = args.GetValueOrDefault("value") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_press",
                "Envoie une combinaison de touches. Exemples: 'Ctrl+S', 'Alt+F4', 'Return', 'Escape', 'Tab'. Pas de focus shift visible, c'est le meme mecanisme que SendKeys.",
                [new AssistantLocal.Parametre("keys", "La combinaison, ex: 'Ctrl+S' ou 'Return'", [])],
                args => AppelerAsync("POST", "/v1/uia/press", new
                {
                    keys = args.GetValueOrDefault("keys") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_screenshot_window",
                "Prend un screenshot UNIQUEMENT de la fenetre identifiee par son titre (pas tout l'ecran). Le resultat est un fichier PNG sauvegarde dans C:\\Program Files\\SenSÉ\\Captures\\. Renvoie le chemin. Utilise cette capacite quand l'utilisateur veut 'une capture de cette fenetre', 'screenshot de la fenetre X', 'montre-moi la fenetre active', etc.",
                [new AssistantLocal.Parametre("titre", "Le titre exact (ou une partie) de la fenetre a capturer", [])],
                args => AppelerAsync("POST", "/v1/uia/screenshot-window", new
                {
                    title = args.GetValueOrDefault("titre") ?? "",
                })),

            new AssistantLocal.Capacite(
                "afficher_image",
                "Affiche une image dans la conversation. Le modele multimodal la voit. Le user la voit. Utilise apres un screenshot pour montrer ce que tu as capture.",
                [new AssistantLocal.Parametre("chemin", "Le chemin absolu du fichier PNG", [])],
                args =>
                {
                    var chemin = args.GetValueOrDefault("chemin") ?? "";
                    if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin))
                    {
                        return "fichier introuvable: " + chemin;
                    }
                    var info = new FileInfo(chemin);
                    // Le marqueur [IMAGE:chemin] est detecte par Repondre (Executer) qui
                    // charge alors le PNG, l'encode en base64, et transforme le "content"
                    // du message tool en tableau multimodal (text + image_url data:base64).
                    return $"[IMAGE:{chemin}] image affichee ({info.Length / 1024} Ko)";
                },
                Interne: true),

            new AssistantLocal.Capacite(
                "debug_uia_list_windows",
                "Liste toutes les fenetres top-level visibles (titre + HWND). Utilise cette capacite en premier pour trouver la fenetre cible avant de la dumper ou de la capturer.",
                [],
                args => AppelerAsync("GET", "/v1/uia/list-windows", null)),

            new AssistantLocal.Capacite(
                "debug_uia_dump_window",
                "Dump l'arbre UIA d'une fenetre specifique identifiee par son titre (substring, case-insensitive). Utilise cette capacite apres debug_uia_list_windows quand la fenetre cible n'est PAS au premier plan.",
                [new AssistantLocal.Parametre("titre", "Le titre (ou partie du titre) de la fenetre a dumper", [])],
                args => AppelerAsync("POST", "/v1/uia/dump-window", new
                {
                    title = args.GetValueOrDefault("titre") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_focus_window",
                "Met au premier plan la fenetre identifiee par son HWND, et VERIFIE que le focus a bien pris. Repond en erreur si Windows a refuse (un processus d'arriere-plan n'a pas le droit de voler le premier plan) : dans ce cas ne tape rien, la frappe irait dans une autre fenetre. Le HWND vient de debug_uia_list_windows (decimal ou 0xABCD).",
                [new AssistantLocal.Parametre("hwnd", "Le HWND de la fenetre (decimal ou 0xABCD). Vient de debug_uia_list_windows.", [])],
                args => AppelerAsync("POST", "/v1/uia/focus-window", new
                {
                    hwnd = args.GetValueOrDefault("hwnd") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_focus_and_type",
                "LE MOYEN PRIVILEGIE d'ecrire dans une application deja ouverte. Met la fenetre au premier plan, verifie que le focus a pris, puis tape le texte — le tout dans un seul appel et sur un seul thread, ce qui est la seule facon fiable sous Windows. Repond {hwnd, title, units_sent, still_foreground} : verifie que title est bien la fenetre visee. Si le focus est refuse, rien n'est tape et tu recois une erreur. A preferer a la sequence debug_uia_focus_window + saisie_clavier_taper, qui ne garantit pas que le focus tienne entre les deux appels.",
                [
                    new AssistantLocal.Parametre("hwnd", "Le HWND de la fenetre (decimal ou 0xABCD). Vient de debug_uia_list_windows. VERIFIE-LE : un HWND lu de travers ecrit dans la mauvaise application.", []),
                    new AssistantLocal.Parametre("texte", "Le texte a taper. Envoye en Unicode, la disposition du clavier n'a pas d'importance.", []),
                ],
                args => AppelerAsync("POST", "/v1/uia/focus-and-type", new
                {
                    hwnd = args.GetValueOrDefault("hwnd") ?? "",
                    text = args.GetValueOrDefault("texte") ?? "",
                })),

            new AssistantLocal.Capacite(
                "debug_uia_find_main_edit",
                "Heuristique qui retourne le champ d'edition principal d'une fenetre (Document > Pane le plus grand > Edit le plus grand, scoring par surface). Renvoie {automationId, name, type, rect}. Si elle ne retourne rien, fais debug_uia_dump_window et cherche manuellement un Document ou un Pane avec le plus grand rectangle.",
                [new AssistantLocal.Parametre("titre", "Titre (ou partie) de la fenetre. Vide = fenetre foreground.", [])],
                args => AppelerAsync("POST", "/v1/uia/find-main-edit", new
                {
                    title = args.GetValueOrDefault("titre") ?? "",
                })),
        ];
    }

    private static string AppelerAsync(string methode, string path, object? body)
    {
        try
        {
            // Tout ce qui va demander le premier plan doit d'abord en recevoir le droit,
            // et le recevoir juste avant : la cession ne vaut que pour un appel.
            if (path.StartsWith("/v1/uia/focus", StringComparison.Ordinal))
            {
                CederLePremierPlan();
            }

            using var req = new HttpRequestMessage(
                methode == "GET" ? HttpMethod.Get : HttpMethod.Post,
                AgentUrl.TrimEnd('/') + path);
            if (body is not null)
            {
                req.Content = System.Net.Http.Json.JsonContent.Create(body);
            }
            if (!string.IsNullOrEmpty(AgentToken))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AgentToken);
            }
            using var resp = Client.SendAsync(req).GetAwaiter().GetResult();
            var respBody = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return $"HTTP {(int)resp.StatusCode} {resp.StatusCode}: {respBody}";
        }
        catch (Exception ex)
        {
            return $"erreur UIA: {ex.GetType().Name}: {ex.Message}";
        }
    }
}