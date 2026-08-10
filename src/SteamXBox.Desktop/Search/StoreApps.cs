using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// The applications Windows lists in <c>shell:AppsFolder</c>.
/// </summary>
/// <remarks>
/// Store and MSIX applications have no executable anybody can point at. They are launched by an
/// <i>application user model id</i> — <c>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c> — and the
/// only place Windows offers the list is this virtual folder. Without it a launcher cannot find the
/// Calculator, the Photos viewer, WhatsApp or anything else installed from the Store, which on a
/// current machine is most of what people open.
///
/// <para>
/// Reached through the Shell automation object by late binding rather than a COM reference. The
/// reference would add an interop assembly and a build-time dependency on a type library, for one
/// call that returns two strings per entry.
/// </para>
/// </remarks>
public static class StoreApps
{
    /// <summary>Every packaged application, as searchable entries.</summary>
    /// <remarks>
    /// Only entries whose id contains <c>!</c>. The folder also lists ordinary desktop programs,
    /// which the Start menu sweep has already found under their real names and paths; taking them
    /// again would show every one of them twice.
    /// </remarks>
    public static IReadOnlyList<SearchItem> AsSearchItems(Action<string>? log = null)
    {
        var items = new List<SearchItem>();

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");

            if (shellType is null)
            {
                log?.Invoke("store apps: Shell.Application is unavailable; skipping.");
                return items;
            }

            dynamic? shell = Activator.CreateInstance(shellType);

            if (shell is null)
            {
                return items;
            }

            dynamic folder = shell.NameSpace("shell:AppsFolder");
            dynamic entries = folder.Items();

            foreach (dynamic entry in entries)
            {
                string name;
                string id;

                try
                {
                    name = entry.Name as string ?? "";
                    id = entry.Path as string ?? "";
                }
                catch
                {
                    // One entry the shell will not describe. The rest are still worth having.
                    continue;
                }

                if (name.Length == 0 || !id.Contains('!'))
                {
                    continue;
                }

                items.Add(new SearchItem(
                    name,
                    $"shell:AppsFolder\\{id}",
                    SearchItemKind.Application,
                    IndexPlan.StoreApplicationPriority,

                    // Packaged applications have no meaningful date here, and reading one would mean
                    // opening the package. The epoch keeps them out of the recency bonus rather than
                    // giving them a false one.
                    DateTime.UnixEpoch));
            }

            log?.Invoke($"store apps: {items.Count} packaged application(s).");
        }
        catch (Exception exception)
        {
            // Shell automation can be disabled by policy, and a machine without it must still get a
            // launcher — one without Store applications, and a line saying so.
            log?.Invoke($"store apps unavailable: {exception.GetType().Name}: {exception.Message}");
        }

        return items;
    }
}
