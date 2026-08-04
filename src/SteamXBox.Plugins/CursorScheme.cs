namespace SteamXBox.Plugins;

/// <summary>A cursor pack: a scheme name and the file each cursor role points to.</summary>
/// <param name="Name">Scheme name, as it will appear in the Windows mouse settings.</param>
/// <param name="Roles">Windows cursor role to file name, both already normalised.</param>
public sealed record CursorScheme(string Name, IReadOnlyDictionary<string, string> Roles);

/// <summary>
/// Reads a cursor pack's <c>install.inf</c>.
/// </summary>
/// <remarks>
/// Only the <c>[Scheme.Reg]</c> section matters. Its <c>CopyFiles</c> counterpart installs the
/// cursors into <c>%WINDIR%\Cursors</c>, which needs administrator rights; SteamXBox does not do
/// that. The registry accepts an absolute path to anywhere, so the pack stays in its plugin folder
/// and the values point at it. No elevation, nothing written outside the user's own hive, and
/// uninstalling the plugin cannot leave orphaned files in the Windows directory.
/// </remarks>
public static class CursorSchemeParser
{
    /// <summary>
    /// Role names Windows actually reads under <c>HKCU\Control Panel\Cursors</c>.
    /// </summary>
    /// <remarks>
    /// Checked against a real machine rather than taken from documentation, because packs get this
    /// wrong: the Layan pack writes <c>Cross</c>, which Windows ignores — the value it reads is
    /// <c>Crosshair</c>. A role that is not in this list is dropped rather than written, since
    /// writing it would silently do nothing while looking like it had worked.
    /// </remarks>
    public static readonly IReadOnlySet<string> KnownRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AppStarting", "Arrow", "Crosshair", "Hand", "Help", "IBeam", "No", "NWPen",
        "Person", "Pin", "SizeAll", "SizeNESW", "SizeNS", "SizeNWSE", "SizeWE", "UpArrow", "Wait",
    };

    /// <summary>Spellings packs use that Windows does not, mapped to the name it reads.</summary>
    private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cross"] = "Crosshair",
    };

    /// <summary>Parses the contents of an <c>install.inf</c>.</summary>
    public static CursorScheme Parse(string inf)
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = string.Empty;

        foreach (var raw in inf.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || !line.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // HKCU,"Control Panel\Cursors","Arrow",,"%25%\Cursors\Normal.cur"
            var fields = SplitInfLine(line);
            if (fields.Count < 5)
            {
                continue;
            }

            var key = Unquote(fields[1]);
            var value = Unquote(fields[2]);
            var data = Unquote(fields[4]);

            if (!key.EndsWith(@"Control Panel\Cursors", StringComparison.OrdinalIgnoreCase))
            {
                // The Schemes subkey registers the pack in the mouse control panel. SteamXBox does
                // not need it: it applies the cursors directly and remembers how to undo that.
                continue;
            }

            if (value is "(Default)" or "(default)")
            {
                name = data;
                continue;
            }

            if (RoleAliases.TryGetValue(value, out var corrected))
            {
                value = corrected;
            }

            if (!KnownRoles.Contains(value) || data.Length == 0)
            {
                continue;
            }

            // Last one wins, which is what the INF installer itself does: this pack lists two files
            // for several roles — Alternate.cur then Alternate_1.cur for UpArrow — and the second
            // AddReg overwrites the first. Taking the first would silently pick the wrong design.
            roles[value] = Path.GetFileName(data.Replace('\\', '/'));
        }

        return new CursorScheme(name, roles);
    }

    /// <summary>
    /// Reads a pack's scheme file, in whichever of the two formats it uses.
    /// </summary>
    /// <remarks>
    /// Packs come in two shapes. Those built for the INF installer ship <c>install.inf</c>; those
    /// from RealWorld Designer ship a <c>.crs</c>, which is plain INI. Both describe the same thing,
    /// so both produce the same <see cref="CursorScheme"/> and everything downstream is unaware of
    /// the difference.
    /// </remarks>
    public static CursorScheme Load(string path)
        => Path.GetExtension(path).Equals(".crs", StringComparison.OrdinalIgnoreCase)
            ? ParseCrs(File.ReadAllText(path))
            : Parse(File.ReadAllText(path));

    /// <summary>
    /// Parses a RealWorld Designer <c>.crs</c> scheme.
    /// </summary>
    /// <remarks>
    /// Sections are the cursor roles and each holds a <c>Path=</c> line:
    /// <code>[Arrow]
    /// Path=glassmain.cur</code>
    /// These files already use the role names Windows reads, so unlike the INF packs there is no
    /// spelling to correct — but they are still filtered against the known list, because a scheme
    /// naming a role Windows ignores should be dropped rather than half-applied.
    /// </remarks>
    public static CursorScheme ParseCrs(string crs)
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;

        foreach (var raw in crs.Split('\n'))
        {
            // These files carry a UTF-8 byte order mark, which arrives as a character on the first
            // line and would otherwise make the first section name unmatchable.
            var line = raw.Trim().TrimStart('﻿').Trim();

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (RoleAliases.TryGetValue(section, out var corrected))
                {
                    section = corrected;
                }

                continue;
            }

            if (section.Length == 0 || !line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = line["Path=".Length..].Trim();
            if (file.Length > 0 && KnownRoles.Contains(section))
            {
                roles[section] = Path.GetFileName(file.Replace('\\', '/'));
            }
        }

        return new CursorScheme(string.Empty, roles);
    }

    /// <summary>
    /// Splits an INF line on commas that are not inside quotes.
    /// </summary>
    /// <remarks>
    /// A plain <c>Split(',')</c> would break on any path containing a comma. Rare, but the failure
    /// would be a cursor silently missing rather than an error.
    /// </remarks>
    private static List<string> SplitInfLine(string line)
    {
        var fields = new List<string>();
        var start = 0;
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == ',' && !quoted)
            {
                fields.Add(line[start..i]);
                start = i + 1;
            }
        }

        fields.Add(line[start..]);
        return fields;
    }

    private static string Unquote(string field)
    {
        var t = field.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }
}
