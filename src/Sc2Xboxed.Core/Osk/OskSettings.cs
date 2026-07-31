using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sc2Xboxed.Core.Osk;

public enum OskTypingMode
{
    /// <summary>Full keyboard with one absolute cursor per trackpad. Click a pad to type.</summary>
    FullKeyboard,

    /// <summary>Eight petals of four characters; pad direction picks the petal, ABXY the character.</summary>
    Daisywheel,
}

/// <summary>
/// Overlay keyboard preferences. Global rather than per-profile: there is a single
/// osk-settings.json, shared by the core, the overlay process and the GUI.
/// </summary>
public sealed class OskSettings
{
    [JsonPropertyName("typingMode")]
    public OskTypingMode TypingMode { get; set; } = OskTypingMode.FullKeyboard;

    /// <summary>
    /// Tick each time the highlighted key changes, not only on keypress. This is what makes
    /// typing without looking at the overlay possible.
    /// </summary>
    [JsonPropertyName("hoverHaptics")]
    public bool HoverHaptics { get; set; } = true;

    /// <summary>Haptic strength, 0-100. 0 disables overlay haptics entirely.</summary>
    [JsonPropertyName("hapticIntensity")]
    public int HapticIntensity { get; set; } = 60;

    /// <summary>Emit the keypress when the pad click is released instead of when it is pressed.</summary>
    [JsonPropertyName("validateOnRelease")]
    public bool ValidateOnRelease { get; set; }

    /// <summary>Keypress vibration strength for the left pad, 0-100. 0 disables it.</summary>
    [JsonPropertyName("leftClickForce")]
    public int LeftClickForce { get; set; } = 35;

    /// <summary>Keypress vibration strength for the right pad, 0-100. 0 disables it.</summary>
    [JsonPropertyName("rightClickForce")]
    public int RightClickForce { get; set; } = 35;

    // Not implemented yet: opening the overlay automatically when a text field takes focus needs a
    // UI Automation focus watcher. Deliberately absent rather than exposed as a dead toggle.

    // ---- Derived values ----

    /// <summary>
    /// Exponential smoothing factor for the overlay cursor. A calibration constant rather than a
    /// preference: too low and the cursor lags behind the finger, too high and it jitters.
    /// </summary>
    [JsonIgnore]
    public double CursorSmoothing => 0.35;

    /// <summary>Keypress pulse width for the left pad, in microseconds. 0 when disabled.</summary>
    [JsonIgnore]
    public ushort LeftClickPulseUs =>
        LeftClickForce <= 0 ? (ushort)0 : (ushort)Lerp(80, 700, LeftClickForce / 100.0);

    /// <summary>Keypress pulse width for the right pad, in microseconds. 0 when disabled.</summary>
    [JsonIgnore]
    public ushort RightClickPulseUs =>
        RightClickForce <= 0 ? (ushort)0 : (ushort)Lerp(80, 700, RightClickForce / 100.0);

    /// <summary>Pulse width in microseconds for a hover tick, or 0 when haptics are off.</summary>
    [JsonIgnore]
    public ushort HoverPulseUs =>
        HapticIntensity <= 0 ? (ushort)0 : (ushort)Lerp(80, 260, HapticIntensity / 100.0);

    /// <summary>Keypress pulse width for the pad that was clicked. 0 disables that pad's click.</summary>
    public ushort ClickPulseUsFor(bool isLeftPad) => isLeftPad ? LeftClickPulseUs : RightClickPulseUs;

    [JsonIgnore]
    public bool HapticsEnabled => HapticIntensity > 0;

    private static double Lerp(double from, double to, double t) => from + (to - from) * Math.Clamp(t, 0.0, 1.0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamXBox");

    private static string FilePath => Path.Combine(DirectoryPath, "osk-settings.json");

    public static OskSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<OskSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Clamp();
                    return loaded;
                }
            }
        }
        catch { }
        return new OskSettings();
    }

    public void Save()
    {
        try
        {
            Clamp();
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }

    /// <summary>Guards against out-of-range values in a hand-edited or stale settings file.</summary>
    private void Clamp()
    {
        HapticIntensity = Math.Clamp(HapticIntensity, 0, 100);
        LeftClickForce = Math.Clamp(LeftClickForce, 0, 100);
        RightClickForce = Math.Clamp(RightClickForce, 0, 100);

        if (!Enum.IsDefined(TypingMode))
        {
            TypingMode = OskTypingMode.FullKeyboard;
        }
    }
}
