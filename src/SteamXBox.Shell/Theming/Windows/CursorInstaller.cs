using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SteamXBox.Plugins;

namespace SteamXBox.Shell.Theming.Windows;

/// <summary>
/// Applies a cursor pack to Windows, and puts the previous one back.
/// </summary>
/// <remarks>
/// Nothing is copied into <c>%WINDIR%\Cursors</c> and nothing is written outside the current user's
/// hive, so no administrator rights are needed and no file is left behind anywhere SteamXBox does
/// not own. The registry accepts an absolute path to the cursor wherever it lives, so the pack
/// stays in its plugin folder.
///
/// The backup is taken before the first write, once, and covers every role Windows knows — not only
/// the ones this pack defines. A pack that leaves <c>NWPen</c> alone must still be undoable if a
/// later one sets it.
/// </remarks>
public static class CursorInstaller
{
    private const string CursorsKey = @"Control Panel\Cursors";

    /// <summary>Tells Windows to re-read the cursor settings.</summary>
    private const uint SpiSetCursors = 0x0057;
    private const uint SpifSendChange = 0x02;

    /// <summary>
    /// Points every role the pack defines at its file.
    /// </summary>
    /// <param name="scheme">Roles and file names, from the pack's <c>install.inf</c>.</param>
    /// <param name="cursorFolder">Folder holding the pack's <c>.cur</c> and <c>.ani</c> files.</param>
    /// <param name="backup">Snapshot store; captured before the first write.</param>
    /// <returns>The roles actually applied, and the files that were missing.</returns>
    public static (int Applied, IReadOnlyList<string> Missing) Apply(
        CursorScheme scheme, string cursorFolder, WindowsStateBackup backup)
    {
        // Every known role plus the scheme name and the source flag, so a later pack that touches
        // more roles than this one can still be undone.
        backup.CaptureOnce(BackedUpValues());

        var missing = new List<string>();
        var applied = 0;

        using var key = Registry.CurrentUser.CreateSubKey(CursorsKey)
                        ?? throw new InvalidOperationException("Impossible d'ouvrir Control Panel\\Cursors.");

        foreach (var (role, fileName) in scheme.Roles)
        {
            var path = Path.Combine(cursorFolder, fileName);
            if (!File.Exists(path))
            {
                // Named rather than silently skipped: a pack whose INF lists a file it does not
                // ship would otherwise apply half a cursor set and look like it worked.
                missing.Add(fileName);
                continue;
            }

            key.SetValue(role, path, RegistryValueKind.String);
            applied++;
        }

        if (scheme.Name.Length > 0)
        {
            key.SetValue("", scheme.Name, RegistryValueKind.String);
        }

        // 2 marks the scheme as user-defined. Without it the mouse control panel keeps showing the
        // previous scheme's name next to the new cursors.
        key.SetValue("Scheme Source", 2, RegistryValueKind.DWord);

        Refresh();
        return (applied, missing);
    }

    /// <summary>Restores the cursors Windows had before SteamXBox touched them.</summary>
    public static bool Restore(WindowsStateBackup backup)
    {
        if (!backup.Restore())
        {
            return false;
        }

        Refresh();
        return true;
    }

    /// <summary>Every value this installer may overwrite, so the backup covers all of them.</summary>
    private static IEnumerable<(string, string)> BackedUpValues()
    {
        foreach (var role in CursorSchemeParser.KnownRoles)
        {
            yield return (CursorsKey, role);
        }

        // The default value holds the scheme name.
        yield return (CursorsKey, "");
        yield return (CursorsKey, "Scheme Source");
    }

    /// <summary>
    /// Makes the change visible without a sign-out.
    /// </summary>
    /// <remarks>
    /// Writing the registry alone changes nothing on screen: the cursors are cached until something
    /// asks the system to reload them.
    /// </remarks>
    private static void Refresh() => SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpifSendChange);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr pointer, uint winIni);
}
