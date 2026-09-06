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
/// <para><b>Une requete par connexion, plusieurs connexions a la fois.</b>
/// Chaque connexion acceptee part dans sa propre tache et le serveur retourne
/// aussitot attendre la suivante ; la connexion est fermee des que la reponse
/// est ecrite, puisque celle-ci annonce toujours "Connection: close".
///
/// La version precedente attendait, dans la boucle d'accept, que le client
/// ferme son socket. HttpClient ouvrant une connexion par appel d'outil et ne
/// la fermant pas toujours dans la seconde, chaque appel bloquait le suivant
/// jusqu'a son timeout de 30 s : cote Assistant, un
/// "HttpRequestException: Error while copying content to a stream" sans cause
/// visible. Servir un seul client a la fois n'etait pas une simplification, mais
/// le defaut lui-meme.
///
/// L'execution des outils reste, elle, serialisee par un verrou : un seul
/// SendInput a la fois, sinon deux frappes s'entrelacent.</para>
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

            // Une tache par connexion, et on repart aussitot attendre la suivante.
            //
            // Auparavant la boucle attendait ici la fin de la connexion courante, et
            // TraiterConnexionAsync ne rendait la main qu'a l'EOF du client. Tant que
            // HttpClient n'avait pas ferme son socket, le serveur n'acceptait plus rien :
            // l'appel d'outil suivant restait en attente jusqu'a son timeout de 30 s, et
            // l'Assistant recevait « HttpRequestException: Error while copying content to
            // a stream ». Un serveur qui ne sert qu'un client a la fois n'est pas une
            // simplification quand le client, lui, en ouvre un par appel.
            //
            // L'execution des outils, elle, reste sequentielle : voir _executions plus
            // bas. Ce qui devait cesser d'etre sequentiel, c'est la duree de vie des
            // connexions, pas l'ordre des frappes.
            var courant = client;
            _ = Task.Run(async () =>
            {
                try
                {
                    await TraiterConnexionAsync(courant, arret).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"erreur connexion: {ex.Message}").ConfigureAwait(false);
                }
                finally
                {
                    courant.Dispose();
                }
            }, arret);
        }

        listener.Stop();
    }

    private static async Task TraiterConnexionAsync(TcpClient client, CancellationToken arret)
    {
        using var stream = client.GetStream();
        // HTTP/1.1 basique. On gere une requete par connexion (Connection: close)
        // pour eviter de devoir parser les headers keep-alive.
        var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "?";
        while (!arret.IsCancellationRequested && client.Connected)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string reqLog = "?";
            var request = await LireRequeteAsync(stream, arret).ConfigureAwait(false);
            if (request is null)
            {
                await Console.Error.WriteLineAsync("[mcp-saisie] connexion de " + remoteEp + " fermee par le client").ConfigureAwait(false);
                break;
            }
            reqLog = request.Methode + " " + request.Path;
            await Console.Error.WriteLineAsync("[mcp-saisie] connexion de " + remoteEp + ", requete " + reqLog).ConfigureAwait(false);

            await EcrireReponseAsync(stream, request).ConfigureAwait(false);
            sw.Stop();
            await Console.Error.WriteLineAsync("[mcp-saisie] connexion de " + remoteEp + ", requete " + reqLog + ", " + sw.ElapsedMilliseconds + "ms, OK").ConfigureAwait(false);

            // On coupe apres avoir repondu, sans consulter request.CloseConnexion.
            //
            // La reponse annonce toujours « Connection: close » : le client ne reutilisera
            // donc jamais cette connexion, et attendre son EOF ne servait qu'a garder un
            // socket et une tache ouverts pour rien. Le test portait de toute facon sur le
            // mauvais bout du fil — HttpClient parle en HTTP/1.1 keep-alive et n'envoie
            // jamais « Connection: close » dans sa requete, si bien que la boucle ne
            // s'arretait jamais par ce chemin.
            break;
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

        // Lit le body : soit une longueur annoncee, soit un corps decoupe en morceaux.
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
        else if (headers.TryGetValue("Transfer-Encoding", out var encodage)
                 && encodage.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await LireCorpsDecoupeAsync(stream, arret).ConfigureAwait(false);
        }

        var close = false;
        if (headers.TryGetValue("Connection", out var conn))
        {
            close = conn.Equals("close", StringComparison.OrdinalIgnoreCase);
        }

        return new RequeteHttp(methode, path, headers, body, close);
    }

    /// <summary>Lit un corps envoye en <c>Transfer-Encoding: chunked</c>.</summary>
    /// <remarks>
    /// <b>Ce que son absence a coute.</b> Le serveur ne lisait le corps que sur presence
    /// d'un <c>Content-Length</c>. Or l'Assistant appelle via
    /// <c>HttpClient.PostAsJsonAsync</c>, et le <c>JsonContent</c> qu'il construit ne sait
    /// pas calculer sa longueur a l'avance : HttpClient bascule alors en chunked. Le corps
    /// etait donc jete en silence, et chaque appel arrivait sans ses arguments —
    /// <c>clavier_taper</c> repondait « rien a taper », <c>souris_deplacer</c> « deplace a
    /// (0,0) », <c>mode_exclusif_ouvrir</c> ouvrait un bandeau sans texte. Aucun de ces
    /// retours ne ressemblait a une erreur, ce qui est le pire cas.
    ///
    /// <para>Pire encore, les octets non lus restaient dans le socket. Le serveur repondait
    /// puis fermait la connexion, et le client recevait un RST en pleine lecture :
    /// <c>SocketException 10054</c>, remontee en
    /// <c>HttpRequestException: Error while copying content to a stream</c>. Un seul defaut,
    /// les deux symptomes.</para>
    ///
    /// <para>Le format est celui de la RFC 9112 : une ligne de taille en hexadecimal
    /// (eventuellement suivie d'un « ; » et d'extensions qu'on ignore), les octets, un CRLF,
    /// et ainsi de suite jusqu'a une taille nulle — puis d'eventuels trailers, lus et jetes
    /// jusqu'a la ligne vide. Les lire est ce qui laisse le flux propre pour la suite.</para>
    /// </remarks>
    private static async Task<string> LireCorpsDecoupeAsync(NetworkStream stream, CancellationToken arret)
    {
        var corps = new MemoryStream();

        while (true)
        {
            var ligneTaille = await LireLigneAsync(stream, arret).ConfigureAwait(false);
            if (ligneTaille is null) break; // EOF prematuree : on rend ce qu'on a.

            // « 1a7 » ou « 1a7;nom=valeur » : seule la partie avant le « ; » compte.
            var pointVirgule = ligneTaille.IndexOf(';');
            if (pointVirgule >= 0) ligneTaille = ligneTaille.Substring(0, pointVirgule);
            ligneTaille = ligneTaille.Trim();
            if (ligneTaille.Length == 0) continue;

            if (!int.TryParse(ligneTaille, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var taille))
            {
                break; // Taille illisible : le flux n'est plus interpretable, on s'arrete.
            }
            if (taille <= 0) break; // Morceau de taille nulle : fin du corps.

            var tampon = new byte[taille];
            var lus = 0;
            while (lus < taille)
            {
                var n = await stream.ReadAsync(tampon.AsMemory(lus, taille - lus), arret).ConfigureAwait(false);
                if (n == 0) break;
                lus += n;
            }
            corps.Write(tampon, 0, lus);
            if (lus < taille) break;

            // Le CRLF qui suit les octets du morceau, et qui n'en fait pas partie.
            await LireLigneAsync(stream, arret).ConfigureAwait(false);
        }

        // Trailers eventuels, jusqu'a la ligne vide qui clot le message.
        while (true)
        {
            var ligne = await LireLigneAsync(stream, arret).ConfigureAwait(false);
            if (ligne is null || ligne.Length == 0) break;
        }

        return Encoding.UTF8.GetString(corps.ToArray());
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

    /// <summary>
    /// Un outil a la fois, quel que soit le nombre de connexions ouvertes.
    /// </summary>
    /// <remarks>
    /// Les connexions sont servies en parallele depuis que la boucle d'accept ne les
    /// attend plus une par une, mais ce qu'elles declenchent — SendInput, capture GDI —
    /// vise un seul ecran et un seul clavier. Deux frappes qui s'entrelacent ecrivent un
    /// mot melange, et deux captures concurrentes se disputent le meme DC. La reponse
    /// tient dans un verrou : la concurrence sert a ne pas bloquer l'accept, pas a
    /// piloter deux choses a la fois.
    /// </remarks>
    private static readonly SemaphoreSlim _executions = new(1, 1);

    private static async Task EcrireReponseAsync(NetworkStream stream, RequeteHttp req)
    {
        ReponseHttp resp;
        await _executions.WaitAsync().ConfigureAwait(false);
        try
        {
            resp = TraiterRequete(req);
        }
        catch (Exception ex)
        {
            resp = new ReponseHttp(500, "application/json",
                $"{{\"ok\":false,\"error\":\"{ex.GetType().Name}: {ex.Message.Replace("\"", "\\\"")}\"}}");
        }
        finally
        {
            _executions.Release();
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
