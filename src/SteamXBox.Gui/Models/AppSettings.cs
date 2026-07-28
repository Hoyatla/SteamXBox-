using System.Text.Json.Serialization;

namespace SteamXBox.Gui.Models;

public sealed class AppSettings
{
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("devicePollInterval")]
    public int DevicePollIntervalMs { get; set; } = 3000;

    [JsonPropertyName("lastActiveProfile")]
    public string LastActiveProfile { get; set; } = "Default";

    public static string SettingsDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SteamXBox");

    public static string SettingsFilePath =>
        System.IO.Path.Combine(SettingsDirectory, "settings.json");
}
