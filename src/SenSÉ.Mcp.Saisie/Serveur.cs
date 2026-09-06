using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Windows;
using SenSÉ.Mcp.Bus;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Le serveur MCP mcp-saisie : expose les verbes de capture, souris,
/// clavier et screenshot sur stdio, en JSON-RPC 2.0 newline-delimited.
/// </summary>
/// <remarks>
/// <b>Stdio, pas HTTP.</b> Pour un client in-process, stdio est plus
/// simple (pas de port, pas de pare-feu, pas de conflit). Le transport
/// est le standard MCP : une requete par ligne, une reponse par ligne.
///
/// <para><b>Le Dispatcher WPF.</b> WPF exige un thread STA pour creer
/// des fenetres. Le serveur tourne sur le thread principal qui n'est
/// pas forcement STA. On utilise <see cref="Application"/> sur le
/// thread courant, et tout le code UI (ModeExclusif) est dispatche
/// dessus.</para>
/// </remarks>
public static class Serveur
{
    /// <summary>Demarre le serveur sur stdio. Bloque jusqu'a EOF ou erreur.</summary>
    public static async Task DemarrerAsync(CancellationToken arret = default)
    {
        var outils = ListeOutils();
        using var lecteur = new StreamReader(Console.OpenStandardInput());
        using var ecrivain = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        await ecrivain.WriteLineAsync(RepondrePret()).ConfigureAwait(false);

        while (!arret.IsCancellationRequested)
        {
            var ligne = await lecteur.ReadLineAsync(arret).ConfigureAwait(false);
            if (ligne is null) break;
            if (string.IsNullOrWhiteSpace(ligne)) continue;

            var reponse = Traiter(ligne, outils);
            if (reponse is not null)
            {
                await ecrivain.WriteLineAsync(reponse).ConfigureAwait(false);
            }
        }
    }

    private static string RepondrePret()
    {
        var n = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/ready",
            ["params"] = new JsonObject
            {
                ["serveur"] = "mcp-saisie",
                ["version"] = "1.0.0",
                ["outils"] = ListeOutils().Count,
            },
        };
        return n.ToJsonString();
    }

    private static IReadOnlyList<JsonObject> ListeOutils()
    {
        return
        [
            Outil("saisie.screenshot_ecran",
                "Capture l'ecran entier, ou un moniteur precis. Retourne le chemin du fichier ecrit.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["moniteur"] = new JsonObject { ["type"] = "integer", ["description"] = "0 = tous, 1+ = ecran precis" },
                        ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("png", "jpg", "bmp") },
                    },
                }),
            Outil("saisie.screenshot_fenetre",
                "Capture la fenetre identifiee par titre (regex partielle) ou handle (HWND).",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["titre"] = new JsonObject { ["type"] = "string" },
                        ["handle"] = new JsonObject { ["type"] = "integer" },
                        ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("png", "jpg", "bmp") },
                    },
                }),
            Outil("saisie.lister_fenetres",
                "Liste les fenetres visibles avec leur titre, pour que l'Assistant puisse choisir.",
                new JsonObject { ["type"] = "object" }),
            Outil("saisie.souris_deplacer",
                "Deplace le curseur a une position absolue (coords ecran). Mode exclusif obligatoire.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("x", "y"),
                    ["properties"] = new JsonObject
                    {
                        ["x"] = new JsonObject { ["type"] = "integer" },
                        ["y"] = new JsonObject { ["type"] = "integer" },
                    },
                }),
            Outil("saisie.souris_cliquer",
                "Clic a la position courante, ou aux coordonnees indiquees.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("bouton"),
                    ["properties"] = new JsonObject
                    {
                        ["bouton"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("gauche", "droit", "milieu") },
                        ["doubles"] = new JsonObject { ["type"] = "boolean" },
                        ["x"] = new JsonObject { ["type"] = "integer" },
                        ["y"] = new JsonObject { ["type"] = "integer" },
                    },
                }),
            Outil("saisie.souris_molette",
                "Fait tourner la molette. delta positif = haut, negatif = bas.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("delta"),
                    ["properties"] = new JsonObject
                    {
                        ["delta"] = new JsonObject { ["type"] = "integer" },
                        ["axe"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("vertical", "horizontal") },
                    },
                }),
            Outil("saisie.clavier_taper",
                "Tape une chaine de caracteres, un caractere a la fois.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("texte"),
                    ["properties"] = new JsonObject
                    {
                        ["texte"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            Outil("saisie.clavier_touche",
                "Appuie sur une touche speciale (Entree, Echap, Tab, F1..F12, fleches) avec modificateurs.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("touche"),
                    ["properties"] = new JsonObject
                    {
                        ["touche"] = new JsonObject { ["type"] = "string" },
                        ["modificateurs"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray("Ctrl", "Shift", "Alt", "Win"),
                            },
                        },
                    },
                }),
            Outil("saisie.mode_exclusif_ouvrir",
                "Ouvre l'overlay 'L'ASSISTANT PILOTE'. A appeler avant toute action souris/clavier.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("sequence"),
                    ["properties"] = new JsonObject
                    {
                        ["sequence"] = new JsonObject { ["type"] = "string", ["description"] = "Description courte, ex: 'clic dans VS Code'" },
                    },
                }),
            Outil("saisie.mode_exclusif_fermer",
                "Ferme l'overlay. A appeler apres la derniere action souris/clavier.",
                new JsonObject { ["type"] = "object" }),
        ];
    }

    private static JsonObject Outil(string nom, string description, JsonObject schema)
    {
        return new JsonObject
        {
            ["name"] = nom,
            ["description"] = description,
            ["inputSchema"] = schema,
        };
    }

    private static string? Traiter(string ligne, IReadOnlyList<JsonObject> outils)
    {
        try
        {
            var noeud = JsonNode.Parse(ligne);
            if (noeud is null) return null;

            var echange = noeud.Deserialize<EchangeJsonRpc>();
            if (echange is null) return null;

            if (echange.EstNotification)
            {
                TraiterNotification(echange, outils);
                return null;
            }

            if (echange.EstRequete)
            {
                var id = echange.Id ?? "";
                try
                {
                    var resultat = ExecuterMethode(echange.Method ?? "", echange.Params, outils);
                    return EchangeJsonRpc.ReponseSucces(id, resultat);
                }
                catch (Exception ex)
                {
                    return EchangeJsonRpc.ReponseErreur(id, -32000, ex.Message);
                }
            }

            return null;
        }
        catch (JsonException ex)
        {
            return EchangeJsonRpc.ReponseErreur(null, -32700, $"JSON invalide: {ex.Message}");
        }
    }

    private static void TraiterNotification(EchangeJsonRpc echange, IReadOnlyList<JsonObject> outils)
    {
        // Rien a faire pour l'instant. Une notification est un "fire and forget".
        // Si le client veut nous informer de quelque chose, on pourrait le traiter ici.
    }

    private static JsonNode? ExecuterMethode(string methode, JsonNode? parametres, IReadOnlyList<JsonObject> outils)
    {
        return methode switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "mcp-saisie", ["version"] = "1.0.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = new JsonArray(outils.Select(o => o).ToArray()) },
            "tools/call" => AppelerOutil(parametres),
            _ => throw new InvalidOperationException($"methode inconnue: {methode}"),
        };
    }

    private static JsonNode? AppelerOutil(JsonNode? parametres)
    {
        if (parametres is null) throw new InvalidOperationException("params requis");
        var nom = parametres["name"]?.GetValue<string>() ?? "";
        var args = parametres["arguments"] as JsonObject ?? new JsonObject();

        var sortie = nom switch
        {
            "saisie.screenshot_ecran" => Capture.Ecran(
                args["moniteur"]?.GetValue<int>() ?? 0,
                args["format"]?.GetValue<string>() ?? "png"),
            "saisie.screenshot_fenetre" => Capture.FenetreParTitre(
                args["titre"]?.GetValue<string>() ?? "",
                args["format"]?.GetValue<string>() ?? "png"),
            "saisie.lister_fenetres" => string.Join("\n",
                Capture.ListerFenetres().Select(f => $"0x{f.Handle.ToInt64():X} {f.Titre}")),
            "saisie.souris_deplacer" => Souris.Deplacer(
                args["x"]?.GetValue<int>() ?? 0,
                args["y"]?.GetValue<int>() ?? 0),
            "saisie.souris_cliquer" => Souris.Cliquer(
                args["bouton"]?.GetValue<string>() ?? "gauche",
                args["doubles"]?.GetValue<bool>() ?? false,
                args["x"]?.GetValue<int>(),
                args["y"]?.GetValue<int>()),
            "saisie.souris_molette" => Souris.Molette(
                args["delta"]?.GetValue<int>() ?? 0,
                args["axe"]?.GetValue<string>() ?? "vertical"),
            "saisie.clavier_taper" => Clavier.Taper(args["texte"]?.GetValue<string>() ?? ""),
            "saisie.clavier_touche" => Clavier.Toucher(
                args["touche"]?.GetValue<string>() ?? "",
                ArgsStringArray(args, "modificateurs")),
            "saisie.mode_exclusif_ouvrir" => OuvrirModeExclusif(args["sequence"]?.GetValue<string>() ?? ""),
            "saisie.mode_exclusif_fermer" => FermerModeExclusif(),
            _ => throw new InvalidOperationException($"outil inconnu: {nom}"),
        };

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = sortie },
            },
        };
    }

    private static IReadOnlyList<string> ArgsStringArray(JsonObject args, string cle)
    {
        var tableau = args[cle] as JsonArray;
        if (tableau is null) return [];
        return tableau.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
    }

    private static string OuvrirModeExclusif(string sequence)
    {
        // ModeExclusif est un overlay Win32 (CreateWindowEx +
        // UpdateLayeredWindow), pas de WPF. Il s'execute directement
        // sur le main thread STA de mcp-saisie, sans thread dedie ni
        // dispatcher. Synchrone : on attend que la fenetre soit peinte
        // avant de retourner au client MCP.
        ModeExclusif.Ouvrir("mcp-saisie", sequence);
        return $"mode exclusif ouvert: {sequence}";
    }

    private static string FermerModeExclusif()
    {
        ModeExclusif.Fermer();
        return "mode exclusif ferme";
    }
}