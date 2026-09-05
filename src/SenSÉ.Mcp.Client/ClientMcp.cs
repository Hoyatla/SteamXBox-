using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Mcp.Bus;

namespace SenSÉ.Mcp.Client;

/// <summary>
/// Un serveur MCP en cours d'execution : son process, ses flux stdio,
/// son verrou d'envoi. Parle JSON-RPC 2.0 newline-delimited.
/// </summary>
/// <remarks>
/// <b>Stdio, pas HTTP.</b> Un serveur par sous-processus. Le client
/// envoie une requete par ligne sur stdin, recoit une reponse par
/// ligne sur stdout. Le lock evite d'entrelacer deux requetes.
///
/// <para><b>Pas de port, pas de pare-feu.</b> La latence est minimale
/// (~1ms) et il n'y a rien a configurer cote Windows.</para>
/// </remarks>
public sealed class ClientMcp : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _entree;
    private readonly StreamReader _sortie;
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private readonly string _nom;

    private long _compteurId;
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _enAttente = new();
    private readonly CancellationTokenSource _lecture = new();

    public string Nom => _nom;
    public int Pid => _process.Id;

    private ClientMcp(Process process, StreamWriter entree, StreamReader sortie, string nom)
    {
        _process = process;
        _entree = entree;
        _sortie = sortie;
        _nom = nom;
        _ = BouclerReponsesAsync(_lecture.Token);
    }

    /// <summary>Démarre un serveur MCP par son chemin d'executable.</summary>
    public static async Task<ClientMcp> DemarrerAsync(string chemin, string nom, CancellationToken arret = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = chemin,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        var p = Process.Start(psi) ?? throw new InvalidOperationException($"impossible de demarrer {chemin}");
        var entree = new StreamWriter(p.StandardInput.BaseStream) { AutoFlush = true };
        var sortie = new StreamReader(p.StandardOutput.BaseStream);
        return new ClientMcp(p, entree, sortie, nom);
    }

    /// <summary>Envoie une requete JSON-RPC arbitraire et attend la reponse.</summary>
    public async Task<JsonNode?> AppelerAsync(string methode, JsonObject? parametres, CancellationToken arret = default)
    {
        await _verrou.WaitAsync(arret).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _compteurId).ToString();
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _enAttente[id] = tcs;

            var requete = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = methode,
                ["params"] = parametres,
            };
            await _entree.WriteLineAsync(requete.ToJsonString()).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(arret);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _verrou.Release();
        }
    }

    private async Task BouclerReponsesAsync(CancellationToken arret)
    {
        try
        {
            while (!arret.IsCancellationRequested)
            {
                var ligne = await _sortie.ReadLineAsync(arret).ConfigureAwait(false);
                if (ligne is null) break;
                if (string.IsNullOrWhiteSpace(ligne)) continue;

                try
                {
                    var noeud = JsonNode.Parse(ligne);
                    if (noeud is null) continue;

                    var id = noeud["id"]?.GetValue<string>();
                    if (id is not null && _enAttente.Remove(id, out var tcs))
                    {
                        if (noeud["error"] is JsonNode err)
                        {
                            tcs.TrySetException(new InvalidOperationException(err["message"]?.GetValue<string>() ?? "erreur MCP"));
                        }
                        else
                        {
                            tcs.TrySetResult(noeud["result"]);
                        }
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            foreach (var tcs in _enAttente.Values) tcs.TrySetException(ex);
            _enAttente.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lecture.Cancel();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _process.Dispose();
        _verrou.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Multiplexeur : garde une collection de ClientMcp et expose leurs
/// outils comme une liste unifiee pour le modele.
/// </summary>
public sealed class MultiplexeurMcp : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ClientMcp> _serveurs = new();
    private readonly ConcurrentDictionary<string, (string Serveur, JsonObject Outil)> _outils = new();

    public IReadOnlyDictionary<string, JsonObject> Outils => (IReadOnlyDictionary<string, JsonObject>)_outils;
    public IReadOnlyCollection<string> Serveurs => (IReadOnlyCollection<string>)_serveurs.Keys;

    public MultiplexeurMcp() { }

    /// <summary>Ajoute un serveur, recupere ses outils, les enregistre dans la liste unifiee.</summary>
    public async Task AjouterAsync(string nom, string chemin, CancellationToken arret = default)
    {
        var client = await ClientMcp.DemarrerAsync(chemin, nom, arret).ConfigureAwait(false);
        _serveurs[nom] = client;

        var init = await client.AppelerAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "SenSÉ.Assistant",
                ["version"] = "1.0.0",
            },
        }, arret).ConfigureAwait(false);

        var liste = await client.AppelerAsync("tools/list", null, arret).ConfigureAwait(false);
        if (liste is JsonObject obj && obj["tools"] is JsonArray tab)
        {
            foreach (var noeud in tab)
            {
                if (noeud is JsonObject outil)
                {
                    var nomOutil = outil["name"]?.GetValue<string>();
                    if (nomOutil is not null)
                    {
                        _outils[nomOutil] = (nom, outil);
                    }
                }
            }
        }
    }

    /// <summary>Appelle un outil par son nom complet (par ex. "saisie.souris_deplacer").</summary>
    public async Task<string> AppelerAsync(string nomOutil, JsonObject? arguments, CancellationToken arret = default)
    {
        if (!_outils.TryGetValue(nomOutil, out var info))
        {
            return $"outil inconnu: {nomOutil}";
        }
        if (!_serveurs.TryGetValue(info.Serveur, out var client))
        {
            return $"serveur indisponible: {info.Serveur}";
        }
        try
        {
            var result = await client.AppelerAsync("tools/call", new JsonObject
            {
                ["name"] = nomOutil,
                ["arguments"] = arguments ?? new JsonObject(),
            }, arret).ConfigureAwait(false);
            if (result is JsonObject r && r["content"] is JsonArray contenu && contenu.Count > 0)
            {
                return contenu[0]?["text"]?.GetValue<string>() ?? "";
            }
            return result?.ToJsonString() ?? "";
        }
        catch (Exception ex)
        {
            return $"erreur: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _serveurs.Values)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        _serveurs.Clear();
        _outils.Clear();
    }
}