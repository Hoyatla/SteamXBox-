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

    /// <summary>Interface language. Defaults to following the Windows display language.</summary>
    [JsonPropertyName("language")]
    public Localization.AppLanguage Language { get; set; } = Localization.AppLanguage.System;

    /// <summary>
    /// Launch SteamXBox when Windows starts, via the per-user Run key. Distinct from
    /// <see cref="AutoStart"/>, which only starts the core once the controller is detected.
    /// </summary>
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Window size remembered per tab, indexed by tab order. Size only: the window position is left
    /// alone so it never reappears off-screen or jumps between monitors.
    /// </summary>
    [JsonPropertyName("tabSizes")]
    public TabSize[] TabSizes { get; set; } = [];

    public static string SettingsDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SteamXBox");

    public static string SettingsFilePath =>
        System.IO.Path.Combine(SettingsDirectory, "settings.json");
}

/// <summary>Remembered window size for one tab.</summary>
public sealed class TabSize
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonIgnore]
    public bool IsUsable => Width > 200 && Height > 200;
}
