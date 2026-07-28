using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamXBox.Gui.Models;

public sealed class ProfileData
{
    [JsonIgnore]
    public bool IsActive { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Profile";

    [JsonPropertyName("switchButton")]
    public string SwitchButton { get; set; } = "quick-access";

    [JsonPropertyName("rightPadSensitivity")]
    public double RightPadSensitivity { get; set; } = 900.0;

    [JsonPropertyName("leftPadInvertVertical")]
    public bool LeftPadInvertVertical { get; set; } = false;

    [JsonPropertyName("rightPadInvertX")]
    public bool RightPadInvertX { get; set; } = false;

    [JsonPropertyName("rightPadInvertY")]
    public bool RightPadInvertY { get; set; } = true;

    [JsonPropertyName("stickDeadZone")]
    public double StickDeadZone { get; set; } = 0.5;

    [JsonPropertyName("xboxStickDeadZone")]
    public double XboxStickDeadZone { get; set; } = 0.08;

    [JsonPropertyName("motions")]
    public Dictionary<string, string> Motions { get; set; } = new()
    {
        ["RightPad"] = "Trackball",
        ["LeftPad"] = "Scroll",
        ["LeftStick"] = "ArrowKeys",
    };

    [JsonPropertyName("buttons")]
    public Dictionary<string, string> Buttons { get; set; } = new()
    {
        ["L4"] = "PrintScreen",
        ["R4"] = "Win+G",
        ["L5"] = "Win+R",
        ["R5"] = "Alt+F4",
        ["A"] = "OSK Toggle",
        ["B"] = "OSK Toggle",
        ["L3"] = "Enter",
        ["Menu"] = "Win",
        ["View"] = "Win+D",
        ["DPadUp"] = "VolumeUp",
        ["DPadDown"] = "VolumeDown",
        ["DPadLeft"] = "Back",
        ["DPadRight"] = "Forward",
        ["LBumper"] = "Alt+Tab",
        ["RBumper"] = "Win+Tab",
    };

    public static string ProfilesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamXBox", "profiles");

    public string FilePath => Path.Combine(ProfilesDirectory, $"{Name}.json");

    public static ProfileData Load(string path)
    {
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
    }

    public void Save()
    {
        Directory.CreateDirectory(ProfilesDirectory);
        var json = System.Text.Json.JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    public static List<ProfileData> LoadAll()
    {
        var dir = ProfilesDirectory;
        if (!Directory.Exists(dir))
            return [new ProfileData()];

        var files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
            return [new ProfileData()];

        return files.Select(f => { try { return Load(f); } catch { return null; } })
                     .Where(p => p != null)
                     .Cast<ProfileData>()
                     .ToList();
    }

    public ProfileData Clone()
    {
        return new ProfileData
        {
            Name = Name,
            Mode = Mode,
            SwitchButton = SwitchButton,
            RightPadSensitivity = RightPadSensitivity,
            LeftPadInvertVertical = LeftPadInvertVertical,
            RightPadInvertX = RightPadInvertX,
            RightPadInvertY = RightPadInvertY,
            StickDeadZone = StickDeadZone,
            XboxStickDeadZone = XboxStickDeadZone,
            Motions = new Dictionary<string, string>(Motions),
            Buttons = new Dictionary<string, string>(Buttons),
        };
    }
}
