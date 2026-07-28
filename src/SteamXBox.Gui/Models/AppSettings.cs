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

    [JsonPropertyName("tabWindowStates")]
    public TabWindowState[] TabWindowStates { get; set; } = new TabWindowState[6];

    public static string SettingsDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SteamXBox");

    public static string SettingsFilePath =>
        System.IO.Path.Combine(SettingsDirectory, "settings.json");
}

public sealed class TabWindowState
{
    [JsonPropertyName("width")]
    public double Width { get; set; } = 900;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 620;

    [JsonPropertyName("left")]
    public double Left { get; set; } = double.NaN;

    [JsonPropertyName("top")]
    public double Top { get; set; } = double.NaN;

    [JsonPropertyName("windowState")]
    public string WindowState { get; set; } = "Normal";
}
