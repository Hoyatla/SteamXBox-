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
/// <para><b>Le verrou ne couvre que l'envoi, et c'est la correction.</b> Il couvrait
/// aussi l'attente de la reponse, que seule <see cref="BoucleLectureAsync"/> peut
/// donner — laquelle prenait ce meme verrou pour retirer la requete du dictionnaire.
/// Chacun attendait l'autre : la premiere commande CDP ne pouvait pas aboutir, le
/// verrou n'etait jamais rendu, et toutes les suivantes s'empilaient derriere.
/// Constate le 7 septembre 2026, ou trois <c>cdp_navigate</c> de suite ont expire a
/// trente secondes, l'un apres l'autre. mcp-cdp n'avait jamais pilote une page.</para>
///
/// <para>Le verrou ne sert donc plus qu'a ce pour quoi il existe :
/// <see cref="System.Net.WebSockets.ClientWebSocket.SendAsync"/> interdit deux envois
/// simultanes. Le dictionnaire est devenu concurrent, la boucle de lecture n'a plus
/// rien a verrouiller, et l'attente se fait dehors.</para>
/// </remarks>
public sealed class CdpClient : IAsyncDisposable
{
    /// <summary>Au-dela, la reponse ne viendra pas.</summary>
    /// <remarks>
    /// <b>Une attente sans borne n'est pas une attente, c'est une panne silencieuse.</b> Aucun
    /// appelant ne passait de jeton d'annulation — tous prenaient le defaut — si bien qu'une
    /// socket morte laissait la requete en suspens pour toujours. Ce qui sauvait la mise etait le
    /// delai HTTP du serveur, trente secondes, qui rendait a l'assistant une erreur parlant de
    /// <c>HttpClient</c> plutot que du navigateur.
    ///
    /// <para>Vingt secondes : assez pour un chargement de page lent, assez court pour laisser le
    /// serveur repondre quelque chose d'utile avant que son propre client ne renonce.</para>
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly ClientWebSocket _ws = new();
    private int _idCounter;

    /// <summary>Serialise les envois sur la socket, rien de plus.</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _readerTask;

    /// <summary>Le client peut-il encore repondre ?</summary>
    /// <remarks>
    /// Demande par <c>Serveur.AssurerAsync</c>, qui gardait son client sans jamais verifier qu'il
    /// etait vivant et resservait donc un cadavre a chaque appel.
    /// </remarks>
    public bool Vivant => _ws.State == WebSocketState.Open;

    /// <summary>Se connecte a une PAGE du navigateur. Demarre le reader.</summary>
    /// <remarks>
    /// <b>A une page, et non au navigateur : c'est ce qui manquait pour que les verbes existent.</b>
    /// <c>/json/version</c> donne la socket du <i>navigateur</i>, qui ne connait que les domaines
    /// <c>Browser</c> et <c>Target</c>. Toutes les commandes de ce client — <c>Page.navigate</c>,
    /// <c>Runtime.evaluate</c>, <c>Page.captureScreenshot</c> — appartiennent a un onglet, et le
    /// navigateur repondait donc <c>-32601 « 'Page.navigate' wasn't found »</c> a chacune.
    ///
    /// <para>
    /// Le defaut etait invisible tant que l'interblocage du verrou d'envoi empechait la moindre
    /// commande d'aboutir : on ne voyait qu'un delai de trente secondes. La premiere reparation a
    /// mis la seconde au jour, ce qui est la seule facon d'apprendre qu'un chemin n'a jamais servi.
    /// </para>
    ///
    /// <para><c>/json/list</c> enumere les cibles ; on retient le premier onglet. S'il n'y en a
    /// aucun — navigateur ouvert sans fenetre — on en fait creer un par <c>/json/new</c> plutot que
    /// d'echouer, parce qu'un navigateur sans onglet est un etat qu'on traverse au demarrage.</para>
    /// </remarks>
    public async Task ConnectAsync(int port = 9223, CancellationToken arret = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var wsUrl = await PageAsync(http, port, arret)
            ?? await NouvellePageAsync(http, port, arret)
            ?? throw new InvalidOperationException(
                "aucun onglet a piloter sur 127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await _ws.ConnectAsync(new Uri(wsUrl), arret);
        _readerTask = Task.Run(() => BoucleLectureAsync(_cts.Token), _cts.Token);
    }

    /// <summary>La socket du premier onglet, ou null s'il n'y en a pas.</summary>
    private static async Task<string?> PageAsync(HttpClient http, int port, CancellationToken arret)
    {
        var json = await http.GetStringAsync(
            $"http://127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/json/list", arret);

        if (JsonNode.Parse(json) is not JsonArray cibles)
        {
            return null;
        }

        foreach (var cible in cibles)
        {
            // « page » et non « background_page », « service_worker » ou « iframe » : ce sont des
            // cibles reelles, mais aucune n'est l'onglet que l'utilisateur regarde.
            if (cible?["type"]?.GetValue<string>() == "page"
                && cible["webSocketDebuggerUrl"]?.GetValue<string>() is { Length: > 0 } socket)
            {
                return socket;
            }
        }

        return null;
    }

    /// <summary>Fait ouvrir un onglet, et rend sa socket.</summary>
    private static async Task<string?> NouvellePageAsync(HttpClient http, int port, CancellationToken arret)
    {
        var adresse = $"http://127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/json/new?about:blank";

        // PUT depuis Chrome 111 ; le GET reste accepte par d'autres Chromium, d'ou les deux.
        var reponse = await http.PutAsync(adresse, content: null, arret);

        if (!reponse.IsSuccessStatusCode)
        {
            reponse = await http.GetAsync(adresse, arret);
        }

        if (!reponse.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await reponse.Content.ReadAsStringAsync(arret);

        return JsonNode.Parse(json)?["webSocketDebuggerUrl"]?.GetValue<string>();
    }

    /// <summary>Envoie une commande CDP et attend la reponse.</summary>
    public async Task<JsonNode?> SendAsync(string method, object? parameters = null, CancellationToken arret = default)
    {
        if (!Vivant)
        {
            throw new InvalidOperationException(
                "la connexion au navigateur est fermee : relance le verbe, une nouvelle sera ouverte.");
        }

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

        try
        {
            // Le verrou ne tient que l'envoi. C'est tout ce que ClientWebSocket exige, et c'est
            // tout ce qu'il doit tenir : l'englober sur l'attente de la reponse etait l'interblocage.
            await _sendLock.WaitAsync(arret);

            try
            {
                var bytes = Encoding.UTF8.GetBytes(req.ToJsonString());
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, arret);
            }
            finally
            {
                _sendLock.Release();
            }

            return await tcs.Task.WaitAsync(Patience, arret);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"le navigateur n'a pas repondu a {method} en {Patience.TotalSeconds:N0} s.");
        }
        finally
        {
            // Retiree quoi qu'il arrive : une requete abandonnee qui resterait inscrite ferait
            // fuir le dictionnaire une entree par appel expire.
            _pending.TryRemove(id, out _);
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
        // Un seul « result », et c'est la correction.
        //
        // SendAsync rend deja le champ « result » de la reponse CDP ; en redescendre un second
        // menait nulle part, et toute evaluation rendait la chaine vide. « 1+1 » rendait vide, ce
        // qui dit assez que le chemin n'avait jamais servi — masque, comme le reste, par
        // l'interblocage qui empechait la commande d'arriver jusqu'ici.
        if (result?["exceptionDetails"] is { } souci)
        {
            // Rendue plutot qu'avalee : une expression fautive rendait « » comme une page vide, et
            // le modele en concluait que l'element n'existait pas.
            throw new InvalidOperationException(
                "l'expression a leve dans la page : "
                + (souci["exception"]?["description"]?.GetValue<string>() ?? souci.ToJsonString()));
        }

        var value = result?["result"]?["value"];

        if (value is null)
        {
            return "";
        }

        // Une chaine est rendue nue ; le reste garde sa forme JSON, qui est ce qui la decrit.
        return value.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? value.GetValue<string>()
            : value.ToJsonString();
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

                        // Sans verrou : le dictionnaire est concurrent. Le prendre ici etait
                        // l'autre moitie de l'interblocage, puisque SendAsync le tenait en
                        // attendant precisement cette ligne.
                        _pending.TryRemove(id, out var tcs);

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
        finally
        {
            // Personne d'autre ne peut plus repondre.
            //
            // Cette boucle est la seule source de reponses ; quand elle s'arrete, tout ce qui
            // attend attend pour rien. Le laisser expirer sur le delai rendrait vingt secondes de
            // silence par appel, et le journal du 7 septembre montre ce que cela donne : le
            // navigateur avait ferme sa socket — « remote party closed » — et l'assistant a
            // continue d'appeler dans le vide.
            var reste = _pending.Count;

            foreach (var attente in _pending.Values)
            {
                attente.TrySetException(new InvalidOperationException(
                    "la connexion au navigateur s'est fermee avant la reponse."));
            }

            _pending.Clear();

            if (reste > 0)
            {
                await Console.Error.WriteLineAsync(
                    $"CDP: {reste} requete(s) en attente abandonnee(s), la socket est fermee.");
            }
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
