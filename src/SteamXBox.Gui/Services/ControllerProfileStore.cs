using System.IO;
using System.Text.Json;
using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Core.Input;

namespace SteamXBox.Gui.Services;

/// <summary>
/// Where the controller-to-profile assignments live.
/// </summary>
/// <remarks>
/// A file of its own rather than a field in the settings. The settings file is rewritten in full
/// from an in-memory copy whenever any tab saves, and this is written from a different screen at a
/// different time — the same collision that once made picking a profile silently revert the
/// "start with Windows" tick.
///
/// Which assignments are worth saving is not decided here. <see cref="ControllerProfileBook"/> owns
/// that rule, because it is the part that can go wrong quietly: an XInput slot names the order
/// somebody switched their controllers on in, and restoring one tomorrow puts a player's settings
/// on somebody else's pad.
/// </remarks>
public sealed class ControllerProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public ControllerProfileStore()
        : this(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamXBox",
            "controller-profiles.json"))
    {
    }

    public ControllerProfileStore(string path) => _path = path;

    public string Path => _path;

    /// <summary>Reads the saved assignments, returning an empty book if there are none.</summary>
    public ControllerProfileBook Load(string defaultProfile)
    {
        var book = new ControllerProfileBook(defaultProfile);

        try
        {
            if (File.Exists(_path))
            {
                book.Load(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)));
            }
        }
        catch (Exception ex)
        {
            // A corrupt file means starting from the defaults, not refusing to open the window.
            UiLog.Failure($"reading {_path}", ex);
        }

        return book;
    }

    public void Save(ControllerProfileBook book)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(book.Persistable(), Json));
        }
        catch (Exception ex)
        {
            UiLog.Failure($"writing {_path}", ex);
        }
    }
}
