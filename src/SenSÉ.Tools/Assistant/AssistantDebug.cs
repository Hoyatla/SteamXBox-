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
        ];
    }

    private static string AppelerAsync(string methode, string path, object? body)
    {
        try
        {
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