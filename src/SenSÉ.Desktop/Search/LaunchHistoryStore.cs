using System.IO;
using System.Text.Json;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Search;

/// <summary>
/// Reads and writes the launch history, in SenSÉ's own folder.
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
        "SenSÉ",
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
            // Protected like the mail index, and for a smaller version of the same reason: a list of
            // what somebody searched for names the people, the documents and the subjects they were
            // looking for. It is short, so it looks harmless — the mail index is the one that looks
            // dangerous, and both say the same things about the same person.
            var json = SenSÉ.Tools.Search.PersonalFile.Read(Path, out var wasPlain, log);

            if (json is null)
            {
                return new LaunchHistory();
            }

            var saved = JsonSerializer.Deserialize<Dictionary<string, LaunchRecord>>(json);

            log?.Invoke($"search history: {saved?.Count ?? 0} entries.");

            var history = new LaunchHistory(saved);

            if (wasPlain)
            {
                log?.Invoke("search history: found in plain text; writing it back protected.");
                Save(history, log);
            }

            return history;
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
            SenSÉ.Tools.Search.PersonalFile.Write(
                Path,
                JsonSerializer.Serialize(history.All, new JsonSerializerOptions { WriteIndented = true }),
                log);
        }
        catch (Exception exception)
        {
            // Losing the history costs the ranking a little. Losing the launch would cost the user
            // what they were doing.
            log?.Invoke($"search history not saved: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
