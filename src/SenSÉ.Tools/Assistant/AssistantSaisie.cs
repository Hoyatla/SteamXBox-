using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de pilotage en mode souris/clavier, branchees sur mcp-saisie.
/// Pour les applications qui ne sont pas UIA-friendly (LibreOffice Writer, vieux
/// logiciels, apps qui utilisent UNO/AT-SPI au lieu de UI Automation), UIA ne
/// voit que la coquille, pas le contenu. Il faut alors passer en mode souris
/// + clavier.
/// </summary>
/// <remarks>
/// <b>Le pont : mcp-saisie.exe.</b> mcp-saisie est un serveur MCP sur stdio
/// (JSON-RPC 2.0 newline-delimited). Cette classe le lance comme subprocess
/// paresseusement au premier appel, garde le pipe ouvert pour toute la duree
/// de la session, et dispatche les Capacites sur le meme pipe. Le binaire
/// doit etre dans <c>Outils\McpSaisie\SenSÉ.Mcp.Saisie.exe</c> a cote de
/// <c>SenSÉ.Desktop.exe</c>.
///
/// <para><b>Pourquoi pas HTTP.</b> mcp-saisie parle stdio exclusivement.
/// C'est le pattern MCP standard (cf la doc en tete de mcp-saisie/Serveur.cs) :
/// "Stdio, pas HTTP. Pour un client in-process, stdio est plus simple (pas de
/// port, pas de pare-feu, pas de conflit)". MultiplexeurMcp (classe hypothetique
/// qui routerait vers plusieurs serveurs MCP) n'existe pas dans cette codebase,
/// donc on parle directement a mcp-saisie en JSON-RPC 2.0.</para>
///
/// <para><b>Mode Exclusif visuel.</b> Les actions souris/clavier doivent etre
/// encadrees par <c>saisie_mode_exclusif_ouvrir</c> et
/// <c>saisie_mode_exclusif_fermer</c> : un bandeau "L'ASSISTANT PILOTE" informe
/// l'utilisateur que la souris va bouger, et un Echap interrompt la sequence.
/// Voir <see cref="saisie_mode_exclusif_ouvrir"/> dans la consigne pour le
/// detail du workflow.</para>
/// </remarks>
public static class AssistantSaisie
{
    private static Process? _proc;
    private static StreamWriter? _stdin;
    private static int _nextId = 1;
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    private static CancellationTokenSource? _ctsReader;
    private static Task? _readerTask;
    private static readonly object _startLock = new();

    /// <summary>
    /// Construit la liste des Capacite de pilotage souris/clavier que
    /// l'Assistant peut appeler. A ajouter a la liste passee a
    /// <see cref="AssistantLocal.Repondre"/>.
    /// </summary>
    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return
        [
            new AssistantLocal.Capacite(
                "saisie_screenshot_ecran",
                "Screenshot de l'ecran entier (PNG dans Outils\\Captures). Utilise quand UIA ne marche pas (ex: LibreOffice Writer) et que tu as besoin de voir l'ecran entier pour reperer ou cliquer.",
                [],
                args => AppelerSaisie("saisie.screenshot_ecran", null)),

            new AssistantLocal.Capacite(
                "saisie_screenshot_fenetre",
                "Screenshot d'une fenetre specifique identifiee par son titre (PNG). Utilise apres un focus_window pour confirmer ce que tu vois dans cette fenetre.",
                [new AssistantLocal.Parametre("titre", "Le titre (ou partie) de la fenetre", [])],
                args => AppelerSaisie("saisie.screenshot_fenetre", new { titre = args.GetValueOrDefault("titre") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_lister_fenetres",
                "Liste les fenetres visibles (titre + HWND). Utilise quand debug_uia_list_windows ne voit pas la cible : apps non-UIA comme LibreOffice.",
                [],
                args => AppelerSaisie("saisie.lister_fenetres", null)),

            new AssistantLocal.Capacite(
                "saisie_souris_deplacer",
                "Deplace le curseur a une position absolue (coords ecran en pixels). Mode Exclusif visuel obligatoire (saisie_mode_exclusif_ouvrir avant).",
                [
                    new AssistantLocal.Parametre("x", "Coordonnee X en pixels", []),
                    new AssistantLocal.Parametre("y", "Coordonnee Y en pixels", []),
                ],
                args => AppelerSaisie("saisie.souris_deplacer", new
                {
                    x = int.Parse(args.GetValueOrDefault("x") ?? "0"),
                    y = int.Parse(args.GetValueOrDefault("y") ?? "0"),
                })),

            new AssistantLocal.Capacite(
                "saisie_souris_cliquer",
                "Clic souris (gauche par defaut). Mode Exclusif visuel obligatoire. Tu peux passer x/y pour cliquer a des coords precises, sinon le clic part au curseur actuel.",
                [
                    new AssistantLocal.Parametre("bouton", "gauche, droit ou milieu", ["gauche", "droit", "milieu"]),
                    new AssistantLocal.Parametre("doubles", "true pour double-clic (defaut false)", []),
                    new AssistantLocal.Parametre("x", "Coord X optionnel", []),
                    new AssistantLocal.Parametre("y", "Coord Y optionnel", []),
                ],
                args =>
                {
                    var body = new Dictionary<string, object>
                    {
                        ["bouton"] = args.GetValueOrDefault("bouton") ?? "gauche",
                    };
                    if (bool.TryParse(args.GetValueOrDefault("doubles") ?? "false", out var d) && d)
                    {
                        body["doubles"] = true;
                    }
                    if (int.TryParse(args.GetValueOrDefault("x"), out var x))
                    {
                        body["x"] = x;
                    }
                    if (int.TryParse(args.GetValueOrDefault("y"), out var y))
                    {
                        body["y"] = y;
                    }
                    return AppelerSaisie("saisie.souris_cliquer", body);
                }),

            new AssistantLocal.Capacite(
                "saisie_clavier_taper",
                "Tape une chaine de caracteres (un caractere a la fois). Mode Exclusif visuel obligatoire.",
                [new AssistantLocal.Parametre("texte", "Le texte a taper", [])],
                args => AppelerSaisie("saisie.clavier_taper", new { texte = args.GetValueOrDefault("texte") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_clavier_touche",
                "Appuie sur une touche speciale ou combinaison. Alias francais OK. Ex: 'Ctrl+S', 'Return', 'Echap', 'Impr', 'F5', 'Tab'.",
                [new AssistantLocal.Parametre("touche", "Le nom de la touche ou combinaison (Ctrl+S, Return, Echap, F1..F12, fleches)", [])],
                args => AppelerSaisie("saisie.clavier_touche", new { touche = args.GetValueOrDefault("touche") ?? "" })),

            new AssistantLocal.Capacite(
                "saisie_mode_exclusif_ouvrir",
                "Ouvre le bandeau 'L'ASSISTANT PILOTE' (Mode Exclusif visuel). A appeler AVANT toute action souris/clavier. L'utilisateur peut interrompre immediatement avec Echap. Une seule sequence par session d'edition.",
                [new AssistantLocal.Parametre("sequence", "Description courte de ce que tu vas faire, ex: 'clic dans Writer'", [])],
                args => AppelerSaisie("saisie.mode_exclusif_ouvrir", new { sequence = args.GetValueOrDefault("sequence") ?? "action Assistant" })),

            new AssistantLocal.Capacite(
                "saisie_mode_exclusif_fermer",
                "Ferme le bandeau Mode Exclusif. A appeler APRES la derniere action souris/clavier (sans condition, en finally logique).",
                [],
                args => AppelerSaisie("saisie.mode_exclusif_fermer", null)),
        ];
    }

    /// <summary>
    /// Demarre mcp-saisie.exe comme subprocess si pas deja fait. Idempotent.
    /// Le binaire doit etre dans <c>Outils\McpSaisie\SenSÉ.Mcp.Saisie.exe</c>.
    /// </summary>
    public static void Demarrer()
    {
        lock (_startLock)
        {
            if (_proc is not null && !_proc.HasExited)
            {
                return;
            }

            // Si on a un process mort, on le jette et on en cree un neuf.
            _proc?.Dispose();
            _proc = null;
            _stdin = null;

            var exe = Path.Combine(AppContext.BaseDirectory, "Outils", "McpSaisie", "SenSÉ.Mcp.Saisie.exe");
            if (!File.Exists(exe))
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            try
            {
                _proc = Process.Start(psi);
            }
            catch
            {
                _proc = null;
                return;
            }
            if (_proc is null)
            {
                return;
            }

            _stdin = _proc.StandardInput;
            _stdin.AutoFlush = true;

            // Sauter la notification "ready" initiale (fire-and-forget, pas de reponse).
            try
            {
                _proc.StandardOutput.ReadLine();
            }
            catch
            {
                // le pipe est deja mort
            }

            // Reader background : distribue chaque ligne JSON-RPC 2.0 sur la TCS correspondante.
            _ctsReader = new CancellationTokenSource();
            _readerTask = Task.Run(() => LireReponses(_proc, _ctsReader.Token));
        }
    }

    /// <summary>
    /// Arrete mcp-saisie. Appele par <c>App.OnExit</c> pour cleanup.
    /// </summary>
    public static void Arreter()
    {
        lock (_startLock)
        {
            try { _ctsReader?.Cancel(); } catch { /* ignore */ }
            try
            {
                if (_proc is not null && !_proc.HasExited)
                {
                    _proc.Kill(entireProcessTree: true);
                }
            }
            catch { /* ignore */ }
            try { _proc?.Dispose(); } catch { /* ignore */ }
            _proc = null;
            _stdin = null;
            // Complete les TCS en attente pour ne pas bloquer les appelants.
            foreach (var kv in _pending)
            {
                kv.Value.TrySetResult("mcp-saisie arrete");
            }
            _pending.Clear();
        }
    }

    /// <summary>
    /// Envoie une requete JSON-RPC 2.0 a mcp-saisie et attend la reponse
    /// (max 15 s). Auto-demarre le subprocess au premier appel.
    /// </summary>
    private static string AppelerSaisie(string nomOutil, object? body)
    {
        try
        {
            Demarrer();
            if (_proc is null || _stdin is null)
            {
                return "mcp-saisie: binaire introuvable (Outils\\McpSaisie\\SenSÉ.Mcp.Saisie.exe a cote de SenSÉ.Desktop.exe)";
            }
            if (_proc.HasExited)
            {
                return $"mcp-saisie: le process a quitte (code {_proc.ExitCode}), redemarrage au prochain appel";
            }

            int id;
            Task<string> attente;
            lock (_startLock)
            {
                id = _nextId++;
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = tcs;
                attente = tcs.Task;
            }

            // body peut etre : null, JsonObject, Dictionary<string,object>, ou un objet anonyme.
            // mcp-saisie attend un objet JSON ; null -> {}.
            JsonObject arguments;
            if (body is null)
            {
                arguments = new JsonObject();
            }
            else if (body is JsonObject jo)
            {
                arguments = jo;
            }
            else
            {
                var node = JsonSerializer.SerializeToNode(body);
                arguments = node as JsonObject ?? new JsonObject();
            }

            var req = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = nomOutil,
                    ["arguments"] = arguments,
                },
            };

            lock (_startLock)
            {
                _stdin.WriteLine(req.ToJsonString());
                _stdin.Flush();
            }

            if (!attente.Wait(TimeSpan.FromSeconds(30)))
            {
                _pending.TryRemove(id, out _);
                return $"mcp-saisie: timeout (15s) sur {nomOutil}";
            }

            return attente.Result;
        }
        catch (Exception ex)
        {
            return $"erreur mcp-saisie: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void LireReponses(Process proc, CancellationToken ct)
    {
        try
        {
            string? line;
            while (!ct.IsCancellationRequested && (line = proc.StandardOutput.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var node = JsonNode.Parse(line);
                    if (node is null)
                    {
                        continue;
                    }
                    var idNode = node["id"];
                    if (idNode is null)
                    {
                        continue; // notification (pas de reponse a envoyer)
                    }
                    var id = idNode.GetValue<int>();
                    if (!_pending.TryRemove(id, out var tcs))
                    {
                        continue; // reponse pour un id qu'on n'attend plus
                    }

                    // Format : result.content[0].text = string renvoyee par l'outil.
                    // Ou error.message si le tool a jete.
                    var text = node["result"]?["content"]?[0]?["text"]?.GetValue<string>();
                    if (text is null)
                    {
                        var errMsg = node["error"]?["message"]?.GetValue<string>();
                        text = errMsg ?? node.ToJsonString();
                    }
                    tcs.TrySetResult(text);
                }
                catch
                {
                    // ligne non-JSON : on l'ignore, le reader continue
                }
            }
        }
        catch
        {
            // le process a surement quitte
        }
    }
}
