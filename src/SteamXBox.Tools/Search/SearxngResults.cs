using System.Text.Json;

namespace SteamXBox.Tools.Search;

/// <summary>One result from the instance.</summary>
/// <param name="Title">What to show.</param>
/// <param name="Url">Where it goes.</param>
/// <param name="Snippet">The instance's own summary, or empty.</param>
public readonly record struct SearxngResult(string Title, string Url, string Snippet);

/// <summary>
/// Reads what a SearXNG instance answered.
/// </summary>
/// <remarks>
/// Parsed by hand from the document rather than deserialised into a mirror of SearXNG's schema. Only
/// three fields are wanted out of a response that carries a dozen, some of which differ between
/// versions and between the engines an instance aggregates. A model of the whole thing would break
/// on a version the institution happens to run; reading three fields does not.
///
/// <para>
/// Nothing here trusts the instance. An address that is not <c>http</c> is dropped, a result with no
/// address is dropped, and a malformed document gives an empty list rather than an exception — an
/// instance is a server on somebody else's network, and it can answer anything at all.
/// </para>
/// </remarks>
public static class SearxngResults
{
    /// <summary>Never more than this, however many the instance sent.</summary>
    public const int Max = 25;

    /// <summary>The usable results in a response, in the order the instance ranked them.</summary>
    public static IReadOnlyList<SearxngResult> Parse(string? json)
    {
        var results = new List<SearxngResult>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var element in array.EnumerateArray())
            {
                if (results.Count >= Max)
                {
                    break;
                }

                var url = Text(element, "url");

                // Only what a browser should be sent to. An instance is a server on somebody else's
                // network; a result carrying a file: or javascript: address is not a search result.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                    || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }

                var title = Text(element, "title");

                results.Add(new SearxngResult(
                    title.Length > 0 ? title : parsed.Host,
                    parsed.AbsoluteUri,
                    Text(element, "content")));
            }
        }
        catch (JsonException)
        {
            // An instance that answers with an error page, or with HTML because JSON was never
            // enabled in its settings. Empty, and the caller says so.
            return [];
        }

        return results;
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
