using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sc2Xboxed.Osk;

/// <summary>
/// Colours and type face used to draw the overlay keyboard and the daisywheel.
/// </summary>
/// <remarks>
/// These were literals scattered through <see cref="OverlayForm"/>, which made the overlay the one
/// surface that could not follow the rest of the interface. Defaults reproduce the original look
/// exactly, so an installation without a palette file is unchanged.
///
/// Values are "#AARRGGBB" or "#RRGGBB". A colour that fails to parse falls back to its default
/// rather than throwing: a bad palette must never stop the overlay from drawing.
/// </remarks>
public sealed class OverlayPalette
{
    public const string FileName = "skin.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // ---- Keyboard ----
    public string KeyFill { get; set; } = "#80202030";
    public string KeyBorder { get; set; } = "#90808090";
    public string KeyHighlight { get; set; } = "#C04080FF";
    public string KeyFlash { get; set; } = "#E0FFFFFF";
    public string KeyText { get; set; } = "#FFFFFFFF";
    public string ShiftText { get; set; } = "#90CCCCCC";
    public string SymbolText { get; set; } = "#C0FFCC44";
    public string SymbolHighlightText { get; set; } = "#FF181818";

    // ---- Daisywheel ----
    public string PetalFill { get; set; } = "#B0181824";
    public string PetalActiveFill { get; set; } = "#D8203860";
    public string PetalBorder { get; set; } = "#70808090";
    public string PetalActiveBorder { get; set; } = "#FF4080FF";
    public string HubFill { get; set; } = "#C0101018";
    public string PetalFlash { get; set; } = "#F0FFFFFF";
    public string PetalDimText { get; set; } = "#A0B0B0C0";

    /// <summary>Face buttons, in A B X Y order.</summary>
    public string SlotA { get; set; } = "#FF5CC05C";
    public string SlotB { get; set; } = "#FFE05C5C";
    public string SlotX { get; set; } = "#FF5C9CE0";
    public string SlotY { get; set; } = "#FFE0C85C";

    /// <summary>Type face for every overlay label. Falls back to Segoe UI when not installed.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonIgnore]
    public static OverlayPalette Current { get; private set; } = new();

    /// <summary>
    /// Loads <c>skin.json</c> from the application directory when present. Safe to call more than
    /// once; the last successful load wins.
    /// </summary>
    public static void Load(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, FileName);
            if (!File.Exists(path))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<OverlayPalette>(File.ReadAllText(path), JsonOptions);
            if (loaded is not null)
            {
                Current = loaded;
            }
        }
        catch
        {
            // Keep whatever palette is already in place.
        }
    }

    /// <summary>Parses a palette entry, falling back to <paramref name="fallback"/> when malformed.</summary>
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim().TrimStart('#');
        if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var packed))
        {
            return fallback;
        }

        return text.Length switch
        {
            8 => Color.FromArgb((int)packed),
            6 => Color.FromArgb(unchecked((int)(0xFF000000u | packed))),
            _ => fallback,
        };
    }

    public Color Colour(string? value, uint fallbackArgb)
        => Parse(value, Color.FromArgb(unchecked((int)fallbackArgb)));

    /// <summary>
    /// Builds a font from the palette's family, falling back to Segoe UI when the family is missing
    /// from the machine. GDI+ substitutes silently for some failures and throws for others, so the
    /// fallback has to be explicit.
    /// </summary>
    public Font CreateFont(float size, FontStyle style)
    {
        try
        {
            var font = new Font(FontFamily, size, style, GraphicsUnit.Pixel);
            if (string.Equals(font.Name, FontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }
        catch (ArgumentException)
        {
        }

        return new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
    }
}
