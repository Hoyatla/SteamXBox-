using System.Net.Http;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>What the instance answered, or why it did not.</summary>
/// <param name="Results">The usable results, possibly none.</param>
/// <param name="Problem">A line to show the user, or empty when all went well.</param>
public readonly record struct SearxngAnswer(IReadOnlyList<SearxngResult> Results, string Problem);

/// <summary>
/// Asks a SearXNG instance, and says plainly when it will not answer.
/// </summary>
/// <remarks>
/// One client for the life of the process. A new <see cref="HttpClient"/> per query exhausts the
/// machine's sockets under repeated use — the classic way this goes wrong, and a launcher is used
/// dozens of times an hour.
///
/// <para>
/// Every failure becomes a sentence rather than an exception. The instance is a server on an
/// institution's network: it can be down, misconfigured, behind a proxy that refuses, or serving
/// HTML because JSON was never enabled. The user needs to know which, and so does whoever they will
/// ask about it.
/// </para>
/// </remarks>
public static class SearxngClient
{
    /// <summary>
    /// Long enough for a slow instance, short enough that nobody wonders whether it is broken.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Client = new()
    {
        Timeout = Timeout,

        // Named honestly. Some instances refuse a request with no user agent, and an institution
        // reading its own logs should be able to see what its users are running.
        DefaultRequestHeaders = { { "User-Agent", "SteamXBox" } },
    };

    /// <summary>Queries the instance.</summary>
    public static async Task<SearxngAnswer> AskAsync(
        WebSearchSettings settings, string keywords, CancellationToken cancellation)
    {
        var url = SearxngQuery.JsonUrl(settings.InstanceUrl, keywords, settings.SafeSearch);

        if (url.Length == 0)
        {
            return new SearxngAnswer([], "L'adresse de l'instance de recherche n'est pas valide.");
        }

        try
        {
            using var response = await Client.GetAsync(url, cancellation).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 403 is the one worth naming: it is what an instance answers when the JSON format
                // is not enabled, and it is the likeliest fault on a fresh deployment.
                return new SearxngAnswer([], (int)response.StatusCode == 403
                    ? "L'instance refuse le format JSON. À activer dans son settings.yml."
                    : $"L'instance a répondu {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            var results = SearxngResults.Parse(body);

            // Parsed nothing out of a successful answer means the body was not what was asked for —
            // an HTML page, almost always, from an instance serving only that format.
            if (results.Count == 0 && !body.TrimStart().StartsWith('{'))
            {
                return new SearxngAnswer([], "L'instance a répondu en HTML. Le format JSON n'est pas activé.");
            }

            return new SearxngAnswer(results, "");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new SearxngAnswer([], $"L'instance n'a pas répondu en {Timeout.TotalSeconds:0} secondes.");
        }
        catch (OperationCanceledException)
        {
            // The user typed on. Not a failure, and nothing to show.
            return new SearxngAnswer([], "");
        }
        catch (HttpRequestException exception)
        {
            return new SearxngAnswer([], $"Instance injoignable : {exception.Message}");
        }
    }
}
