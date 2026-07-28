using System.IO;
using System.Text.Json;
using SteamXBox.Gui.Models;

namespace SteamXBox.Gui.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppSettings.SettingsFilePath))
            {
                var json = File.ReadAllText(AppSettings.SettingsFilePath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(AppSettings.SettingsFilePath, json);
        }
        catch { }
    }
}
