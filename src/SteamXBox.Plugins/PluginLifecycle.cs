using System.IO.Compression;
using System.Text.Json;

namespace SteamXBox.Plugins;

/// <summary>Where a tool stands: installed, switched off, or packed away.</summary>
public enum PluginState
{
    /// <summary>On disk and running.</summary>
    Enabled,

    /// <summary>On disk, switched off. Nothing was moved; it comes back with one click.</summary>
    Disabled,

    /// <summary>Compressed to a single file, its folder gone. Recoverable, and takes no space.</summary>
    Archived,
}

/// <summary>A tool as the uninstall screen shows it.</summary>
/// <param name="Id">Its identifier, which is also its folder and its archive name.</param>
/// <param name="Name">What to call it in the list; the identifier when no manifest can be read.</param>
/// <param name="Version">As declared, or empty for an archive that has not been opened.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Bytes">What it occupies, folder or archive.</param>
public sealed record PluginEntry(string Id, string Name, string Version, PluginState State, long Bytes);

/// <summary>
/// Switching a tool off, packing it away, and throwing it out.
/// </summary>
/// <remarks>
/// "A tool is a folder" makes installing and uninstalling obvious, and that is its strength — but
/// moving folders by hand is not an interface. This is the same three operations with names:
/// switched off but kept, compressed and kept, or removed.
///
/// <para>
/// <b>Nothing is written inside a tool's folder.</b> Which tools are switched off is the host's
/// business and lives in the host's own storage, because a folder has to stay exactly what was
/// dropped in — otherwise throwing it away no longer leaves nothing behind, and the one definition
/// of detachable that can be checked stops being true.
/// </para>
/// </remarks>
public static class PluginLifecycle
{
    /// <summary>Where archives are kept, beside the tools rather than hidden away.</summary>
    /// <remarks>
    /// Inside the plugin folder on purpose: this is a portable product, so copying that one folder
    /// has to carry the tools <i>and</i> what was packed away. It is skipped by the scan, which only
    /// looks at folders holding a manifest.
    /// </remarks>
    public const string ArchiveFolderName = "_archives";

    /// <summary>What the user has decided about each tool, in the host's storage.</summary>
    private static string ChoicesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "plugins-choices.json");

    /// <summary>
    /// The tools the user has switched on or off by hand.
    /// </summary>
    /// <remarks>
    /// Only what was decided. A tool absent from this has never been touched, and takes whatever its
    /// manifest says — which is how a tool can ship present but idle without that looking like the
    /// user had turned it off.
    /// </remarks>
    public static IReadOnlyDictionary<string, bool> Choices()
    {
        try
        {
            if (!File.Exists(ChoicesPath))
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            var stored = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(ChoicesPath));

            return stored is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable file means nothing was decided, so every tool takes its own default.
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Whether a tool runs, given what the user decided and what it ships as.</summary>
    public static bool IsEnabled(string id, bool byDefault)
        => Choices().TryGetValue(id, out var chosen) ? chosen : byDefault;

    /// <summary>Records the user's decision, without touching the tool's folder.</summary>
    public static void SetEnabled(string id, bool enabled, Action<string>? log = null)
    {
        try
        {
            var choices = new Dictionary<string, bool>(Choices(), StringComparer.OrdinalIgnoreCase)
            {
                [id] = enabled,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ChoicesPath)!);
            File.WriteAllText(ChoicesPath, JsonSerializer.Serialize(choices));

            log?.Invoke($"plugin {id}: {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {id}: could not be switched: {exception.Message}");
        }
    }

    /// <summary>
    /// Compresses a tool and removes its folder.
    /// </summary>
    /// <remarks>
    /// The archive is written and verified before the folder goes. The other order turns a failed
    /// compression into a deletion, which is the one outcome the user did not ask for.
    /// </remarks>
    public static bool Archive(string root, string id, Action<string>? log = null)
    {
        var folder = Path.Combine(root, id);
        var archive = ArchivePath(root, id);

        try
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            ZipFile.CreateFromDirectory(folder, archive, CompressionLevel.Optimal, includeBaseDirectory: false);

            // Opened again before anything is deleted. A zero-length or truncated archive would
            // otherwise be discovered on the day somebody tries to restore it.
            using (var check = ZipFile.OpenRead(archive))
            {
                if (check.Entries.Count == 0)
                {
                    log?.Invoke($"plugin {id}: the archive came out empty; the folder was kept.");
                    return false;
                }
            }

            Directory.Delete(folder, recursive: true);
            SetEnabled(id, enabled: true, log);

            log?.Invoke($"plugin {id}: archived to {Path.GetFileName(archive)}.");

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {id}: could not be archived: {exception.Message}");
            return false;
        }
    }

    /// <summary>Unpacks an archive back into a working tool.</summary>
    public static bool Restore(string root, string id, Action<string>? log = null)
    {
        var folder = Path.Combine(root, id);
        var archive = ArchivePath(root, id);

        try
        {
            if (!File.Exists(archive) || Directory.Exists(folder))
            {
                return false;
            }

            ZipFile.ExtractToDirectory(archive, folder);
            File.Delete(archive);

            log?.Invoke($"plugin {id}: restored.");

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {id}: could not be restored: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a tool's folder, and leaves any archive of it alone.
    /// </summary>
    /// <remarks>
    /// Deliberately not "delete everything". Somebody who archived a tool and then removes the copy
    /// they had unpacked still meant to keep the archive — that is what archiving it was for. The
    /// archive is deleted by <see cref="Forget"/>, which is a separate decision.
    /// </remarks>
    public static bool Delete(string root, string id, Action<string>? log = null)
    {
        try
        {
            var folder = Path.Combine(root, id);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            log?.Invoke($"plugin {id}: removed"
                + (File.Exists(ArchivePath(root, id)) ? ", its archive kept." : "."));

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {id}: could not be removed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Removes the archive too, which is the only irreversible step.</summary>
    public static bool Forget(string root, string id, Action<string>? log = null)
    {
        try
        {
            var archive = ArchivePath(root, id);

            if (File.Exists(archive))
            {
                File.Delete(archive);
                log?.Invoke($"plugin {id}: archive deleted.");
            }

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {id}: archive could not be deleted: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// The tools installed or packed away, for the uninstall screen.
    /// </summary>
    /// <remarks>
    /// Tools only. A cursor pack is a <c>theme.windows</c> plugin — it changes a Windows setting and
    /// has no tile, no window and nothing to open — so listing it beside the calculator invites
    /// switching off something whose effect is somewhere else entirely. Themes belong to the screen
    /// that applies them.
    /// </remarks>
    public static IReadOnlyList<PluginEntry> List(string root)
    {
        var entries = new List<PluginEntry>();

        foreach (var manifest in PluginCatalog.Scan(root).Loaded.Where(m => m.Kind == PluginCategory.Tool))
        {
            entries.Add(new PluginEntry(
                manifest.Id,
                manifest.Name.Length > 0 ? manifest.Name : manifest.Id,
                manifest.Version,
                IsEnabled(manifest.Id, manifest.Enabled) ? PluginState.Enabled : PluginState.Disabled,
                SizeOf(manifest.Directory)));
        }

        var archives = Path.Combine(root, ArchiveFolderName);

        if (Directory.Exists(archives))
        {
            foreach (var file in Directory.GetFiles(archives, "*.zip"))
            {
                var id = Path.GetFileNameWithoutExtension(file);

                // An archive of something that is also unpacked is not listed twice: the folder is
                // what the user acts on, and the archive rides along with it.
                if (entries.Any(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                entries.Add(new PluginEntry(id, id, "", PluginState.Archived, new FileInfo(file).Length));
            }
        }

        return entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>Whether an archive of this tool exists.</summary>
    public static bool HasArchive(string root, string id) => File.Exists(ArchivePath(root, id));

    private static string ArchivePath(string root, string id)
        => Path.Combine(root, ArchiveFolderName, Safe(id) + ".zip");

    private static long SizeOf(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// A file name that cannot leave the folder it belongs to.
    /// </summary>
    /// <remarks>
    /// The identifier comes from a file somebody downloaded. One containing <c>..\</c> would choose
    /// where the archive is written, and later what gets deleted.
    /// </remarks>
    private static string Safe(string id)
    {
        var safe = new string(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());

        return safe.Length == 0 ? "tool" : safe;
    }
}
