namespace SenSÉ.Tools.Search;

/// <summary>Where a <c>web:</c> query goes.</summary>
public enum WebSearchProvider
{
    /// <summary>The user's own browser, at the configured engine. No dependency, no instance.</summary>
    Browser,

    /// <summary>A SearXNG instance, with the results shown inside the launcher.</summary>
    Instance,

    /// <summary>Nowhere. The prefix is refused and says so.</summary>
    Disabled,
}

/// <summary>How much the instance filters.</summary>
public enum SafeSearch
{
    Off,
    Moderate,
    Strict,
}

/// <summary>What the web side of the launcher does.</summary>
/// <param name="Provider">Where queries go.</param>
/// <param name="InstanceUrl">The SearXNG instance, when <see cref="WebSearchProvider.Instance"/>.</param>
/// <param name="SafeSearch">The filtering level asked of the instance.</param>
public sealed record WebSearchSettings(
    WebSearchProvider Provider = WebSearchProvider.Browser,
    string InstanceUrl = "",
    SafeSearch SafeSearch = SafeSearch.Moderate);

/// <summary>
/// What an administrator has fixed, and what the user may still change.
/// </summary>
/// <param name="Effective">The settings actually in force.</param>
/// <param name="Locked">The names of the fields an administrator has fixed.</param>
public sealed record ResolvedWebSearch(WebSearchSettings Effective, IReadOnlySet<string> Locked)
{
    public bool IsLocked(string field) => Locked.Contains(field);
}

/// <summary>
/// Merges an administrator's policy over the user's own settings.
/// </summary>
/// <remarks>
/// Built before there is anything much to lock, and deliberately. A secondary school cannot deploy a
/// search tool whose safe-search a pupil can switch off, and a company with sensitive data cannot
/// deploy one whose instance address a user can repoint at the open internet. Retrofitting that
/// means revisiting every setting ever added; having it from the first setting costs nothing.
///
/// <para>
/// A field is locked by being <i>present</i> in the policy. No separate flag saying so — two sources
/// of truth about whether something is locked would eventually disagree, and the one that lies would
/// be the one shown to the user.
/// </para>
///
/// <para>
/// The real enforcement is not in this code and cannot be: it is the file's location. The policy
/// belongs somewhere only an administrator can write, and the user's settings somewhere they can.
/// Any lock a user could rewrite is decoration.
/// </para>
/// </remarks>
public static class WebSearchPolicy
{
    public const string ProviderField = "provider";
    public const string InstanceUrlField = "instanceUrl";
    public const string SafeSearchField = "safeSearch";

    /// <summary>
    /// The settings in force, given what the user chose and what the administrator fixed.
    /// </summary>
    /// <param name="user">The user's own choices. Null is the defaults.</param>
    /// <param name="policy">
    /// What the administrator fixed. Null when there is no policy at all — the ordinary case of
    /// somebody running SenSÉ on their own machine, where nothing is locked.
    /// </param>
    public static ResolvedWebSearch Resolve(WebSearchSettings? user, PolicyValues? policy)
    {
        var effective = user ?? new WebSearchSettings();
        var locked = new HashSet<string>(StringComparer.Ordinal);

        if (policy is null)
        {
            return new ResolvedWebSearch(effective, locked);
        }

        if (policy.Provider is { } provider)
        {
            effective = effective with { Provider = provider };
            locked.Add(ProviderField);
        }

        if (policy.InstanceUrl is { } url)
        {
            effective = effective with { InstanceUrl = url };
            locked.Add(InstanceUrlField);
        }

        if (policy.SafeSearch is { } safeSearch)
        {
            effective = effective with { SafeSearch = safeSearch };
            locked.Add(SafeSearchField);
        }

        // An instance provider without an instance is a launcher that cannot answer. Falling back to
        // the browser rather than to nothing keeps the feature usable while an administrator is
        // still writing the policy, and says so through the resolved settings rather than failing at
        // the moment somebody types a query.
        if (effective.Provider == WebSearchProvider.Instance
            && string.IsNullOrWhiteSpace(effective.InstanceUrl))
        {
            effective = effective with { Provider = WebSearchProvider.Browser };
        }

        return new ResolvedWebSearch(effective, locked);
    }

    /// <summary>
    /// A policy file, where every field is optional.
    /// </summary>
    /// <remarks>
    /// Nullable throughout, because absence is what says "the user decides". An administrator who
    /// only wants to force the instance address writes one line and leaves the rest alone.
    /// </remarks>
    public sealed class PolicyValues
    {
        public WebSearchProvider? Provider { get; set; }

        public string? InstanceUrl { get; set; }

        public SafeSearch? SafeSearch { get; set; }
    }
}
