using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// A named Xbox360-mode button mapping, stored on disk like the Profile-tab profiles.
/// </summary>
/// <remarks>
/// Kept in Core rather than in the interface so the runtime reads exactly the file the editor wrote,
/// with no second model to drift from it.
/// </remarks>
public sealed class XboxProfile
{
    public const string DefaultName = "Default";

    public string Name { get; set; } = DefaultName;

    /// <summary>Physical button name to Xbox 360 button name.</summary>
    public Dictionary<string, string> Buttons { get; set; } = [];

    /// <summary>Sticks, triggers and vibration.</summary>
    public XboxTuning Tuning { get; set; } = new();

    [JsonIgnore]
    public XboxButtonMap Map => XboxButtonMap.FromDictionary(Buttons);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "xbox-profiles");

    public static string PathFor(string name) => Path.Combine(Directory, name + ".json");

    public static XboxProfile CreateDefault() => new()
    {
        Name = DefaultName,
        Buttons = XboxButtonMap.Default.ToDictionary(),
    };

    /// <summary>Loads a profile by name, falling back to the built-in default.</summary>
    public static XboxProfile Load(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateDefault();
        }

        try
        {
            var path = PathFor(name);
            if (!File.Exists(path))
            {
                return CreateDefault();
            }

            var loaded = JsonSerializer.Deserialize<XboxProfile>(File.ReadAllText(path), JsonOptions);
            if (loaded is null)
            {
                return CreateDefault();
            }

            loaded.Name = name;
            return loaded;
        }
        catch
        {
            // A corrupt file must not leave the controller unmapped.
            return CreateDefault();
        }
    }

    public static List<XboxProfile> LoadAll()
    {
        var profiles = new List<XboxProfile>();

        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    profiles.Add(Load(name));
                }
            }
        }
        catch
        {
        }

        if (!profiles.Any(p => p.Name == DefaultName))
        {
            profiles.Insert(0, CreateDefault());
        }

        return profiles;
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(Name), JsonSerializer.Serialize(this, JsonOptions));
    }

    public void Delete()
    {
        try
        {
            var path = PathFor(Name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
