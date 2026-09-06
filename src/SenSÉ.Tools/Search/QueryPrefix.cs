namespace SenSÉ.Tools.Search;

/// <summary>What a prefix diverts the query to.</summary>
public enum QueryRoute
{
    /// <summary>No prefix: the machine's own index.</summary>
    Local,

    /// <summary>The web, through the user's browser.</summary>
    Web,

    /// <summary>The institution's own corpus, through its Meilisearch index.</summary>
    Docs,

    /// <summary>
    /// This person's own mail, from an index held on this machine.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="Docs"/>, and the separation is a privacy decision rather
    /// than a tidiness one. The institution's corpus lives in a Meilisearch index, which has no
    /// per-document permissions: a mailbox put in there is readable by everyone who can query it.
    /// Mail therefore never leaves the machine it belongs to, and has its own route so that it
    /// cannot be pointed at a shared instance by a change of setting.
    /// </remarks>
    Mail,
}

/// <summary>A query, once its prefix has been read.</summary>
/// <param name="Route">Where it goes.</param>
/// <param name="Rest">What is left after the prefix, trimmed.</param>
public readonly record struct RoutedQuery(QueryRoute Route, string Rest);

/// <summary>
/// Reads the prefix that sends a query somewhere other than the local index.
/// </summary>
/// <remarks>
/// A mechanism rather than a special case, and that distinction is the point of writing it this way.
/// <c>web:</c> is the first, and it would have been half a line to test for it inside the filtering
/// code — after which <c>calc:</c> and the next one would each be another half line in the same
/// place, until the filter is a list of exceptions nobody dares reorder.
///
/// <para>
/// Here a prefix is a name in one table. What it does is decided by the caller, so adding one costs
/// an entry and touches nothing that already works — which is also the seam a declarative tool will
/// need if one is ever allowed to answer a query.
/// </para>
/// </remarks>
public static class QueryPrefix
{
    /// <summary>The recognised prefixes. One line each, by design.</summary>
    private static readonly IReadOnlyDictionary<string, QueryRoute> Known =
        new Dictionary<string, QueryRoute>(StringComparer.OrdinalIgnoreCase)
        {
            ["web"] = QueryRoute.Web,
            ["docs"] = QueryRoute.Docs,
            ["mail"] = QueryRoute.Mail,

            // Les deux orthographes françaises du même mot. Le bouton de la barre pose
            // « e-mail: », et quelqu'un qui tape le préfixe à la main écrira l'un ou l'autre
            // sans se demander lequel SenSÉ attend — c'est une entrée chacun, exactement ce que
            // cette table est faite pour absorber.
            ["e-mail"] = QueryRoute.Mail,
            ["email"] = QueryRoute.Mail,
        };

    /// <summary>Reads a raw query and says where it goes.</summary>
    /// <remarks>
    /// A colon alone does not make a prefix: <c>D:\Projets</c> and <c>C:</c> are paths people type
    /// constantly, and reading them as an unknown prefix would break the most ordinary search there
    /// is. Only names in the table divert anything; everything else stays local, colon and all.
    /// </remarks>
    public static RoutedQuery Parse(string? raw)
    {
        var text = (raw ?? "").TrimStart();

        var colon = text.IndexOf(':');

        if (colon > 0
            && Known.TryGetValue(text[..colon], out var route))
        {
            return new RoutedQuery(route, text[(colon + 1)..].Trim());
        }

        return new RoutedQuery(QueryRoute.Local, text);
    }

    /// <summary>
    /// The address that searches the web for <paramref name="keywords"/>.
    /// </summary>
    /// <remarks>
    /// An address opened in the user's own browser, not a search performed here. That keeps SenSÉ
    /// out of the business of talking to a search engine — no key, no quota, no request leaving on
    /// its own, and nothing to bundle. It also sidesteps a licence problem worth naming: SearXNG,
    /// the obvious candidate for results shown inside the window, is AGPL, whose network clause
    /// binds whoever runs the instance. Querying an instance the user names is fine; shipping one
    /// would not be.
    ///
    /// <para>
    /// DuckDuckGo by default because it needs no account and does not require a key. The engine is a
    /// constant here rather than buried in the caller, so making it a setting later changes one
    /// line.
    /// </para>
    /// </remarks>
    public static string WebSearchUrl(string keywords)
        => "https://duckduckgo.com/?q=" + Uri.EscapeDataString(keywords);
}
