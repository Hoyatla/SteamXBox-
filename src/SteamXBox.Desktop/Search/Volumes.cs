using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>A volume the machine currently has, and the identity that outlives its letter.</summary>
/// <param name="Identity">
/// Windows' own name for the volume — <c>\\?\Volume{…}\</c> — which does not change when the drive
/// letter does.
/// </param>
/// <param name="Letter">Where it is mounted right now, which does.</param>
/// <param name="Label">What the user called it, or the drive type when they called it nothing.</param>
/// <param name="IsRemovable">Whether it can be unplugged, and therefore ought to be ejected first.</param>
public readonly record struct Volume(string Identity, string Letter, string Label, bool IsRemovable);

/// <summary>Asks Windows to release a removable volume.</summary>
/// <remarks>
/// Through the shell's own <c>Eject</c> verb rather than by calling <c>CM_Request_Device_Eject</c>
/// directly. The shell verb is the same path the user's own right-click takes: it flushes the
/// caches, asks the applications holding the volume, and tells the user which one refuses. A direct
/// eject reports success while a file is still being written, which is how data is lost on a stick.
/// </remarks>
public static class Eject
{
    /// <summary>Ejects the volume mounted at <paramref name="letter"/>. Returns what to show.</summary>
    public static string Request(string letter, Action<string>? log = null)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");

            if (shellType is null || Activator.CreateInstance(shellType) is not { } instance)
            {
                return "";
            }

            dynamic shell = instance;

            // 17 is the drives folder. Its items carry the verbs the right-click menu shows.
            dynamic drives = shell.NameSpace(17);
            dynamic item = drives.ParseName(letter.TrimEnd('\\'));

            if (item is null)
            {
                return "";
            }

            item.InvokeVerb("Eject");
            log?.Invoke($"eject requested for {letter}.");

            return $"{letter.TrimEnd('\\')} éjecté.";
        }
        catch (Exception exception)
        {
            log?.Invoke($"eject {letter} failed: {exception.GetType().Name}: {exception.Message}");
            return "";
        }
    }
}

/// <summary>
/// Finds the machine's volumes, by identity rather than by letter.
/// </summary>
/// <remarks>
/// This replaces indexing the surface of every drive. Walking drive roots produced thousands of
/// entries that were nobody's answer to anything, and it had a fault no amount of walking fixes:
/// <b>drive letters move</b>. A stick indexed as <c>E:\</c> comes back as <c>F:\</c>, and every entry
/// filed under the old letter then points at whatever took its place. That is the same mistake as
/// filing a controller's settings under its XInput slot, and it was fixed there the same way — by
/// asking the system for an identity instead of using a position.
///
/// <para>
/// So a volume is one entry, found by its label or its letter, and opened at whatever letter it
/// currently has. What is inside it is not indexed: the launcher opens the volume and the file
/// explorer takes over, which is what the user was going to do anyway.
/// </para>
/// </remarks>
public static class Volumes
{
    /// <summary>Every ready fixed or removable volume, as searchable entries.</summary>
    public static IReadOnlyList<SearchItem> AsSearchItems(Action<string>? log = null)
    {
        var items = new List<SearchItem>();

        foreach (var volume in All(log))
        {
            // Both spellings are findable: people look for "USB", and people look for "E:".
            items.Add(new SearchItem(
                $"{volume.Label} ({volume.Letter.TrimEnd('\\')})",
                volume.Letter,
                SearchItemKind.Drive,
                IndexPlan.UserFolderPriority,
                DateTime.UtcNow));
        }

        return items;
    }

    /// <summary>The volumes present now, each with its durable identity.</summary>
    public static IReadOnlyList<Volume> All(Action<string>? log = null)
    {
        var volumes = new List<Volume>();

        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"volumes: listing failed: {exception.Message}");
            return volumes;
        }

        foreach (var drive in drives)
        {
            string letter;
            string label;

            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }

                letter = drive.RootDirectory.FullName;

                // A volume with no label still needs a name somebody can type. Its type is the next
                // most useful thing to call it.
                label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? (drive.DriveType == DriveType.Removable ? "Amovible" : "Disque")
                    : drive.VolumeLabel;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A drive that went away between the listing and here — a stick being pulled.
                continue;
            }

            var identity = IdentityOf(letter) ?? letter;

            volumes.Add(new Volume(identity, letter, label, drive.DriveType == DriveType.Removable));
            log?.Invoke($"volume {letter} = {label} [{identity}]");
        }

        return volumes;
    }

    /// <summary>
    /// The <c>\\?\Volume{…}\</c> name of whatever is mounted at <paramref name="mountPoint"/>.
    /// </summary>
    /// <remarks>
    /// Null when Windows will not say, and the caller then falls back to the letter. That fallback is
    /// honest rather than safe: it means this one volume is identified by its position, and it will
    /// be confused with whatever takes that letter next. Better than refusing to list the drive at
    /// all, and the log names it either way.
    /// </remarks>
    public static string? IdentityOf(string mountPoint)
    {
        try
        {
            var buffer = new StringBuilder(64);

            return GetVolumeNameForVolumeMountPointW(mountPoint, buffer, buffer.Capacity)
                ? buffer.ToString()
                : null;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string mountPoint, StringBuilder volumeName, int length);
}
