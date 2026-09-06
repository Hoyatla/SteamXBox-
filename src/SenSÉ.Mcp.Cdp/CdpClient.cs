using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Mcp.Cdp;

/// <summary>
/// Client Chrome DevTools Protocol via WebSocket. Supporte les
/// methodes CDP classiques (Page.navigate, Runtime.evaluate,
/// Page.captureScreenshot, etc.).
/// </summary>
/// <remarks>
/// <b>Pas de client WebDriver.</b> On parle directement le protocole
/// CDP sur WebSocket. C'est plus leger que Selenium et ca marche avec
/// n'importe quel Chromium-compatible qui expose <c>--remote-debugging-port</c>.
///
/// <para><b>Thread-safe.</b> Le <see cref="SendAsync"/> est serialise
/// par un <see cref="SemaphoreSlim"/> ; la <see cref="BoucleLectureAsync"/>
/// accede au dictionnaire des requetes en attente sous le meme lock.
/// Les reponses sont dispatchees via TCS pour que chaque appelant
/// attende sa propre reponse.</para>
/// </remarks>
public sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private int _idCounter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _readerTask;

    /// <summary>Se connecte au navigateur via son webSocketDebuggerUrl. Demarre le reader.</summary>
    public async Task ConnectAsync(int port = 9223, CancellationToken arret = default)
    {
        // Le navigateur expose /json/version avec le webSocketDebuggerUrl.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", arret);
        var noeud = JsonNode.Parse(json);
        var wsUrl = noeud?["webSocketDebuggerUrl"]?.GetValue<string>()
            ?? throw new InvalidOperationException("webSocketDebuggerUrl manquant dans /json/version");

        await _ws.ConnectAsync(new Uri(wsUrl), arret);
        _readerTask = Task.Run(() => BoucleLectureAsync(_cts.Token), _cts.Token);
    }

    /// <summary>Envoie une commande CDP et attend la reponse.</summary>
    public async Task<JsonNode?> SendAsync(string method, object? parameters = null, CancellationToken arret = default)
    {
        await _sendLock.WaitAsync(arret);
        try
        {
            var id = Interlocked.Increment(ref _idCounter);
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var req = new JsonObject
            {
                ["id"] = id,
                ["method"] = method,
            };
            if (parameters is not null)
            {
                req["params"] = JsonSerializer.SerializeToNode(parameters);
            }

            var bytes = Encoding.UTF8.GetBytes(req.ToJsonString());
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, arret);

            return await tcs.Task.WaitAsync(arret);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Execute du JS dans la page et renvoie la valeur retournee (en string JSON).</summary>
    public async Task<string> EvaluateAsync(string expression, CancellationToken arret = default)
    {
        var result = await SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true,
        }, arret);
        var value = result?["result"]?["result"]?["value"];
        if (value is null) return "";
        // value est un JsonNode ; on le rend tel quel en string
        return value.ToJsonString();
    }

    /// <summary>Navigue vers une URL. Attend 1,5s pour que la page charge (best-effort).</summary>
    public async Task NavigateAsync(string url, CancellationToken arret = default)
    {
        await SendAsync("Page.navigate", new { url }, arret);
        // Best-effort : attendre que la page rende
        try { await Task.Delay(1500, arret); } catch (OperationCanceledException) { }
    }

    /// <summary>Clique sur le premier element matchant le selecteur CSS. Echoue silencieusement si pas trouve.</summary>
    public async Task ClickAsync(string selector, CancellationToken arret = default)
    {
        var js = $"document.querySelector({JsonSerializer.Serialize(selector)})?.click()";
        await EvaluateAsync(js, arret);
    }

    /// <summary>Tape du texte dans un champ : focus + set value + dispatch input/change events.</summary>
    public async Task TypeAsync(string selector, string text, CancellationToken arret = default)
    {
        var js = $@"
            (() => {{
                const el = document.querySelector({JsonSerializer.Serialize(selector)});
                if (!el) return 'no element';
                el.focus();
                el.value = {JsonSerializer.Serialize(text)};
                el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                return 'ok';
            }})()";
        await EvaluateAsync(js, arret);
    }

    /// <summary>Prend un screenshot PNG et l'ecrit dans <paramref name="path"/>.</summary>
    public async Task ScreenshotAsync(string path, CancellationToken arret = default)
    {
        var result = await SendAsync("Page.captureScreenshot", new { format = "png" }, arret);
        var data = result?["data"]?.GetValue<string>() ?? "";
        var bytes = Convert.FromBase64String(data);
        await File.WriteAllBytesAsync(path, bytes, arret);
    }

    /// <summary>Attend qu'un selecteur CSS apparaisse dans le DOM. Rejette si timeout.</summary>
    public async Task WaitForSelectorAsync(string selector, int timeoutMs = 5000, CancellationToken arret = default)
    {
        var js = $@"new Promise((resolve, reject) => {{
            const start = Date.now();
            const check = () => {{
                if (document.querySelector({JsonSerializer.Serialize(selector)})) resolve('ok');
                else if (Date.now() - start > {timeoutMs}) reject('timeout');
                else setTimeout(check, 100);
            }};
            check();
        }})";
        await EvaluateAsync(js, arret);
    }

    /// <summary>Lit les messages du WebSocket et dispatche les reponses aux TCS en attente.</summary>
    private async Task BoucleLectureAsync(CancellationToken arret)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!arret.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, arret);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                try
                {
                    var noeud = JsonNode.Parse(sb.ToString());
                    if (noeud is JsonObject obj && obj["id"] is JsonValue val)
                    {
                        var id = val.GetValue<int>();
                        TaskCompletionSource<JsonNode?>? tcs;
                        // Lock pour eviter la race avec SendAsync qui ajoute en parallele
                        await _sendLock.WaitAsync(arret);
                        try
                        {
                            _pending.Remove(id, out tcs);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                        if (tcs is not null)
                        {
                            var err = obj["error"];
                            if (err is not null)
                            {
                                tcs.TrySetException(new InvalidOperationException(err.ToJsonString()));
                            }
                            else
                            {
                                tcs.TrySetResult(obj["result"]);
                            }
                        }
                    }
                    // Si pas de champ "id", c'est un event CDP ; on l'ignore pour l'instant.
                }
                catch
                {
                    // Message non-JSON ou erreur de parsing ; on continue
                }
            }
        }
        catch (OperationCanceledException)
        {
            // arret demande
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("CDP boucle lecture erreur: " + ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ws.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default); }
            catch { /* ignore */ }
        }
        _ws.Dispose();
        _sendLock.Dispose();
        _cts.Dispose();
    }
}
