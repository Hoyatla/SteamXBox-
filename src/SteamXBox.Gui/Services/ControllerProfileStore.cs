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

    /// <summary>
    /// Where the user-given controller names live.
    /// </summary>
    /// <remarks>
    /// A third file rather than a third field in the first, for the same reason as the Xbox
    /// assignments: it is written from a different screen at a different time, and one screen's save
    /// must not rewrite another's work. Names are cosmetic — logs and the strip keep calling the pad
    /// what the user chose — so the rules are lighter than for profiles: nothing durable is decided
    /// here, the caller owns the dictionary.
    /// </remarks>
    private string NamesPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(_path)!, "controller-names.json");

    /// <summary>Reads the saved controller names.</summary>
    public Dictionary<string, string> LoadNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(NamesPath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(NamesPath));
                if (saved is not null)
                {
                    foreach (var (id, name) in saved)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names[id] = name;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure($"reading {NamesPath}", ex);
        }

        return names;
    }

    public void SaveNames(Dictionary<string, string> names)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NamesPath)!);
            File.WriteAllText(NamesPath, JsonSerializer.Serialize(names, Json));
        }
        catch (Exception ex)
        {
            UiLog.Failure($"writing {NamesPath}", ex);
        }
    }

    /// <summary>Where the remembered controller numbers live.</summary>
    private string SlotsPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(_path)!, "controller-slots.json");

    /// <summary>Reads the remembered numbers and forgets the ones nobody has used for months.</summary>
    /// <remarks>
    /// The cleanup happens on the way in rather than on a schedule: this is read once per session,
    /// which is exactly the cadence the pruning wants, and a background job for a file of five lines
    /// would be more machinery than the problem deserves.
    ///
    /// Ninety days is chosen to be far longer than any holiday: a controller nobody has touched for
    /// a season is a guest's, and holding its number pushes the household's numbering upwards for
    /// ever.
    /// </remarks>
    public ControllerSlotBook LoadSlots(DateTimeOffset now)
    {
        var book = new ControllerSlotBook();

        try
        {
            if (File.Exists(SlotsPath))
            {
                book.Load(JsonSerializer.Deserialize<Dictionary<string, ControllerSlot>>(
                    File.ReadAllText(SlotsPath)));

                var dropped = book.DropUnseenSince(now.AddDays(-90));
                if (dropped > 0)
                {
                    UiLog.Info($"{dropped} controller slot(s) forgotten after 90 days unseen");
                }
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure($"reading {SlotsPath}", ex);
        }

        return book;
    }

    public void SaveSlots(ControllerSlotBook book)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SlotsPath)!);
            File.WriteAllText(SlotsPath, JsonSerializer.Serialize(book.Persistable(), Json));
        }
        catch (Exception ex)
        {
            UiLog.Failure($"writing {SlotsPath}", ex);
        }
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
