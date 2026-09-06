using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Le serveur HTTP mcp-saisie : expose les verbes de capture, souris,
/// clavier et screenshot sur un socket TCP en loopback (127.0.0.1).
/// </summary>
/// <remarks>
/// <b>Pourquoi HTTP et pas stdio.</b> Le binaire etant self-contained
/// single-file (PublishSingleFile=true) avec UseWindowsForms=true, la
/// redirection stdin/stdout sous PowerShell ou via Process.Start avec
/// RedirectStandardInput est instable : le pipe ne recoit pas les
/// donnees du parent, ou le StreamReader buffered ne les voit pas.
/// Un socket TCP loopback n'a pas ce probleme : c'est un flux reseau
/// classique que HttpClient consomme sans surprise.
///
/// <para><b>Pas d'auth en loopback.</b> On accepte toutes les requetes
/// venant de 127.0.0.1 sans token. Loopback = confiance sur Windows :
/// seul l'utilisateur local peut atteindre ce port. Si quelqu'un
/// d'autre sur la machine veut causer avec mcp-saisie, il peut, mais
/// il n'y a aucun moyen d'echapper au sandbox user par ce canal.
///
/// <para><b>Une connexion a la fois.</b> Le serveur accepte UN client
/// puis bloque jusqu'a ce qu'il ferme. C'est suffisant pour SenSÉ.Desktop
/// qui est le seul caller prevu. Si tu veux du multi-client, il faut
/// passer a un pool de threads ou async AcceptTcpClient en boucle.</para>
/// </remarks>
public static class Serveur
{
    /// <summary>Demarre le serveur HTTP sur 127.0.0.1:port. Bloque jusqu'a EOF ou arret demande.</summary>
    public static async Task DemarrerAsync(int port, CancellationToken arret = default)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        await Console.Error.WriteLineAsync($"mcp-saisie en ecoute sur 127.0.0.1:{port}").ConfigureAwait(false);

        while (!arret.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(arret).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await Console.Error.WriteLineAsync($"client connecte depuis {client.Client.RemoteEndPoint}").ConfigureAwait(false);
            try
            {
                await TraiterConnexionAsync(client, arret).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"erreur connexion: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                client.Dispose();
            }
            // Si le client ferme, on accepte le suivant.
        }

        listener.Stop();
    }

    private static async Task TraiterConnexionAsync(TcpClient client, CancellationToken arret)
    {
        using var stream = client.GetStream();
        // HTTP/1.1 basique. On gere une requete par connexion (Connection: close)
        // pour eviter de devoir parser les headers keep-alive.
        while (!arret.IsCancellationRequested && client.Connected)
        {
            var request = await LireRequeteAsync(stream, arret).ConfigureAwait(false);
            if (request is null) break; // EOF

            await EcrireReponseAsync(stream, request).ConfigureAwait(false);
            // Si le client a envoye Connection: close, on coupe.
            if (request.CloseConnexion) break;
        }
    }

    private static async Task<RequeteHttp?> LireRequeteAsync(NetworkStream stream, CancellationToken arret)
    {
        // Lit la request line
        var requestLine = await LireLigneAsync(stream, arret).ConfigureAwait(false);
        if (requestLine is null) return null;

        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 3) return null;
        var methode = parts[0];
        var path = parts[1];

        // Lit les headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var ligne = await LireLigneAsync(stream, arret).ConfigureAwait(false);
            if (ligne is null) return null;
            if (ligne.Length == 0) break; // fin des headers
            var idx = ligne.IndexOf(':');
            if (idx < 0) continue;
            var name = ligne.Substring(0, idx).Trim();
            var value = ligne.Substring(idx + 1).Trim();
            headers[name] = value;
        }

        // Lit le body si Content-Length
        var body = "";
        if (headers.TryGetValue("Content-Length", out var lenStr) && int.TryParse(lenStr, out var len) && len > 0)
        {
            var buf = new byte[len];
            var read = 0;
            while (read < len)
            {
                var n = await stream.ReadAsync(buf.AsMemory(read, len - read), arret).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            body = Encoding.UTF8.GetString(buf, 0, read);
        }

        var close = false;
        if (headers.TryGetValue("Connection", out var conn))
        {
            close = conn.Equals("close", StringComparison.OrdinalIgnoreCase);
        }

        return new RequeteHttp(methode, path, headers, body, close);
    }

    private static async Task<string?> LireLigneAsync(NetworkStream stream, CancellationToken arret)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buf.AsMemory(0, 1), arret).ConfigureAwait(false);
            if (n == 0) return sb.Length == 0 ? null : sb.ToString();
            var c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    private static async Task EcrireReponseAsync(NetworkStream stream, RequeteHttp req)
    {
        ReponseHttp resp;
        try
        {
            resp = TraiterRequete(req);
        }
        catch (Exception ex)
        {
            resp = new ReponseHttp(500, "application/json",
                $"{{\"ok\":false,\"error\":\"{ex.GetType().Name}: {ex.Message.Replace("\"", "\\\"")}\"}}");
        }

        var headersStr = new StringBuilder();
        headersStr.Append($"HTTP/1.1 {resp.Status} {StatusText(resp.Status)}\r\n");
        headersStr.Append($"Content-Type: {resp.ContentType}\r\n");
        headersStr.Append($"Content-Length: {Encoding.UTF8.GetByteCount(resp.Body)}\r\n");
        headersStr.Append("Connection: close\r\n");
        headersStr.Append("Access-Control-Allow-Origin: *\r\n");
        headersStr.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headersStr.ToString());
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        var bodyBytes = Encoding.UTF8.GetBytes(resp.Body);
        await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Unknown",
    };

    private static ReponseHttp TraiterRequete(RequeteHttp req)
    {
        // GET /  -> liste des outils (compatibilite debug)
        if (req.Methode == "GET" && req.Path == "/")
        {
            return new ReponseHttp(200, "application/json",
                JsonSerializer.Serialize(new
                {
                    ok = true,
                    serveur = "mcp-saisie",
                    version = "1.0.0",
                    outils = 10,
                }));
        }

        // POST /saisie/<tool> -> appelle l'outil avec le body JSON comme args
        if (req.Methode == "POST" && req.Path.StartsWith("/saisie/"))
        {
            var tool = req.Path.Substring("/saisie/".Length);
            return AppelerOutil(tool, req.Body);
        }

        return new ReponseHttp(404, "application/json",
            "{\"ok\":false,\"error\":\"route inconnue: " + req.Methode + " " + req.Path + "\"}");
    }

    private static ReponseHttp AppelerOutil(string tool, string bodyJson)
    {
        JsonNode? args = null;
        if (!string.IsNullOrWhiteSpace(bodyJson))
        {
            try { args = JsonNode.Parse(bodyJson); }
            catch (Exception ex) { return Erreur(400, "JSON invalide: " + ex.Message); }
        }
        var argsObj = args as JsonObject ?? new JsonObject();

        string sortie;
        try
        {
            sortie = tool switch
            {
                "screenshot_ecran" => Capture.Ecran(
                    argsObj["moniteur"]?.GetValue<int>() ?? 0,
                    argsObj["format"]?.GetValue<string>() ?? "png"),
                "screenshot_fenetre" => Capture.FenetreParTitre(
                    argsObj["titre"]?.GetValue<string>() ?? "",
                    argsObj["format"]?.GetValue<string>() ?? "png"),
                "lister_fenetres" => string.Join("\n",
                    Capture.ListerFenetres().Select(f => $"0x{f.Handle.ToInt64():X} {f.Titre}")),
                "souris_deplacer" => Souris.Deplacer(
                    argsObj["x"]?.GetValue<int>() ?? 0,
                    argsObj["y"]?.GetValue<int>() ?? 0),
                "souris_cliquer" => Souris.Cliquer(
                    argsObj["bouton"]?.GetValue<string>() ?? "gauche",
                    argsObj["doubles"]?.GetValue<bool>() ?? false,
                    argsObj["x"]?.GetValue<int?>(),
                    argsObj["y"]?.GetValue<int?>()),
                "souris_molette" => Souris.Molette(
                    argsObj["delta"]?.GetValue<int>() ?? 0,
                    argsObj["axe"]?.GetValue<string>() ?? "vertical"),
                "clavier_taper" => Clavier.Taper(
                    argsObj["texte"]?.GetValue<string>() ?? ""),
                "clavier_touche" => Clavier.Toucher(
                    argsObj["touche"]?.GetValue<string>() ?? "",
                    ArgsStringArray(argsObj, "modificateurs")),
                "mode_exclusif_ouvrir" => OuvrirModeExclusif(
                    argsObj["sequence"]?.GetValue<string>() ?? ""),
                "mode_exclusif_fermer" => FermerModeExclusif(),
                _ => throw new InvalidOperationException($"outil inconnu: {tool}"),
            };
        }
        catch (Exception ex)
        {
            return Erreur(500, $"{ex.GetType().Name}: {ex.Message}");
        }

        return new ReponseHttp(200, "application/json",
            JsonSerializer.Serialize(new { ok = true, data = sortie }));
    }

    private static IReadOnlyList<string> ArgsStringArray(JsonObject args, string cle)
    {
        var tableau = args[cle] as JsonArray;
        if (tableau is null) return [];
        return tableau.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
    }

    private static string OuvrirModeExclusif(string sequence)
    {
        ModeExclusif.Ouvrir("mcp-saisie", sequence);
        return $"mode exclusif ouvert: {sequence}";
    }

    private static string FermerModeExclusif()
    {
        ModeExclusif.Fermer();
        return "mode exclusif ferme";
    }

    private static ReponseHttp Erreur(int status, string message) => new(status, "application/json",
        JsonSerializer.Serialize(new { ok = false, error = message }));

    private sealed record RequeteHttp(string Methode, string Path, Dictionary<string, string> Headers, string Body, bool CloseConnexion);

    private sealed record ReponseHttp(int Status, string ContentType, string Body);
}
