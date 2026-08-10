using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>What the index answered, or why it did not.</summary>
public readonly record struct DocsAnswer(IReadOnlyList<DocsResult> Results, string Problem);

/// <summary>
/// Asks a Meilisearch index, and says plainly when it will not answer.
/// </summary>
/// <remarks>
/// One client for the life of the process, as for the web side: a new <see cref="HttpClient"/> per
/// query exhausts the machine's sockets under repeated use.
/// </remarks>
public static class MeilisearchClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Client = new()
    {
        Timeout = Timeout,
        DefaultRequestHeaders = { { "User-Agent", "SteamXBox" } },
    };

    /// <summary>Searches the corpus.</summary>
    public static async Task<DocsAnswer> AskAsync(
        DocsSettings settings, string keywords, CancellationToken cancellation)
    {
        var url = MeilisearchQuery.SearchUrl(settings);

        if (url.Length == 0)
        {
            return new DocsAnswer([], "L'adresse ou l'index du corpus n'est pas valide.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    MeilisearchQuery.SearchBody(keywords), Encoding.UTF8, "application/json"),
            };

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
            }

            using var response = await Client.SendAsync(request, cancellation).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // The two that actually happen on a deployment, named so whoever reads this knows
                // which door to knock on.
                return new DocsAnswer([], (int)response.StatusCode switch
                {
                    401 or 403 => "Le corpus refuse la clé. Vérifiez qu'il s'agit d'une clé de recherche.",
                    404 => $"L'index « {settings.Index} » n'existe pas sur cette instance.",
                    var code => $"Le corpus a répondu {code}.",
                });
            }

            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);

            return new DocsAnswer(MeilisearchQuery.Parse(body), "");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new DocsAnswer([], $"Le corpus n'a pas répondu en {Timeout.TotalSeconds:0} secondes.");
        }
        catch (OperationCanceledException)
        {
            // The user typed on. Not a failure, and nothing to show.
            return new DocsAnswer([], "");
        }
        catch (HttpRequestException exception)
        {
            return new DocsAnswer([], $"Corpus injoignable : {exception.Message}");
        }
    }
}
