using System.IO;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Search;

/// <summary>
/// Walks a drive for folders while the user types, instead of having indexed it.
/// </summary>
/// <remarks>
/// The index deliberately stops at the surface of the user's own folders, so nothing deep on another
/// drive is in it — <c>D:\…\Projets\SenSÉ-portable-win-x64</c> could not be found at all. Indexing
/// every drive would put back the thousands of entries that were just removed, and for a folder
/// nobody opens twice a year it would be paid for on every launch.
///
/// <para>
/// Folders only, which is what makes walking on demand affordable: a drive holds ten to fifty times
/// more files than directories, and <see cref="Directory.GetDirectories(string)"/> does not
/// enumerate the files at all. It is also the right answer — the user wants the folder, and the file
/// explorer takes over from there, exactly as for a volume.
/// </para>
///
/// <para>
/// Bounded three ways, because this runs while somebody is typing: a depth, a count, and a clock.
/// Whichever comes first stops it. A search that keeps going after the answer is on screen is a
/// search the machine is paying for and nobody is reading.
/// </para>
/// </remarks>
public static class LiveFolderSearch
{
    /// <summary>How deep below the drive root to look.</summary>
    private const int MaxDepth = 6;

    /// <summary>Enough to fill the list twice over; past that nobody scrolls.</summary>
    private const int MaxMatches = 60;

    /// <summary>The longest the walk may run before giving what it has.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The drive a query names, or null when it names none.
    /// </summary>
    /// <remarks>
    /// A drive term is what makes this affordable: it says which one disk to walk. Without one there
    /// would be nothing to do but walk them all on every keystroke, which is the cost this exists to
    /// avoid.
    /// </remarks>
    public static string? DriveNamedBy(IReadOnlyList<string> terms)
    {
        foreach (var term in terms)
        {
            if (term.Length == 2 && char.IsAsciiLetter(term[0]) && term[1] == ':')
            {
                var root = term.ToUpperInvariant() + "\\";

                return Directory.Exists(root) ? root : null;
            }
        }

        return null;
    }

    /// <summary>Folders under <paramref name="root"/> matching every term.</summary>
    /// <remarks>
    /// Breadth first, so the shallow answers — which are almost always the wanted ones — arrive
    /// before the walk has been anywhere deep, and the budget cuts off the part nobody was waiting
    /// for.
    /// </remarks>
    public static IReadOnlyList<SearchItem> Find(
        string root, IReadOnlyList<string> terms, CancellationToken cancellation)
    {
        var matches = new List<SearchItem>();

        if (terms.Count == 0)
        {
            return matches;
        }

        var started = DateTime.UtcNow;
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0 && matches.Count < MaxMatches)
        {
            if (cancellation.IsCancellationRequested || DateTime.UtcNow - started > Budget)
            {
                break;
            }

            var (directory, depth) = pending.Dequeue();

            string[] children;

            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // This folder only. A drive is full of ones that refuse.
                continue;
            }

            foreach (var child in children)
            {
                if (IndexPlan.IsExcluded(child))
                {
                    continue;
                }

                if (Matches(child, terms))
                {
                    matches.Add(new SearchItem(
                        System.IO.Path.GetFileName(child),
                        child,
                        SearchItemKind.Folder,

                        // Below anything indexed. These are found because the user named a drive, so
                        // they answer the query — but an installed application matching as well is
                        // still the likelier intent.
                        Priority: 0,
                        LastWriteOf(child)));
                }

                if (depth < MaxDepth)
                {
                    pending.Enqueue((child, depth + 1));
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Whether a path answers every term.
    /// </summary>
    /// <remarks>
    /// Against the whole path, not the folder's own name. The drive term only ever appears at the
    /// front of it, and the whole point of a two-word query is that one word says where and another
    /// says what.
    /// </remarks>
    private static bool Matches(string path, IReadOnlyList<string> terms)
    {
        var normalized = SearchText.Normalize(path);

        foreach (var term in terms)
        {
            if (!normalized.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static DateTime LastWriteOf(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.UnixEpoch;
        }
    }
}
