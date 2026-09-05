using System.Text.Json;

namespace SenSÉ.Tools.Search;

/// <summary>One document the instance returned.</summary>
/// <param name="Title">What to show.</param>
/// <param name="Path">Where it is: a file path, or an address.</param>
/// <param name="Snippet">A line of the content, or empty.</param>
public readonly record struct DocsResult(string Title, string Path, string Snippet);

/// <summary>
/// Builds a search against a Meilisearch index, and reads what comes back.
/// </summary>
/// <remarks>
/// The document shape is a contract, not a guess:
///
/// <code>
/// { "id": "…", "title": "Compte rendu mars", "path": "\\\\serveur\\docs\\cr-mars.pdf", "content": "…" }
/// </code>
///
/// <para>
/// Whatever fills the index must produce that. Reading a known shape can be tested and explained;
/// guessing across whatever fields an institution happened to use is how a search tool shows blank
/// rows on one deployment and works on another. The reader is tolerant about which field holds the
/// name — <c>title</c>, then <c>name</c>, then the file name of the path — because that one varies
/// for good reasons, and about nothing else.
/// </para>
/// </remarks>
public static class MeilisearchQuery
{
    /// <summary>Never more than this, however many the index holds.</summary>
    public const int Max = 25;

    /// <summary>The address to POST a search to, or empty when the settings cannot make one.</summary>
    public static string SearchUrl(DocsSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.InstanceUrl) || string.IsNullOrWhiteSpace(settings.Index))
        {
            return "";
        }

        if (!Uri.TryCreate(settings.InstanceUrl.Trim(), UriKind.Absolute, out var root)
            || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            return "";
        }

        var baseUrl = root.GetLeftPart(UriPartial.Authority).TrimEnd('/');

        return $"{baseUrl}/indexes/{Uri.EscapeDataString(settings.Index.Trim())}/search";
    }

    /// <summary>The request body.</summary>
    /// <remarks>
    /// Serialised rather than assembled by hand: a query is user input, and a document that
    /// concatenates it would break on the first quotation mark somebody types.
    /// </remarks>
    public static string SearchBody(string keywords)
        => JsonSerializer.Serialize(new { q = keywords, limit = Max });

    /// <summary>The usable documents in a response.</summary>
    public static IReadOnlyList<DocsResult> Parse(string? json)
    {
        var results = new List<DocsResult>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("hits", out var hits)
                || hits.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var hit in hits.EnumerateArray())
            {
                // Capped here as well as asked for in the request. The limit in the body is a
                // request; this is the guarantee. An instance that ignores it — misconfigured, or an
                // older version — would otherwise put thousands of rows in a list nobody can read.
                if (results.Count >= Max)
                {
                    break;
                }

                var path = Text(hit, "path");

                if (path.Length == 0)
                {
                    // Nothing to open. A row that cannot be acted on is worse than a shorter list.
                    continue;
                }

                var title = Text(hit, "title");

                if (title.Length == 0)
                {
                    title = Text(hit, "name");
                }

                if (title.Length == 0)
                {
                    title = System.IO.Path.GetFileName(path);
                }

                results.Add(new DocsResult(
                    title.Length > 0 ? title : path,
                    path,
                    Snippet(Text(hit, "content"))));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return results;
    }

    /// <summary>The first line of the content, short enough for a row.</summary>
    /// <remarks>
    /// Cut here rather than asked of the instance. Meilisearch can return highlighted extracts, but
    /// that needs the index to have been configured for it — one more thing to get right on a
    /// deployment, for a line of grey text.
    /// </remarks>
    private static string Snippet(string content)
    {
        if (content.Length == 0)
        {
            return "";
        }

        var flattened = content.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return flattened.Length <= 160 ? flattened : flattened[..160] + "…";
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
