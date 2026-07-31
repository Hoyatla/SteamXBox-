using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamXBox.Gui.Models;

public sealed class ProfileData
{

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Profile";

    [JsonPropertyName("switchButton")]
    public string SwitchButton { get; set; } = "quick-access";

    [JsonPropertyName("rightPadSensitivity")]
    public double RightPadSensitivity { get; set; } = 380.0;

    /// <summary>
    /// Wheel units per pad unit for left pad scrolling. A full pad swipe spans 2.0 units, so 10
    /// gives about 20 notches per swipe. The old 600 default was roughly 60x too fast.
    /// </summary>
    [JsonPropertyName("leftPadSensitivity")]
    public double LeftPadSensitivity { get; set; } = 4.8;

    [JsonPropertyName("leftPadDeadZone")]
    public double LeftPadDeadZone { get; set; } = 0.002;

    [JsonPropertyName("rightPadDeadZone")]
    public double RightPadDeadZone { get; set; } = 0.00015;

    [JsonPropertyName("leftPadInvertVertical")]
    public bool LeftPadInvertVertical { get; set; } = true;

    [JsonPropertyName("rightPadInvertX")]
    public bool RightPadInvertX { get; set; } = false;

    [JsonPropertyName("rightPadInvertY")]
    public bool RightPadInvertY { get; set; } = true;

    // ---- Motion behaviour ----

    /// <summary>Acceleration exponent for the right pad. 1.0 is linear, i.e. disabled.</summary>
    [JsonPropertyName("rightPadAcceleration")]
    public double RightPadAcceleration { get; set; } = 2.0;

    /// <summary>Acceleration exponent for left pad scrolling. 1.0 is linear.</summary>
    [JsonPropertyName("leftPadAcceleration")]
    public double LeftPadAcceleration { get; set; } = 1.5;

    /// <summary>Cursor speed while the finger rests at the pad edge, in pixels per second. 0 is off.</summary>
    [JsonPropertyName("rightPadEdgeSpeed")]
    public double RightPadEdgeSpeed { get; set; } = 750.0;

    /// <summary>Gain floor for slow gestures. 0.25 means four times the pointing precision.</summary>
    [JsonPropertyName("finePrecision")]
    public double FinePrecision { get; set; } = 0.10;


    /// <summary>Travel a gesture needs, in pixels, before releasing it may throw the cursor.</summary>
    [JsonPropertyName("minThrowTravel")]
    public double MinThrowTravel { get; set; } = 70.0;



    /// <summary>Inertia decay per second. Lower glides longer.</summary>
    [JsonPropertyName("rightPadInertia")]
    public double RightPadInertia { get; set; } = 2.0;

    [JsonPropertyName("leftPadInertia")]
    public double LeftPadInertia { get; set; } = 2.0;

    // ---- Per-pad haptics ----
    // Force and rate of the vibration only. Motion output is unaffected by these.

    /// <summary>Left pad vibration strength, 0-1. 0 disables it.</summary>
    [JsonPropertyName("leftPadHapticForce")]
    public double LeftPadHapticForce { get; set; } = 0.5;

    /// <summary>Left pad vibration rate, 0-1. Higher means pulses closer together.</summary>
    [JsonPropertyName("leftPadHapticFrequency")]
    public double LeftPadHapticFrequency { get; set; } = 0.5;

    /// <summary>Right pad vibration strength, 0-1. 0 disables it.</summary>
    [JsonPropertyName("rightPadHapticForce")]
    public double RightPadHapticForce { get; set; } = 0.5;

    /// <summary>Right pad vibration rate, 0-1. Higher means pulses closer together.</summary>
    [JsonPropertyName("rightPadHapticFrequency")]
    public double RightPadHapticFrequency { get; set; } = 0.5;

    [JsonPropertyName("leftPadHorizontalScroll")]
    public bool LeftPadHorizontalScroll { get; set; }

    [JsonPropertyName("stickDeadZone")]
    public double StickDeadZone { get; set; } = 0.06;

    [JsonPropertyName("xboxStickDeadZone")]
    public double XboxStickDeadZone { get; set; } = 0.018;

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
            LeftPadSensitivity = LeftPadSensitivity,
            LeftPadDeadZone = LeftPadDeadZone,
            RightPadDeadZone = RightPadDeadZone,
            LeftPadInvertVertical = LeftPadInvertVertical,
            RightPadInvertX = RightPadInvertX,
            RightPadInvertY = RightPadInvertY,
            RightPadAcceleration = RightPadAcceleration,
            LeftPadAcceleration = LeftPadAcceleration,
            RightPadEdgeSpeed = RightPadEdgeSpeed,
            FinePrecision = FinePrecision,
            MinThrowTravel = MinThrowTravel,
            RightPadInertia = RightPadInertia,
            LeftPadInertia = LeftPadInertia,
            LeftPadHapticForce = LeftPadHapticForce,
            LeftPadHapticFrequency = LeftPadHapticFrequency,
            RightPadHapticForce = RightPadHapticForce,
            RightPadHapticFrequency = RightPadHapticFrequency,
            LeftPadHorizontalScroll = LeftPadHorizontalScroll,
            StickDeadZone = StickDeadZone,
            XboxStickDeadZone = XboxStickDeadZone,
            Motions = new Dictionary<string, string>(Motions),
            Buttons = new Dictionary<string, string>(Buttons),
        };
    }
}
