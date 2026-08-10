namespace SteamXBox.Tools.Search;

/// <summary>
/// The institution's own corpus, searched through a Meilisearch instance.
/// </summary>
/// <param name="Enabled">Whether the <c>docs:</c> prefix answers at all.</param>
/// <param name="InstanceUrl">Where the instance is.</param>
/// <param name="Index">Which index to ask. One, by design — see the remarks.</param>
/// <param name="ApiKey">A search key. Never an admin key; see the remarks.</param>
/// <remarks>
/// One index, one key: a single scope. Meilisearch has no per-document permission — its keys grant
/// an index entire — so anything reachable through this is reachable by everybody who can type into
/// the launcher. That is a deliberate limit, not an oversight, and it decides what may be indexed:
/// <b>only what everyone in the institution may already read</b>. Payroll, human resources and legal
/// documents do not belong in it.
///
/// <para>
/// Modelling their real permissions would mean mirroring the file system's rights into filters and
/// keeping the two in step for ever. That is a project of its own, and pretending to it with an
/// approximation is how a search tool shows somebody a document they should not have seen.
/// </para>
/// </remarks>
public sealed record DocsSettings(
    bool Enabled = false,
    string InstanceUrl = "",
    string Index = "documents",
    string ApiKey = "");

/// <summary>What is in force for the corpus, and what an administrator fixed.</summary>
public sealed record ResolvedDocs(DocsSettings Effective, IReadOnlySet<string> Locked)
{
    public bool IsLocked(string field) => Locked.Contains(field);

    /// <summary>Whether it can actually answer.</summary>
    /// <remarks>
    /// Enabled is not enough: an instance without an address answers nothing, and saying so here
    /// keeps every caller from repeating the same three checks.
    /// </remarks>
    public bool IsUsable =>
        Effective.Enabled
        && !string.IsNullOrWhiteSpace(Effective.InstanceUrl)
        && !string.IsNullOrWhiteSpace(Effective.Index);
}

/// <summary>Merges an administrator's policy over the user's corpus settings.</summary>
/// <remarks>
/// The same rule as <see cref="WebSearchPolicy"/>: a field is locked by being present. For a company
/// with sensitive data this matters more than it does for the web side — the address and the key are
/// exactly what must not be repointed at something else.
/// </remarks>
public static class DocsPolicy
{
    public const string EnabledField = "enabled";
    public const string InstanceUrlField = "instanceUrl";
    public const string IndexField = "index";
    public const string ApiKeyField = "apiKey";

    public static ResolvedDocs Resolve(DocsSettings? user, PolicyValues? policy)
    {
        var effective = user ?? new DocsSettings();
        var locked = new HashSet<string>(StringComparer.Ordinal);

        if (policy is null)
        {
            return new ResolvedDocs(effective, locked);
        }

        if (policy.Enabled is { } enabled)
        {
            effective = effective with { Enabled = enabled };
            locked.Add(EnabledField);
        }

        if (policy.InstanceUrl is { } url)
        {
            effective = effective with { InstanceUrl = url };
            locked.Add(InstanceUrlField);
        }

        if (policy.Index is { } index)
        {
            effective = effective with { Index = index };
            locked.Add(IndexField);
        }

        if (policy.ApiKey is { } key)
        {
            effective = effective with { ApiKey = key };
            locked.Add(ApiKeyField);
        }

        return new ResolvedDocs(effective, locked);
    }

    /// <summary>A policy section where every field is optional.</summary>
    public sealed class PolicyValues
    {
        public bool? Enabled { get; set; }

        public string? InstanceUrl { get; set; }

        public string? Index { get; set; }

        /// <summary>
        /// A <b>search</b> key, never an administration key.
        /// </summary>
        /// <remarks>
        /// Whatever is written here reaches every machine the policy is deployed to, and a policy
        /// file is readable by anyone who can read the disk. A Meilisearch search key can only
        /// search; an admin key can rewrite and delete the index. The distinction is the difference
        /// between a leaked key being an annoyance and being an incident.
        /// </remarks>
        public string? ApiKey { get; set; }
    }
}
