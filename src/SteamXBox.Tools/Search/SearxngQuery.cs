namespace SteamXBox.Tools.Search;

/// <summary>
/// Builds the address that asks a SearXNG instance a question.
/// </summary>
/// <remarks>
/// One instance, chosen by the institution and usually on its own network. The query goes to a
/// server they run, behind their own filtering, and never to a third party — which is the whole
/// reason this exists for schools and for companies handling sensitive data.
///
/// <para>
/// The safe-search level travels in the address, and the instance is expected to force it in its own
/// configuration as well. Belt and braces on purpose: what is in an address can be edited by anyone
/// who sees it, so it is a courtesy to the instance rather than a control. The control is the
/// instance's own setting, which is why the deployment notes must say so.
/// </para>
/// </remarks>
public static class SearxngQuery
{
    /// <summary>The search address on <paramref name="instance"/>, or empty if it is unusable.</summary>
    /// <remarks>
    /// Only <c>http</c> and <c>https</c>, and nothing that carries a path of its own: a policy value
    /// is typed by a person, and an address with a scheme of another kind is a way to launch
    /// something rather than to search.
    /// </remarks>
    public static string Url(string? instance, string keywords, SafeSearch safeSearch)
    {
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(keywords))
        {
            return "";
        }

        if (!Uri.TryCreate(instance.Trim(), UriKind.Absolute, out var root)
            || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            return "";
        }

        var baseUrl = root.GetLeftPart(UriPartial.Authority).TrimEnd('/');

        return $"{baseUrl}/search?q={Uri.EscapeDataString(keywords)}&safesearch={Level(safeSearch)}";
    }

    /// <summary>The same query, asking for results as data rather than as a page.</summary>
    /// <remarks>
    /// A deployment note that belongs next to this line: <b>SearXNG does not serve JSON by
    /// default</b>. Its <c>settings.yml</c> must list <c>json</c> under <c>search.formats</c>, or the
    /// instance answers with an error and the launcher can show nothing. It is one line for whoever
    /// runs the instance, and it is the single most likely reason for "the web results do not work"
    /// on a fresh install.
    /// </remarks>
    public static string JsonUrl(string? instance, string keywords, SafeSearch safeSearch)
    {
        var url = Url(instance, keywords, safeSearch);

        return url.Length == 0 ? "" : url + "&format=json";
    }

    /// <summary>SearXNG's own numbering: 0 none, 1 moderate, 2 strict.</summary>
    private static int Level(SafeSearch safeSearch) => safeSearch switch
    {
        SafeSearch.Off => 0,
        SafeSearch.Strict => 2,
        _ => 1,
    };
}
