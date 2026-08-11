using System.IO;
using System.Text.Json;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// What a tool gets back when it is opened again.
/// </summary>
/// <remarks>
/// The host writes and reads it, in a place the host allocates — never the tool's own folder, which
/// must stay exactly what was dropped in so that throwing it away leaves nothing behind.
///
/// <para>
/// Only the values the manifest names in <c>remembers</c> are kept. What is not named is not stored,
/// so a tool cannot make something persist that whoever reads its manifest cannot see.
/// </para>
///
/// <para>
/// A tool must open correctly when nothing has been kept — first use, cleared values, a folder
/// carried to another machine. Remembered state is a convenience, never a condition of working,
/// which is why every failure here is silent and returns nothing rather than propagating.
/// </para>
/// </remarks>
public static class PluginMemory
{
    /// <summary>Where a tool's remembered values live.</summary>
    private static string PathFor(PluginManifest manifest) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "tools",
        Safe(manifest.Id) + ".json");

    public static IReadOnlyDictionary<string, string> Load(PluginManifest manifest, Action<string>? log = null)
    {
        try
        {
            var path = PathFor(manifest);

            if (!File.Exists(path))
            {
                return new Dictionary<string, string>();
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                   ?? new Dictionary<string, string>();
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin state unreadable for {manifest.Id}: {exception.Message}");
            return new Dictionary<string, string>();
        }
    }

    public static void Save(
        PluginManifest manifest, IReadOnlyDictionary<string, string> values, Action<string>? log = null)
    {
        if (values.Count == 0)
        {
            return;
        }

        try
        {
            var path = PathFor(manifest);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(values));
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin state could not be saved for {manifest.Id}: {exception.Message}");
        }
    }

    /// <summary>
    /// A file name that cannot escape the folder it belongs in.
    /// </summary>
    /// <remarks>
    /// An identifier comes from a file somebody downloaded. One containing <c>..\..\</c> would
    /// otherwise choose where the host writes, which is exactly the kind of thing a plugin format
    /// exists to make impossible.
    /// </remarks>
    private static string Safe(string id)
    {
        var safe = new string(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());

        return safe.Length == 0 ? "tool" : safe;
    }
}
