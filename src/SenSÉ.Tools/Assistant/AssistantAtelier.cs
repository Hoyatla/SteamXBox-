using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de pilotage de l'Atelier, branchees sur le subprocess HTTP
/// demarre par <c>SenSÉ.Desktop</c> au boot (cf. <c>ServeurAtelier.Demarrer</c>).
/// </summary>
/// <remarks>
/// <b>Le pont : SenSÉ.Atelier.exe + HTTP loopback.</b> L'Atelier est un
/// subprocess WPF en mode headless, expose sur <c>http://127.0.0.1:8770</c>.
/// Cette classe lui parle via HttpClient. L'URL est fixee par
/// <see cref="Demarrer"/>, et l'env var <c>SENSE_ATELIER_URL</c> est posee
/// par le lanceur pour les clients qui preferent lire la config.
///
/// <para><b>Pas d'auth.</b> L'Atelier n'authentifie pas les requetes venant
/// de 127.0.0.1. Loopback only, comme mcp-saisie et mcp-cdp.</para>
///
/// <para><b>Stade initial.</b> Ce fichier n'expose pour l'instant que
/// <see cref="Demarrer"/>. Les Capacite (lister/creer/executer des graphes)
/// sont ajoutees par la suite (cf. <c>Creer()</c>).</para>
/// </remarks>
public static class AssistantAtelier
{
    private static HttpClient? _http;
    private static string _urlBase = "";

    /// <summary>
    /// Initialise le client HTTP. Appele par <c>ServeurAtelier.Demarrer</c>
    /// apres que l'Atelier a ete lance. Idempotent.
    /// </summary>
    public static void Demarrer(string urlBase, string? token = null)
    {
        if (_http is not null && _urlBase == urlBase) return;
        _urlBase = urlBase.TrimEnd('/');
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }
    }

    /// <summary>
    /// L'URL de base du serveur HTTP de l'Atelier. Vide tant que
    /// <see cref="Demarrer"/> n'a pas ete appele.
    /// </summary>
    public static string UrlBase => _urlBase;
}
