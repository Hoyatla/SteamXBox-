using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Search;

/// <summary>Everything the launcher's search sides need, once policy has been applied.</summary>
public sealed record SearchConfiguration(ResolvedWebSearch Web, ResolvedDocs Docs);

/// <summary>
/// Reads the search settings: the user's, and the administrator's policy over them.
/// </summary>
/// <remarks>
/// Two files in two places, and the places are the whole mechanism.
///
/// <list type="bullet">
/// <item>
/// <c>%ProgramData%\SenSÉ\policy.json</c> — the administrator's. That directory is writable only
/// by administrators on a default Windows installation, which is what makes a lock a lock. No code
/// here enforces anything: a lock a user could rewrite would be decoration.
/// </item>
/// <item>
/// <c>%LOCALAPPDATA%\SenSÉ\search.json</c> — the user's own, alongside their profiles.
/// </item>
/// </list>
///
/// <para>
/// One store for both the web side and the corpus side, and one policy file with a section each.
/// Two stores would have drifted — this project has spent enough nights undoing pairs of mechanisms
/// that were supposed to stay in step — and an institution deploys one file, once, gaining sections
/// as the product does.
/// </para>
/// </remarks>
public static class SearchPolicyStore
{
    private static string PolicyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SenSÉ",
        "policy.json");

    private static string UserPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ",
        "search.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What is in force, and what the administrator has fixed.</summary>
    public static SearchConfiguration Load(Action<string>? log = null)
    {
        var user = Read<UserFile>(UserPath, log, "user search settings");
        var policy = Read<PolicyFile>(PolicyPath, log, "administrator policy");

        var web = WebSearchPolicy.Resolve(user?.WebSearch, policy?.WebSearch);
        var docs = DocsPolicy.Resolve(user?.Docs, policy?.Docs);

        log?.Invoke($"web search: {web.Effective.Provider}"
                    + (web.Locked.Count > 0 ? $", fixed: {string.Join(", ", web.Locked)}" : ", free"));

        log?.Invoke(docs.IsUsable
            ? $"docs search: {docs.Effective.Index} on {docs.Effective.InstanceUrl}"
              + (docs.Locked.Count > 0 ? $", fixed: {string.Join(", ", docs.Locked)}" : ", free")
            : "docs search: not configured.");

        return new SearchConfiguration(web, docs);
    }

    /// <summary>
    /// Writes the user's own settings. What the policy fixes is written too and simply ignored.
    /// </summary>
    /// <remarks>
    /// Deliberately not filtered against the policy before writing. A policy that is later withdrawn
    /// should give the user back the choices they had, and stripping their file on every save would
    /// have quietly erased them in the meantime.
    /// </remarks>
    public static void SaveUser(WebSearchSettings web, DocsSettings docs, Action<string>? log = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);

            File.WriteAllText(
                UserPath,
                JsonSerializer.Serialize(new UserFile { WebSearch = web, Docs = docs }, Json));
        }
        catch (Exception exception)
        {
            log?.Invoke($"search settings not saved: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static T? Read<T>(string path, Action<string>? log, string what)
        where T : class
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                : null;
        }
        catch (Exception exception)
        {
            // A malformed policy is the dangerous case: ignoring it in silence would hand a locked
            // deployment back to its users. Said loudly, and treated as absent — which is visible in
            // the interface, because everything becomes editable again.
            log?.Invoke($"{what} unreadable at {path}: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private sealed class UserFile
    {
        public WebSearchSettings? WebSearch { get; set; }

        public DocsSettings? Docs { get; set; }
    }

    private sealed class PolicyFile
    {
        public WebSearchPolicy.PolicyValues? WebSearch { get; set; }

        public DocsPolicy.PolicyValues? Docs { get; set; }
    }
}
