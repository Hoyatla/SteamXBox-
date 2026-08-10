using System.IO;
using System.Text.Json;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// Reads and writes the launch history, in SteamXBox's own folder.
/// </summary>
/// <remarks>
/// Beside the other things the environment keeps — profiles, keyboard settings — rather than
/// anywhere near the files it describes. It is a record of what this user opens, so it belongs with
/// their settings and it never leaves the machine.
/// </remarks>
public static class LaunchHistoryStore
{
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "search-history.json");

    /// <summary>Loads what was remembered, or an empty history.</summary>
    /// <remarks>
    /// Any failure gives an empty one. A launcher whose ranking is slightly worse than yesterday is
    /// a launcher; a launcher that refuses to open because a settings file is malformed is not.
    /// </remarks>
    public static LaunchHistory Load(Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new LaunchHistory();
            }

            var saved = JsonSerializer.Deserialize<Dictionary<string, LaunchRecord>>(File.ReadAllText(Path));

            log?.Invoke($"search history: {saved?.Count ?? 0} entries.");

            return new LaunchHistory(saved);
        }
        catch (Exception exception)
        {
            log?.Invoke($"search history unreadable, starting fresh: {exception.GetType().Name}: {exception.Message}");
            return new LaunchHistory();
        }
    }

    /// <summary>Writes it out. Called after each open, which is a few hundred bytes.</summary>
    public static void Save(LaunchHistory history, Action<string>? log = null)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

            File.WriteAllText(
                Path,
                JsonSerializer.Serialize(history.All, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            // Losing the history costs the ranking a little. Losing the launch would cost the user
            // what they were doing.
            log?.Invoke($"search history not saved: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
