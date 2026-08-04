using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SteamXBox.Shell.Theming.Windows;

/// <summary>
/// Remembers what Windows looked like before SteamXBox changed it.
/// </summary>
/// <remarks>
/// This is the piece the whole Windows-theming plan rests on, and it is written first for that
/// reason. Anything that writes into the user's hive must be undoable, or uninstalling SteamXBox
/// leaves a machine carrying cursors and colours whose origin nobody can trace.
///
/// Two rules make the difference between a backup and a trap:
///
/// A value that did not exist and a value that was empty are not the same thing. Under
/// <c>Control Panel\Cursors</c>, an empty string means "use the system default"; deleting the value
/// instead would be a different state. The snapshot records absence as null and emptiness as "",
/// and the restore reproduces each faithfully.
///
/// The snapshot is written once and never overwritten while it exists. Applying a second theme on
/// top of the first must still restore the state from before the first — otherwise the second
/// backup would capture the first theme's values and "restore" would mean going back to a theme the
/// user never chose.
/// </remarks>
public sealed class WindowsStateBackup
{
    private readonly string _path;

    public WindowsStateBackup(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamXBox", "windows-state-backup.json");

    /// <summary>A registry value as it was found. Null <see cref="Data"/> means it did not exist.</summary>
    public sealed class Entry
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Data { get; set; }
    }

    public sealed class Snapshot
    {
        public string TakenUtc { get; set; } = "";
        public List<Entry> Entries { get; set; } = [];
    }

    public bool Exists => File.Exists(_path);

    /// <summary>
    /// Records the current value of every named entry, unless a snapshot already exists.
    /// </summary>
    /// <returns>True when a snapshot was written, false when one was already in place.</returns>
    public bool CaptureOnce(IEnumerable<(string Key, string Name)> values)
    {
        if (Exists)
        {
            return false;
        }

        var snapshot = new Snapshot { TakenUtc = DateTimeOffset.UtcNow.ToString("O") };

        foreach (var (key, name) in values)
        {
            using var registryKey = Registry.CurrentUser.OpenSubKey(key);
            var data = registryKey?.GetValue(name, null);

            snapshot.Entries.Add(new Entry
            {
                Key = key,
                Name = name,

                // ToString() on a null stays null, which is exactly the distinction being kept.
                Data = data?.ToString(),
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    /// <summary>
    /// Puts every recorded value back and discards the snapshot.
    /// </summary>
    /// <returns>False when there was nothing to restore.</returns>
    public bool Restore()
    {
        if (!Exists)
        {
            return false;
        }

        var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path));
        if (snapshot is null)
        {
            return false;
        }

        foreach (var entry in snapshot.Entries)
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.Key);
            if (key is null)
            {
                continue;
            }

            if (entry.Data is null)
            {
                // It did not exist before. Deleting is the faithful undo; creating an empty string
                // would leave a value the user never had.
                key.DeleteValue(entry.Name, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(entry.Name, entry.Data, RegistryValueKind.String);
            }
        }

        // Deleted only after a successful pass: a snapshot that survives a failed restore can be
        // used again, one that is deleted first cannot.
        File.Delete(_path);
        return true;
    }
}
