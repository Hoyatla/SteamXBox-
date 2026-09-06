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
/// <para><b>Lance le navigateur au boot.</b> Le serveur detecte Edge
/// (ou Chrome/Chromium) au demarrage, le lance avec
/// <c>--remote-debugging-port=9223</c>, puis se connecte via
/// <see cref="CdpClient"/>. Une seule instance du navigateur est
/// partagee entre tous les appels.</para>
/// </remarks>
public static class Serveur
{
    private static CdpClient? _client;
    private static Process? _browser;

    public static async Task DemarrerAsync(int port = 9224, CancellationToken arret = default)
    {
        // 1) Verifie si un navigateur compatible tourne deja avec le port debug
        using var httpCheck = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        bool dejaActif = false;
        try
        {
            var json = await httpCheck.GetStringAsync("http://127.0.0.1:9223/json/version", arret);
            dejaActif = !string.IsNullOrEmpty(json);
            if (dejaActif)
            {
                await Console.Error.WriteLineAsync("navigateur detecte deja actif sur 9223, on ne lance pas Edge");
            }
        }
        catch
        {
            // Sans nommer Edge : depuis que le Chromium du projet passe en premier, c'est lui
            // qu'on lance dans le cas courant. BrowserLauncher dit ensuite lequel il a retenu.
            await Console.Error.WriteLineAsync("aucun navigateur actif sur 9223, lancement du navigateur...");
        }

        if (!dejaActif)
        {
            try
            {
                _browser = SenSÉ.Mcp.Bus.BrowserLauncher.Lancer(9223);
                await Console.Error.WriteLineAsync($"navigateur lance, PID {_browser.Id} (port debug 9223)");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"impossible de lancer le navigateur: {ex.Message}");
            }
        }

        // 2) Probe loop : 60 iterations x 2s = 120s max, log a chaque essai
        CdpClient? client = null;
        for (int i = 0; i < 60 && !arret.IsCancellationRequested; i++)
        {
            await Console.Error.WriteLineAsync($"probe {i + 1}/60: test 127.0.0.1:9223...");
            try
            {
                client = new CdpClient();
                await client.ConnectAsync(9223, arret);
                break;
            }
            catch
            {
                if (client is not null) await client.DisposeAsync();
                client = null;
                await Task.Delay(2000, arret);
            }
        }
        if (client is null)
        {
            await Console.Error.WriteLineAsync("ERREUR: impossible de se connecter a CDP apres 60 essais (120s)");
            throw new InvalidOperationException("CDP non disponible sur 127.0.0.1:9223 apres 120s");
        }
        _client = client;
        await Console.Error.WriteLineAsync("CDP connecte sur 127.0.0.1:9223");

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
                    cdp = _client is not null ? "connected" : "disconnected",
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
        if (_client is null)
        {
            return new { ok = false, error = "CDP non connecte (le navigateur n'a pas repondu sur 9223)" };
        }
        // On matche le path "/cdp/navigate" -> outil "cdp.navigate"
        var nom = path.TrimStart('/').Replace('/', '.');

        return nom switch
        {
            "cdp.navigate" => await Safe(async () =>
            {
                var url = args?["url"]?.GetValue<string>() ?? "";
                await _client.NavigateAsync(url);
                return $"navigue vers {url}";
            }),
            "cdp.eval" => await Safe(async () =>
            {
                var expr = args?["expression"]?.GetValue<string>() ?? "";
                return await _client.EvaluateAsync(expr);
            }),
            "cdp.click" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                await _client.ClickAsync(sel);
                return $"click sur {sel}";
            }),
            "cdp.type" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                var txt = args?["text"]?.GetValue<string>() ?? "";
                await _client.TypeAsync(sel, txt);
                return $"tape dans {sel}";
            }),
            "cdp.wait" => await Safe(async () =>
            {
                var sel = args?["selector"]?.GetValue<string>() ?? "";
                int timeout = 5000;
                if (args?["timeout"] is JsonValue tv && tv.TryGetValue<int>(out var t)) timeout = t;
                await _client.WaitForSelectorAsync(sel, timeout);
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
                await _client.ScreenshotAsync(pathOut);
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
