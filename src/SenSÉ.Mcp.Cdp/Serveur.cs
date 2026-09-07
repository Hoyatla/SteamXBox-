using System.Diagnostics;
using System.Net;
using SenSÉ.Mcp.Bus;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Mcp.Cdp;

/// <summary>
/// Le serveur HTTP mcp-cdp : expose les verbes CDP (navigate, eval,
/// click, type, wait, screenshot) sur 127.0.0.1. Sert d'adaptateur
/// entre l'Assistant (HttpClient) et le navigateur (WebSocket CDP).
/// </summary>
/// <remarks>
/// <b>HttpListener, pas TcpListener.</b> On utilise HttpListener cette
/// fois (au lieu du TcpListener + StreamReader/Writer de mcp-saisie)
/// parce que HttpListener gere pour nous le parsing de la request line,
/// des headers, et de Content-Length. C'est plus simple et plus robuste.
///
/// <para><b>Le navigateur attend le premier verbe, il ne se lance plus
/// au boot.</b> Il l'a fait, et cela se voyait : ouvrir la fenetre de
/// l'Assistant faisait surgir une fenetre Chromium alors que personne
/// n'avait parle du web. Le serveur detecte donc Edge (ou
/// Chrome/Chromium) et le lance avec
/// <c>--remote-debugging-port=9223</c> au premier appel seulement, puis
/// se connecte via <see cref="CdpClient"/>. Une seule instance du
/// navigateur est partagee entre tous les appels.</para>
/// </remarks>
public static class Serveur
{
    private static CdpClient? _client;
    private static Process? _browser;

    /// <summary>Serialise le lancement du navigateur. Voir <see cref="AssurerAsync"/>.</summary>
    private static readonly SemaphoreSlim _connexion = new(1, 1);

    /// <summary>Les verbes servis, connus avant que le navigateur ne soit lance.</summary>
    /// <remarks>
    /// Le nom est verifie d'abord parce que le lancement est devenu paresseux : sans cette liste,
    /// une faute de frappe dans un verbe ouvrirait une fenetre de navigateur pour repondre
    /// « outil inconnu ».
    /// </remarks>
    private static readonly HashSet<string> Verbes =
    [
        "cdp.navigate", "cdp.eval", "cdp.click", "cdp.type", "cdp.wait", "cdp.screenshot",
    ];

    public static async Task DemarrerAsync(int port = 9224, CancellationToken arret = default)
    {
        // Le navigateur n'est pas lance ici, et c'est tout le correctif : ouvrir la fenetre de
        // l'Assistant faisait surgir une fenetre Chromium alors que personne n'avait demande le
        // web. Un serveur qui se tient pret n'a pas a agir avant qu'on l'appelle.
        //
        // L'ecoute HTTP demarre donc tout de suite et le navigateur attend le premier verbe CDP.
        // Deuxieme benefice, non cherche : mcp-cdp ne peut plus retenir deux minutes le demarrage
        // des serveurs en sondant un port que rien n'ecoute.
        await Console.Error.WriteLineAsync("navigateur non lance: il attend le premier appel CDP.");

        // 4) Ecoute HTTP loopback
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        await Console.Error.WriteLineAsync($"mcp-cdp en ecoute sur http://127.0.0.1:{port}/");

        while (!arret.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = Task.Run(() => TraiterRequete(ctx));
        }
        listener.Stop();
    }

    /// <summary>Le navigateur, lance a la premiere demande et pas avant.</summary>
    /// <remarks>
    /// <b>Le verrou n'est pas une precaution, il est necessaire.</b> Les requetes sont servies en
    /// parallele (<c>Task.Run</c> par connexion) : deux verbes CDP arrivant ensemble lanceraient
    /// deux navigateurs, dont le second echouerait a prendre le port de debogage, et l'Assistant
    /// verrait une erreur qui ne se reproduit jamais a la main.
    ///
    /// <para>Un navigateur deja en ecoute sur 9223 est adopte plutot que double — c'est le cas
    /// quand l'utilisateur a ouvert le sien, ou quand une session precedente a laisse le sien.</para>
    /// </remarks>
    private static async Task<CdpClient> AssurerAsync(CancellationToken arret = default)
    {
        // Vivant, pas seulement present. Un client dont la socket est fermee etait resservi
        // indefiniment, et chaque verbe expirait sur un navigateur qui n'ecoutait plus.
        if (_client is { Vivant: true })
        {
            return _client;
        }

        await _connexion.WaitAsync(arret);

        try
        {
            // Reteste apres le verrou : celui qui attendait derriere n'a plus rien a faire.
            if (_client is { Vivant: true })
            {
                return _client;
            }

            if (_client is { } mort)
            {
                // Congedié avant d'en ouvrir un autre, sinon sa boucle de lecture et sa socket
                // survivraient a chaque reconnexion.
                _client = null;
                await Console.Error.WriteLineAsync("CDP: connexion morte, on en rouvre une.");
                try { await mort.DisposeAsync(); } catch { /* deja ferme */ }
            }

            using var sonde = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var dejaActif = false;

            try
            {
                var json = await sonde.GetStringAsync("http://127.0.0.1:9223/json/version", arret);
                dejaActif = !string.IsNullOrEmpty(json);
            }
            catch
            {
                // Rien n'ecoute : c'est le cas normal au premier appel.
            }

            if (dejaActif)
            {
                await Console.Error.WriteLineAsync("navigateur deja actif sur 9223, on l'adopte.");
            }
            else
            {
                // Sans nommer Edge : depuis que le Chromium du projet passe en premier, c'est lui
                // qu'on lance dans le cas courant. BrowserLauncher dit ensuite lequel il a retenu.
                await Console.Error.WriteLineAsync("aucun navigateur sur 9223, lancement...");
                _browser = SenSÉ.Mcp.Bus.BrowserLauncher.Lancer(9223);
                await Console.Error.WriteLineAsync($"navigateur lance, PID {_browser.Id} (port debug 9223)");
            }

            // Trente secondes, non plus deux minutes : l'appelant attend maintenant sa reponse
            // pendant cette sonde, et un navigateur qui n'a pas ouvert son port en trente secondes
            // ne l'ouvrira pas.
            CdpClient? client = null;

            for (var essai = 0; essai < 60 && !arret.IsCancellationRequested; essai++)
            {
                try
                {
                    client = new CdpClient();
                    await client.ConnectAsync(9223, arret);
                    break;
                }
                catch
                {
                    if (client is not null)
                    {
                        await client.DisposeAsync();
                        client = null;
                    }

                    await Task.Delay(500, arret);
                }
            }

            if (client is null)
            {
                await Console.Error.WriteLineAsync("ERREUR: CDP injoignable sur 9223 apres 30s");
                throw new InvalidOperationException("le navigateur n'a pas repondu sur 127.0.0.1:9223 en 30 s");
            }

            await Console.Error.WriteLineAsync("CDP connecte sur 127.0.0.1:9223");
            _client = client;

            return client;
        }
        finally
        {
            _connexion.Release();
        }
    }

    private static async Task TraiterRequete(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;
            string body = "";
            if (ctx.Request.HasEntityBody)
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }

            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            object sortie;
            if (path == "/" && method == "GET")
            {
                sortie = new
                {
                    ok = true,
                    serveur = "mcp-cdp",
                    version = "1.0.0",
                    browser = _browser?.Id,
                    cdp = _client is not null ? "connected" : "idle (le navigateur attend le premier verbe)",
                };
            }
            else
            {
                JsonObject? args = null;
                if (!string.IsNullOrEmpty(body))
                {
                    try { args = JsonNode.Parse(body) as JsonObject; }
                    catch (Exception ex)
                    {
                        sortie = new { ok = false, error = "JSON invalide: " + ex.Message };
                        await EcrireReponse(ctx, sortie);
                        return;
                    }
                }
                sortie = await ExecuterOutil(path, args);
            }
            await EcrireReponse(ctx, sortie);
        }
        catch (Exception ex)
        {
            try
            {
                await EcrireReponse(ctx, new { ok = false, error = ex.GetType().Name + ": " + ex.Message });
            }
            catch { /* ignore */ }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { /* ignore */ }
        }
    }

    private static async Task<object> ExecuterOutil(string path, JsonObject? args)
    {
        // On matche le path "/cdp/navigate" -> outil "cdp.navigate"
        var nom = path.TrimStart('/').Replace('/', '.');

        // Le nom est verifie avant que le navigateur ne soit lance : une faute de frappe dans un
        // verbe ne doit pas ouvrir une fenetre.
        if (!Verbes.Contains(nom))
        {
            return new { ok = false, error = "outil inconnu: " + nom + " (attendu cdp.navigate|eval|click|type|wait|screenshot)" };
        }

        CdpClient client;

        try
        {
            client = await AssurerAsync();
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }

        return nom switch
        {
            "cdp.navigate" => await Safe(async () =>
            {
                var url = args?["url"]?.GetValue<string>() ?? "";
                await client.NavigateAsync(url);
                return $"navigue vers {url}";
            }),
            "cdp.eval" => await Safe(async () =>
            {
                var expr = args?["expression"]?.GetValue<string>() ?? "";
                return await client.EvaluateAsync(expr);
            }),
            "cdp.click" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                await client.ClickAsync(sel);
                return $"click sur {sel}";
            }),
            "cdp.type" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                var txt = args?["text"]?.GetValue<string>() ?? "";
                await client.TypeAsync(sel, txt);
                return $"tape dans {sel}";
            }),
            "cdp.wait" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                int timeout = 5000;
                if (args?["timeout"] is JsonValue tv && tv.TryGetValue<int>(out var t)) timeout = t;
                await client.WaitForSelectorAsync(sel, timeout);
                return $"attendu {sel}";
            }),
            "cdp.screenshot" => await Safe(async () =>
            {
                var pathOut = args?["path"]?.GetValue<string>();
                if (string.IsNullOrEmpty(pathOut))
                {
                    var dir = Path.Combine(AppContext.BaseDirectory, "Captures");
                    Directory.CreateDirectory(dir);
                    pathOut = Path.Combine(dir, $"cdp-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pathOut)!);
                }
                await client.ScreenshotAsync(pathOut);
                return pathOut;
            }),
            _ => new { ok = false, error = "outil inconnu: " + nom + " (attendu cdp.navigate|eval|click|type|wait|screenshot)" },
        };
    }

    private static async Task<object> Safe(Func<Task<object>> action)
    {
        try
        {
            var data = await action();
            return new { ok = true, data };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static async Task EcrireReponse(HttpListenerContext ctx, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }
}
