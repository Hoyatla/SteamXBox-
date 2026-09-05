using System.Text.RegularExpressions;

namespace SenSÉ.Tools.Search;

/// <summary>Whether a query is looking for a path or for a name.</summary>
/// <remarks>
/// The two want opposite things. Typing <c>doc</c> means "the thing called doc"; typing
/// <c>C:\Users\</c> means "show me what is there", and ranking the second by name would bury the
/// folder actually asked for under everything whose name happens to contain the letters.
/// </remarks>
public static class PathQuery
{
    private static readonly Regex DriveLetter = new(@"^[a-zA-Z]:[\\/]", RegexOptions.Compiled);

    public static bool LooksLikeAPath(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && (DriveLetter.IsMatch(text)
               || text.StartsWith(@"\\", StringComparison.Ordinal)
               || text.StartsWith("~", StringComparison.Ordinal)
               || text.StartsWith(@".\", StringComparison.Ordinal)
               || text.StartsWith("../", StringComparison.Ordinal)
               || text.Contains('\\')
               || text.Contains('/'));

    /// <summary>Resolves <c>~</c> and environment variables, leaving the rest untouched.</summary>
    public static string Expand(string? text, string? userProfile = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        if (text.StartsWith("~", StringComparison.Ordinal))
        {
            var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return System.IO.Path.Combine(home, text[1..].TrimStart('\\', '/'));
        }

        return Environment.ExpandEnvironmentVariables(text);
    }
}

/// <summary>
/// How well an entry answers a query, or nothing when it does not answer it at all.
/// </summary>
/// <remarks>
/// Ported from the PowerShell version's <c>Score-Item</c> with its weights intact. They were arrived
/// at by use, and changing them while changing the language would make any difference in behaviour
/// impossible to attribute to one or the other.
/// </remarks>
public static class SearchRanking
{
    /// <summary>Names that are almost always what was meant when typed in full.</summary>
    private static readonly Regex CommonApplications = new(
        "^(chrome|edge|firefox|word|excel|powerpoint|outlook|notepad|code|terminal|powershell|explorer|spotify|discord|teams|steam)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits a query into the terms every result must satisfy.
    /// </summary>
    /// <remarks>
    /// Typing a whole path to find something is absurd once the machine already knows where things
    /// are: <c>D, SenSÉ</c> should find
    /// <c>D:\…\SenSÉ-portable-win-x64\SenSÉ.exe</c> without anyone spelling out the middle.
    ///
    /// <para>
    /// Comma and space both separate, because both are what people type. A term that is a bare drive
    /// letter keeps its colon so it matches the path and not every word containing that letter.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Terms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Length == 1 && char.IsLetter(term[0]) ? term + ":" : term)
            .Select(SearchText.Normalize)
            .Where(term => term.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The score for a query of several terms, or null unless every one of them matches.
    /// </summary>
    /// <remarks>
    /// All terms required, never any. Two terms are how somebody narrows a search; treating them as
    /// alternatives would widen it instead, and the second word would make the answer worse.
    ///
    /// <para>
    /// Each term is scored against the whole entry, name and path together, and the best of the two
    /// is kept — <c>D:</c> matches the path while <c>SenSÉ</c> matches the name, and neither
    /// should be penalised for not being the other. The result is the sum, so an entry that answers
    /// both well outranks one that scrapes past on each.
    /// </para>
    /// </remarks>
    public static int? Score(SearchItem item, IReadOnlyList<string> terms, bool pathMode, DateTime now)
    {
        if (terms.Count == 0)
        {
            return null;
        }

        if (terms.Count == 1)
        {
            return Score(item, terms[0], pathMode, now);
        }

        var total = 0;

        foreach (var term in terms)
        {
            // Both readings, because a multi-term query mixes them: one term is a place and another
            // is a name. Refusing the entry unless every term matches the same way would answer
            // nothing.
            var asName = Score(item, term, pathMode: false, now);
            var asPath = Score(item, term, pathMode: true, now);

            if (asName is null && asPath is null)
            {
                return null;
            }

            total += Math.Max(asName ?? int.MinValue, asPath ?? int.MinValue);
        }

        // Averaged, so a two-term query is comparable with a one-term query rather than scoring
        // twice as high for the same entry.
        return total / terms.Count;
    }

    /// <summary>The score, or null when the entry does not match and must not be shown.</summary>
    /// <param name="item">The candidate.</param>
    /// <param name="query">The query, already normalised.</param>
    /// <param name="pathMode">From <see cref="PathQuery.LooksLikeAPath"/>.</param>
    /// <param name="now">Passed in rather than read, so the recency bonus is testable.</param>
    public static int? Score(SearchItem item, string query, bool pathMode, DateTime now)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var name = item.SearchName;
        var path = item.SearchPath;
        var score = item.Priority;

        if (pathMode)
        {
            if (path == query) score += 1200;
            else if (path.StartsWith(query, StringComparison.Ordinal)) score += 950;
            else if (path.Contains(query, StringComparison.Ordinal)) score += 650;
            else if (name.StartsWith(LastSegment(query), StringComparison.Ordinal)) score += 250;
            else return null;
        }
        else
        {
            if (name == query) score += 1200;
            else if (name.StartsWith(query, StringComparison.Ordinal)) score += 900;
            else if (StartsAWord(name, query)) score += 700;
            else if (name.Contains(query, StringComparison.Ordinal)) score += 520;
            else if (path.Contains(query, StringComparison.Ordinal)) score += 90;
            else return null;
        }

        if (item.Kind == SearchItemKind.Application) score += 120;
        if (item.IsUserPath) score += 160;
        if (CommonApplications.IsMatch(item.Name)) score += 150;

        // Findable but never first. Windows holds thousands of files whose names collide with
        // everything, and an exact match is the one case where the user really did mean that one.
        if (item.IsWindowsPath && name != query) score -= 450;

        return score + Recency(item.LastWrite, now);
    }

    /// <summary>Up to 80 points, fading over the last eighty days.</summary>
    private static int Recency(DateTime lastWrite, DateTime now)
        => Math.Clamp(80 - (int)(now - lastWrite).TotalDays, 0, 80);

    /// <summary>Whether the query begins a word inside the name.</summary>
    /// <remarks>
    /// Separators only — space, dot, underscore, hyphen — so <c>note</c> finds <c>release_note</c>
    /// without every name that merely contains the letters ranking as highly.
    /// </remarks>
    private static bool StartsAWord(string name, string query)
    {
        var at = name.IndexOf(query, StringComparison.Ordinal);

        while (at > 0)
        {
            if (name[at - 1] is ' ' or '.' or '_' or '-')
            {
                return true;
            }

            at = name.IndexOf(query, at + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>The last segment of a path query, however it is spelled.</summary>
    private static string LastSegment(string query)
    {
        var at = query.LastIndexOfAny(['\\', '/']);

        return at >= 0 && at < query.Length - 1 ? query[(at + 1)..] : query;
    }
}
