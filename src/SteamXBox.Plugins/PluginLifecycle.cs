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

    /// <summary>
    /// Variable d'environnement qui deplace la storage de l'hote, pour un harnais de test.
    /// </summary>
    /// <remarks>
    /// Sans elle, une serie de tests ecrit dans le fichier de reglages de la personne qui la lance :
    /// on en a retrouve onze identifiants <c>test-steamxbox-…</c> dans le vrai fichier. Un test qui
    /// modifie l'installation de son auteur est un defaut a lui seul, avant meme d'etre instable.
    /// </remarks>
    public const string StorageRootVariable = "STEAMXBOX_HOST_STORAGE";

    /// <summary>
    /// Un seul ecrivain a la fois dans ce processus.
    /// </summary>
    /// <remarks>
    /// <see cref="SetEnabled"/> lit le fichier, ajoute une entree et le reecrit en entier. Deux
    /// appels simultanes lisaient donc tous deux l'etat d'avant, et le second effacait la decision
    /// du premier — une entree perdue, sans erreur nulle part. La lecture est prise sous le meme
    /// verrou : c'est ce qui garantit que personne ne lit pendant qu'on remplace le fichier.
    /// </remarks>
    private static readonly object Gate = new();

    /// <summary>What the user has decided about each tool, in the host's storage.</summary>
    private static string ChoicesPath => Path.Combine(HostStorage, "plugins-choices.json");

    /// <inheritdoc cref="StorageRootVariable"/>
    private static string HostStorage
        => Environment.GetEnvironmentVariable(StorageRootVariable) is { Length: > 0 } elsewhere
            ? elsewhere
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamXBox");

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
        lock (Gate)
        {
            try
            {
                if (!File.Exists(ChoicesPath))
                {
                    return Empty();
                }

                using var file = Open(FileMode.Open, FileAccess.Read, FileShare.Read);

                return ReadFrom(file);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // An unreadable file means nothing was decided, so every tool takes its own default.
                return Empty();
            }
        }
    }

    /// <summary>Whether a tool runs, given what the user decided and what it ships as.</summary>
    public static bool IsEnabled(string id, bool byDefault)
        => Choices().TryGetValue(id, out var chosen) ? chosen : byDefault;

    /// <summary>Records the user's decision, without touching the tool's folder.</summary>
    /// <remarks>
    /// <b>Le defaut que ceci corrige.</b> C'etait « lire le fichier, ajouter une entree, le
    /// reecrire », sans rien pour tenir les deux moities ensemble. Deux appels simultanes lisaient
    /// donc le meme etat d'avant et le second effacait la decision du premier. Rien n'echouait :
    /// l'entree manquait, l'outil reprenait son defaut, et personne n'avait de quoi le rattacher a
    /// autre chose qu'a de la malchance.
    ///
    /// <para>
    /// Relecture, fusion et reecriture se font maintenant sur un seul descripteur ouvert en
    /// exclusif, donc l'intervalle ou un autre ecrivain pouvait lire l'etat d'avant n'existe plus.
    /// <see cref="Gate"/> en dispense les threads de ce processus ; le descripteur couvre les autres
    /// processus, ce qui compte parce que le produit en fait tourner plusieurs — l'environnement et
    /// la fenetre de configuration montrent tous deux cette liste d'outils.
    /// </para>
    /// </remarks>
    public static void SetEnabled(string id, bool enabled, Action<string>? log = null)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(HostStorage);

                using var file = Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                var choices = ReadFrom(file);
                choices[id] = enabled;

                // Repositionne et tronque : le fichier est reecrit en entier, et une entree retiree
                // ne doit pas laisser derriere elle la queue de la version precedente.
                file.Position = 0;
                file.SetLength(0);

                JsonSerializer.Serialize(file, choices);

                log?.Invoke($"plugin {id}: {(enabled ? "enabled" : "disabled")}.");
            }
            catch (Exception exception)
            {
                log?.Invoke($"plugin {id}: could not be switched: {exception.Message}");
            }
        }
    }

    private static Dictionary<string, bool> Empty()
        => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lit le fichier deja ouvert, en repartant de son debut.</summary>
    /// <remarks>
    /// Prend un descripteur plutot qu'un chemin, pour que <see cref="SetEnabled"/> relise
    /// exactement le fichier qu'il s'apprete a reecrire et non un autre ouvert entre-temps.
    /// </remarks>
    private static Dictionary<string, bool> ReadFrom(FileStream file)
    {
        if (file.Length == 0)
        {
            return Empty();
        }

        file.Position = 0;

        var stored = JsonSerializer.Deserialize<Dictionary<string, bool>>(file);

        return stored is null ? Empty() : new Dictionary<string, bool>(stored, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ouvre le fichier des decisions, en reessayant tant qu'un autre processus le tient.
    /// </summary>
    /// <remarks>
    /// Un partage refuse est passager par construction : celui qui tient le fichier fait une
    /// lecture ou une ecriture de quelques centaines d'octets. Abandonner a la premiere tentative
    /// rendrait la valeur par defaut sur une lecture — « rien n'a jamais ete decide » — ou perdrait
    /// la decision sur une ecriture, dans les deux cas sans que l'utilisateur voie autre chose
    /// qu'un reglage qui ne tient pas.
    /// </remarks>
    private static FileStream Open(FileMode mode, FileAccess access, FileShare share)
    {
        const int Attempts = 20;
        const int PauseMs = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(ChoicesPath, mode, access, share);
            }
            // Un fichier absent n'est pas un partage refuse : c'est la reponse definitive « rien
            // n'a jamais ete decide », et la reessayer ne ferait que retarder de cent millisecondes
            // une lecture parfaitement concluante.
            catch (IOException exception) when (attempt < Attempts && exception is not FileNotFoundException)
            {
                Thread.Sleep(PauseMs);
            }
        }
    }

    /// <summary>
    /// Compresses a tool and removes its folder.
    /// </summary>
    /// <remarks>
    /// The archive is written and verified before the folder goes. The other order turns a failed
    /// compression into a deletion, which is the one outcome the user did not ask for.
    /// </remarks>
    public static bool Archive(string root, string id, Action<string>? log = null, string corps = "")
    {
        var folder = Path.Combine(root, id);
        var archive = ArchivePath(root, id);

        // Le corps d'abord, et ce n'est pas un détail d'ordre. C'est lui qui pèse et lui qui peut
        // échouer — disque plein, fichier verrouillé par le programme encore ouvert. S'il échoue
        // après qu'on a rangé le manifeste, l'outil se retrouve sans description et avec ses cinq
        // cents mégaoctets toujours là : le pire des deux états.
        if (corps.Length > 0 && Directory.Exists(corps)
            && !Ranger(corps, ArchiveCorps(corps, id), $"{id} (corps)", log))
        {
            return false;
        }

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
    public static bool Restore(string root, string id, Action<string>? log = null, string corps = "")
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

            // Le corps revient après sa description, parce que c'est elle qui dit où il va. Une
            // archive de corps absente n'est pas une erreur : tous les outils n'en ont pas.
            if (corps.Length > 0 && File.Exists(ArchiveCorps(corps, id)) && !Directory.Exists(corps))
            {
                ZipFile.ExtractToDirectory(ArchiveCorps(corps, id), corps);
                File.Delete(ArchiveCorps(corps, id));

                log?.Invoke($"plugin {id}: its body is back in {Path.GetFileName(corps)}.");
            }

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
    public static bool Delete(string root, string id, Action<string>? log = null, string corps = "")
    {
        try
        {
            var folder = Path.Combine(root, id);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            // Éjecter un outil hébergé sans emporter son corps laisserait cinq cents mégaoctets dans
            // Outils sans plus rien pour dire à qui ils appartiennent : exactement ce que le produit
            // reproche aux désinstalleurs des autres.
            if (corps.Length > 0 && Directory.Exists(corps))
            {
                Directory.Delete(corps, recursive: true);
                log?.Invoke($"plugin {id}: its body in {Path.GetFileName(corps)} went with it.");
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

    /// <summary>
    /// Où se range l'archive du corps d'un outil hébergé.
    /// </summary>
    /// <remarks>
    /// À côté du corps, pas à côté du manifeste : cinq cents mégaoctets rangés dans le dossier des
    /// descriptions donneraient un <c>Plugins</c> plus lourd que tout le reste, et une sauvegarde
    /// du dossier des manifestes emporterait sans le vouloir les programmes.
    /// </remarks>
    public static string ArchiveCorps(string corps, string id)
        => Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(corps.TrimEnd(Path.DirectorySeparatorChar)))!,
            ArchiveFolderName,
            Safe(id) + ".zip");

    /// <summary>
    /// Range un dossier dans une archive, et ne l'efface que si l'archive tient debout.
    /// </summary>
    /// <remarks>
    /// La relecture avant l'effacement est la même précaution que pour un manifeste, et elle compte
    /// bien plus ici : une archive tronquée d'un kilooctet se refait, une archive tronquée de cinq
    /// cents mégaoctets se retélécharge — quand la source existe encore.
    /// </remarks>
    private static bool Ranger(string dossier, string archive, string quoi, Action<string>? log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            ZipFile.CreateFromDirectory(dossier, archive, CompressionLevel.Optimal, includeBaseDirectory: false);

            using (var check = ZipFile.OpenRead(archive))
            {
                if (check.Entries.Count == 0)
                {
                    log?.Invoke($"plugin {quoi}: the archive came out empty; the folder was kept.");

                    return false;
                }
            }

            Directory.Delete(dossier, recursive: true);
            log?.Invoke($"plugin {quoi}: archived to {Path.GetFileName(archive)}.");

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin {quoi}: could not be archived: {exception.Message}");

            return false;
        }
    }

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
