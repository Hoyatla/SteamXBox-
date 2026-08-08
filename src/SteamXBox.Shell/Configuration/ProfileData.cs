using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sc2Xboxed.Core.Mapping;

namespace SteamXBox.Shell.Configuration;

public sealed class ProfileData
{

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Profile";

    /// <summary>
    /// Which controller family the profile was written for — "Steam", "DualSense" or "XInput".
    /// Empty for Default and for profiles an older build created without a family.
    /// </summary>
    /// <remarks>
    /// Read by the GUI to file each profile under its family's tab. The runtime never reads it:
    /// <c>ProfileMapper</c> picks its keys by name, so the extra field is inert there.
    /// </remarks>
    [JsonPropertyName("family")]
    public string Family { get; set; } = "";

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

    [JsonPropertyName("xboxStickCurve")]
    public double XboxStickCurve { get; set; } = 1.0;

    [JsonPropertyName("xboxStickSensitivity")]
    public double XboxStickSensitivity { get; set; } = 1.0;

    [JsonPropertyName("xboxTriggerThreshold")]
    public double XboxTriggerThreshold { get; set; } = 0.0;

    [JsonPropertyName("xboxTriggerFullPoint")]
    public double XboxTriggerFullPoint { get; set; } = 1.0;

    [JsonPropertyName("xboxVibrationEnabled")]
    public bool XboxVibrationEnabled { get; set; } = true;

    [JsonPropertyName("xboxVibrationIntensity")]
    public double XboxVibrationIntensity { get; set; } = 1.0;

    [JsonPropertyName("xboxHapticForwarding")]
    public bool XboxHapticForwarding { get; set; } = false;

    [JsonPropertyName("xboxTriggerHapticsEnabled")]
    public bool XboxTriggerHapticsEnabled { get; set; } = false;

    [JsonPropertyName("xboxTriggerHapticStrength")]
    public double XboxTriggerHapticStrength { get; set; } = 0.6;

    [JsonPropertyName("xboxTriggerActuatorIndex")]
    public int XboxTriggerActuatorIndex { get; set; } = 2;

    // ---- Xbox360 mode ----
    //
    // The gamepad layout used to live in separate files under "xbox-profiles". It now travels with
    // the desktop profile so one save in the Profile tab moves both. The keys are the same names the
    // old Xbox tab wrote, so profiles written before the merge read back untouched.

    /// <summary>Physical button name to Xbox 360 button name.</summary>
    [JsonPropertyName("xboxButtons")]
    public Dictionary<string, string> XboxButtons { get; set; } = new(XboxButtonMap.Default.ToDictionary());

    [JsonIgnore]
    public XboxButtonMap XboxMap => XboxButtonMap.FromDictionary(XboxButtons);

    public void ApplyXboxMap(XboxButtonMap map) => XboxButtons = map.ToDictionary();

    [JsonPropertyName("motions")]
    public Dictionary<string, string> Motions { get; set; } = new()
    {
        ["RightPad"] = "Trackball",
        ["LeftPad"] = "Scroll",
        ["LeftStick"] = "ArrowKeys",
        ["RightStick"] = "Souris",
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
        ["X"] = "Alt+←",
        ["Y"] = "Alt+→",
        ["L3"] = "Enter",
        ["R3"] = "Aucun",
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
        var data = System.Text.Json.JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
        MigrateLegacyXboxSection(data, json);
        return data;
    }

    /// <summary>
    /// Profiles written before the merge kept the Xbox layout in separate files under
    /// "xbox-profiles". Merging them into the profile means one file carries the whole pad, so a
    /// legacy profile is migrated here, once, by copying the stored default Xbox layout into its
    /// own section. The runtime performs the same migration when it loads a profile, so both sides
    /// always read the same layout.
    /// </summary>
    private static void MigrateLegacyXboxSection(ProfileData data, string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("xboxButtons", out var section) && section.ValueKind == JsonValueKind.Object)
        {
            return;
        }

        var legacy = XboxProfile.Load(XboxProfile.DefaultName);
        data.XboxButtons = legacy.Buttons;
        data.XboxStickDeadZone = legacy.Tuning.StickDeadZone;
        data.XboxStickCurve = legacy.Tuning.StickCurve;
        data.XboxStickSensitivity = legacy.Tuning.StickSensitivity;
        data.XboxTriggerThreshold = legacy.Tuning.TriggerThreshold;
        data.XboxTriggerFullPoint = legacy.Tuning.TriggerFullPoint;
        data.XboxVibrationEnabled = legacy.Tuning.VibrationEnabled;
        data.XboxVibrationIntensity = legacy.Tuning.VibrationIntensity;
        data.XboxHapticForwarding = legacy.Tuning.HapticForwarding;
        data.XboxTriggerHapticsEnabled = legacy.Tuning.TriggerHapticsEnabled;
        data.XboxTriggerHapticStrength = legacy.Tuning.TriggerHapticStrength;
        data.XboxTriggerActuatorIndex = legacy.Tuning.TriggerActuatorIndex;
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
            Family = Family,
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
            XboxStickCurve = XboxStickCurve,
            XboxStickSensitivity = XboxStickSensitivity,
            XboxTriggerThreshold = XboxTriggerThreshold,
            XboxTriggerFullPoint = XboxTriggerFullPoint,
            XboxVibrationEnabled = XboxVibrationEnabled,
            XboxVibrationIntensity = XboxVibrationIntensity,
            XboxHapticForwarding = XboxHapticForwarding,
            XboxTriggerHapticsEnabled = XboxTriggerHapticsEnabled,
            XboxTriggerHapticStrength = XboxTriggerHapticStrength,
            XboxTriggerActuatorIndex = XboxTriggerActuatorIndex,
            XboxButtons = new Dictionary<string, string>(XboxButtons),
            Motions = new Dictionary<string, string>(Motions),
            Buttons = new Dictionary<string, string>(Buttons),
        };
    }
}
