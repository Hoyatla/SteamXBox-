using System.IO;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// Walks the disk and produces the index the launcher searches.
/// </summary>
/// <remarks>
/// The judgement — which places, how deep, what each is worth — is in
/// <see cref="IndexPlan"/> where it can be read and tested. What is here is the walking, which
/// touches Windows and therefore cannot be.
///
/// <para>
/// Every enumeration is guarded and every failure is skipped. A machine has folders that refuse to
/// open, drives that go away mid-walk and paths longer than the API accepts; an index that gives up
/// at the first of them indexes almost nothing on a real machine.
/// </para>
/// </remarks>
public static class SearchIndexBuilder
{
    /// <summary>Builds the whole index. Minutes of work on a large machine, so never on the UI thread.</summary>
    public static IReadOnlyList<SearchItem> Build(Action<string>? log = null)
    {
        var items = new List<SearchItem>(4096);

        StartMenusAndDesktops(items, log);
        ExecutablesOnThePath(items, log);
        items.AddRange(StoreApps.AsSearchItems(log));
        ProfileFolders(items, log);
        UserFolders(items, log);

        // Volumes are deliberately absent here. They change while the session runs — a key plugged in
        // between two searches has to be findable at once — so the launcher keeps them in a set of
        // their own, refreshed on the device-change broadcast rather than by rebuilding all of this.

        // One entry per path. The same shortcut sits in the user's Start menu and the common one, and
        // showing it twice is the kind of thing that makes a launcher look broken.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = items.Where(item => seen.Add(item.Path)).ToList();

        log?.Invoke($"search index: {unique.Count} entries ({items.Count - unique.Count} duplicates dropped).");

        return unique;
    }

    private static void StartMenusAndDesktops(List<SearchItem> items, Action<string>? log)
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.StartMenu,
                     Environment.SpecialFolder.CommonStartMenu,
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.CommonDesktopDirectory,
                 })
        {
            var root = Environment.GetFolderPath(folder);

            if (root.Length == 0)
            {
                continue;
            }

            foreach (var path in Files(root, "*.lnk", SearchOption.AllDirectories, log))
            {
                Add(items, path, SearchItemKind.Application, IndexPlan.ShortcutPriority);
            }
        }
    }

    private static void ExecutablesOnThePath(List<SearchItem> items, Action<string>? log)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            var priority = IndexPlan.PathDirectoryPriority(directory, windows);

            foreach (var path in Files(directory, "*.exe", SearchOption.TopDirectoryOnly, log))
            {
                Add(items, path, SearchItemKind.Application, priority);
            }
        }
    }

    /// <summary>The folders sitting directly in the user's profile.</summary>
    /// <remarks>
    /// Documents, Pictures and the rest were indexed while the folder holding them was not, so
    /// everything an application puts beside them was invisible: Dropbox, OneDrive, source, and
    /// whatever the user made themselves. Searching for "dropbox" found the tray application's
    /// shortcut — which does nothing when it is already running — and never the folder, which is
    /// what was wanted.
    ///
    /// <para>
    /// One level, and no files: the profile root holds NTUSER.DAT and its kin, which nobody opens
    /// from a launcher. Hidden and system folders are skipped for the same reason, and so are the
    /// dot-folders every development tool leaves behind — <c>.cargo</c>, <c>.conda</c>, <c>.config</c>
    /// — which would be thirty entries of noise against the twenty that matter.
    /// </para>
    /// </remarks>
    private static void ProfileFolders(List<SearchItem> items, Action<string>? log)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (profile.Length == 0)
        {
            return;
        }

        foreach (var path in Directories(profile, log))
        {
            var name = Path.GetFileName(path);

            if (name.StartsWith('.'))
            {
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(path);

                if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // Above the other user folders: these are the places people organise their work into,
            // and one of them is usually the answer when its name is typed.
            Add(items, path, SearchItemKind.Folder, IndexPlan.UserFolderPriority + 20, userPath: true);
        }
    }

    private static void UserFolders(List<SearchItem> items, Action<string>? log)
    {
        foreach (var root in UserRoots())
        {
            foreach (var path in Directories(root, log).Take(IndexPlan.MaxUserFolders))
            {
                Add(items, path, SearchItemKind.Folder, IndexPlan.UserFolderPriority, userPath: true);
            }

            foreach (var path in Files(root, "*", SearchOption.TopDirectoryOnly, log).Take(IndexPlan.MaxUserFiles))
            {
                Add(items, path, SearchItemKind.File, IndexPlan.UserFolderPriority, userPath: true);
            }
        }
    }

    /// <remarks>
    /// Downloads has no <see cref="Environment.SpecialFolder"/>, so it is composed from the profile
    /// and checked. The same gap the launch vocabulary has to work around.
    /// </remarks>
    private static IEnumerable<string> UserRoots()
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyVideos,
                 })
        {
            var path = Environment.GetFolderPath(folder);

            if (path.Length > 0 && Directory.Exists(path))
            {
                yield return path;
            }
        }

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(downloads))
        {
            yield return downloads;
        }
    }


    /// <summary>Files under a root, skipping only the folders that refuse.</summary>
    /// <remarks>
    /// Walked folder by folder rather than with <c>SearchOption.AllDirectories</c>. That option
    /// throws at the first directory it cannot open and abandons the whole walk, and the log showed
    /// precisely what that costs: the user's Start menu holds a legacy junction called
    /// <c>Programmes</c> that denies access, so the entire Start menu — every application shortcut
    /// on the machine, the single most useful source a launcher has — was lost to one folder.
    ///
    /// <para>
    /// Walking by hand means a refusal costs that folder and nothing above or beside it. The depth
    /// limit is there because reparse points can point at their own ancestors, and a launcher that
    /// walks in a circle never finishes indexing.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Files(
        string root, string filter, SearchOption option, Action<string>? log)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        const int MaxDepth = 8;

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();

            string[] files;

            try
            {
                files = Directory.GetFiles(directory, filter);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // This folder only. Its siblings and its parent are unaffected.
                continue;
            }

            foreach (var file in files)
            {
                if (!IndexPlan.IsExcluded(file))
                {
                    yield return file;
                }
            }

            if (option != SearchOption.AllDirectories || depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in Directories(directory, log: null))
            {
                pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static IEnumerable<string> Directories(string root, Action<string>? log)
    {
        string[] found;

        try
        {
            found = Directory.Exists(root)
                ? Directory.GetDirectories(root)
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"search index: cannot list {root}: {exception.Message}");
            return [];
        }

        return found.Where(path => !IndexPlan.IsExcluded(path));
    }

    /// <remarks>
    /// A shortcut is shown under its own name, without the <c>.lnk</c>: nobody looks for
    /// "Firefox.lnk". Everything else keeps its extension, which is how the user tells two files
    /// apart.
    /// </remarks>
    private static void Add(
        List<SearchItem> items, string path, SearchItemKind kind, int priority, bool userPath = false)
    {
        try
        {
            var name = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(path)
                : Path.GetFileName(path);

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            items.Add(new SearchItem(
                name,
                path,
                kind,
                priority,
                LastWriteOf(path),
                userPath,
                windows.Length > 0 && path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // One unreadable entry is not a reason to lose the rest.
        }
    }

    /// <remarks>
    /// Falls back to the epoch rather than to now. A file whose date cannot be read would otherwise
    /// look brand new and take the recency bonus from everything that genuinely is.
    /// </remarks>
    private static DateTime LastWriteOf(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.UnixEpoch;
        }
    }
}
