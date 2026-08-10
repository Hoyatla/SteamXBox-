using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SteamXBox.Desktop.Assets;

/// <summary>
/// SteamXBox's own icons, as WPF geometry.
/// </summary>
/// <remarks>
/// Taken from <c>Library/Icons/fluent</c>: MIT, commercial use allowed, and the closest of the ten
/// sets to the look of Windows. Its <c>LICENSE.txt</c> is embedded beside the icons so the
/// provenance travels with the binary instead of living only in a folder somebody may not ship.
///
/// <para>
/// Each file is a single <c>&lt;path&gt;</c> whose colour is <c>currentColor</c>, which is exactly
/// what makes them usable here: the shape comes from the file, the colour from the theme brush at
/// the point of use, so one icon serves a light theme and a dark one.
/// </para>
///
/// <para>
/// Geometries are frozen and cached. A frozen one can be shared across threads and across every row
/// of a list without WPF copying it, and the search results redraw on every keystroke.
/// </para>
/// </remarks>
public static class IconLibrary
{
    /// <summary>The names used by the rest of the environment. Add here, not as loose strings.</summary>
    public const string Document = "document-24-regular";
    public const string Folder = "folder-24-regular";
    public const string Application = "apps-24-regular";
    public const string Drive = "hard-drive-24-regular";
    public const string Code = "code-24-regular";

    /// <summary>Safely remove a volume.</summary>
    /// <remarks>
    /// Fluent has no eject symbol, and this is its word for the same act. Kept in the family rather
    /// than borrowing the classic triangle from another set: one glyph in a different hand is
    /// visible, and the strip is four items wide.
    /// </remarks>
    public const string Eject = "plug-disconnected-24-regular";

    /// <summary>A result from the web.</summary>
    public const string Web = "globe-24-regular";

    private static readonly Dictionary<string, Geometry?> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>The icon's outline, or null when it cannot be had.</summary>
    /// <remarks>
    /// Null rather than a placeholder or an exception. A missing icon must cost a blank space in one
    /// row, never a crash of the window that was drawing it, and never a wrong picture that would
    /// have to be recognised as wrong.
    /// </remarks>
    public static Geometry? Get(string name)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var geometry = Load(name);
            Cache[name] = geometry;

            return geometry;
        }
    }

    private static Geometry? Load(string name)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($".{name}.svg", StringComparison.OrdinalIgnoreCase));

            if (resource is null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var svg = reader.ReadToEnd();

            // The path data only. WPF's geometry mini-language accepts SVG path syntax for the
            // commands these icons use, so the attribute transfers as written — no conversion, and
            // nothing to drift when the set is updated.
            var match = Regex.Match(svg, @"\sd=""([^""]+)""", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return null;
            }

            var geometry = Geometry.Parse(match.Groups[1].Value);
            geometry.Freeze();

            return geometry;
        }
        catch (Exception exception) when (exception is IOException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
