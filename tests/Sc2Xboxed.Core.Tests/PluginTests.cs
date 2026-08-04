using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class CursorSchemeParserTests
{
    private const string Inf = """
        [Version]
        signature="$CHICAGO$"

        [Scheme.Reg]
        HKCU,"Control Panel\Cursors","UpArrow",,"%25%\Cursors\Alternate.cur"
        HKCU,"Control Panel\Cursors","UpArrow",,"%25%\Cursors\Alternate_1.cur"
        HKCU,"Control Panel\Cursors","Arrow",,"%25%\Cursors\Normal.cur"
        HKCU,"Control Panel\Cursors","Cross",,"%25%\Cursors\Precision.cur"
        HKCU,"Control Panel\Cursors","Wait",,"%25%\Cursors\Wait_32-48-64.ani"
        ; Set scheme name
        HKCU,"Control Panel\Cursors","(Default)",,"Layan White Cursor"
        HKCU,"Control Panel\Cursors\Schemes","Layan White Cursor",,""
        """;

    private static readonly CursorScheme Scheme = CursorSchemeParser.Parse(Inf);

    [Fact]
    public void ReadsTheSchemeName() => Assert.Equal("Layan White Cursor", Scheme.Name);

    [Fact]
    public void KeepsOnlyTheFileName()
        => Assert.Equal("Normal.cur", Scheme.Roles["Arrow"]);

    // The pack lists two files for the same role and the INF installer applies them in order, so
    // the second overwrites the first. Taking the first would pick the wrong design.
    [Fact]
    public void LastEntryWinsWhenARoleIsListedTwice()
        => Assert.Equal("Alternate_1.cur", Scheme.Roles["UpArrow"]);

    // The pack writes "Cross"; Windows reads "Crosshair". Left uncorrected, the crosshair cursor
    // would silently stay the system one.
    [Fact]
    public void CorrectsTheCrosshairRoleName()
    {
        Assert.Equal("Precision.cur", Scheme.Roles["Crosshair"]);
        Assert.False(Scheme.Roles.ContainsKey("Cross"));
    }

    [Fact]
    public void AcceptsAnimatedCursors()
        => Assert.Equal("Wait_32-48-64.ani", Scheme.Roles["Wait"]);

    // The Schemes subkey registers the pack in the mouse control panel; it is not a cursor role and
    // must not become one.
    [Fact]
    public void IgnoresTheSchemesSubkey()
        => Assert.DoesNotContain(Scheme.Roles.Keys, k => k.Contains("Layan"));

    [Fact]
    public void DropsRolesWindowsDoesNotRead()
    {
        var scheme = CursorSchemeParser.Parse(
            """HKCU,"Control Panel\Cursors","Invente",,"%25%\Cursors\X.cur" """);
        Assert.Empty(scheme.Roles);
    }

    [Fact]
    public void CommentsAndBlankLinesAreSkipped()
        => Assert.Equal(4, Scheme.Roles.Count);
}

/// <summary>
/// The other pack format: RealWorld Designer schemes, which are plain INI.
/// </summary>
public class CrsSchemeParserTests
{
    // The real files open with a UTF-8 byte order mark, which lands as a character on the first
    // line: without stripping it the first section name never matches and the pack loses a cursor.
    private const string Crs = "﻿" + """
        [AppStarting]
        Path=glassbgbusy.ani
        [Wait]
        Path=glassbusy.ani
        [Arrow]
        Path=glassmain.cur
        [SizeNWSE]
        Path=glassdiag1.cur
        [Inconnu]
        Path=nowhere.cur
        """;

    private static readonly CursorScheme Scheme = CursorSchemeParser.ParseCrs(Crs);

    [Fact]
    public void ReadsSectionsAsRoles()
    {
        Assert.Equal("glassmain.cur", Scheme.Roles["Arrow"]);
        Assert.Equal("glassdiag1.cur", Scheme.Roles["SizeNWSE"]);
    }

    [Fact]
    public void SurvivesTheByteOrderMarkOnTheFirstSection()
        => Assert.Equal("glassbgbusy.ani", Scheme.Roles["AppStarting"]);

    [Fact]
    public void DropsRolesWindowsDoesNotRead()
        => Assert.False(Scheme.Roles.ContainsKey("Inconnu"));

    // A .crs carries no scheme name; the caller falls back to the plugin's own.
    [Fact]
    public void HasNoSchemeName() => Assert.Equal("", Scheme.Name);

    [Fact]
    public void LoadPicksTheParserFromTheExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"steamxbox-{Guid.NewGuid()}.crs");
        try
        {
            File.WriteAllText(path, "[Arrow]\nPath=a.cur");
            Assert.Equal("a.cur", CursorSchemeParser.Load(path).Roles["Arrow"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class PluginManifestTests
{
    private static PluginManifest Valid() => new()
    {
        Id = "x",
        Category = "theme.windows",
        Licence = "GPL-3.0",
        Revertible = true,
    };

    [Fact]
    public void AcceptsAWellFormedManifest() => Assert.Null(PluginCatalog.Validate(Valid()));

    // The rule the whole contract exists for.
    [Fact]
    public void RefusesAWindowsThemeThatCannotBeUndone()
    {
        var manifest = Valid();
        manifest.Revertible = false;
        Assert.Contains("revertible", PluginCatalog.Validate(manifest));
    }

    [Fact]
    public void ASurfaceThemeNeedsNoRevertFlag()
    {
        var manifest = Valid();
        manifest.Category = "theme.surface";
        manifest.Revertible = false;
        Assert.Null(PluginCatalog.Validate(manifest));
    }

    [Fact]
    public void RefusesAMissingLicence()
    {
        var manifest = Valid();
        manifest.Licence = "";
        Assert.Contains("licence", PluginCatalog.Validate(manifest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("theme.system")]
    public void RefusesAnUnknownCategory(string category)
    {
        var manifest = Valid();
        manifest.Category = category;
        Assert.Contains("catégorie", PluginCatalog.Validate(manifest));
    }

    [Fact]
    public void RefusesAManifestWithoutAnId()
    {
        var manifest = Valid();
        manifest.Id = "";
        Assert.Contains("id", PluginCatalog.Validate(manifest));
    }

    [Fact]
    public void ScanningAMissingFolderIsNotAnError()
    {
        var scan = PluginCatalog.Scan(Path.Combine(Path.GetTempPath(), "steamxbox-absent-" + Guid.NewGuid()));
        Assert.Empty(scan.Loaded);
        Assert.Empty(scan.Rejected);
    }

    [Fact]
    public void ScanLoadsAValidPluginAndReportsABadOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "steamxbox-plugins-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "good"));
            File.WriteAllText(Path.Combine(root, "good", "plugin.json"),
                """{"id":"good","category":"tile","licence":"MIT"}""");

            Directory.CreateDirectory(Path.Combine(root, "bad"));
            File.WriteAllText(Path.Combine(root, "bad", "plugin.json"),
                """{"id":"bad","category":"theme.windows","licence":"MIT","revertible":false}""");

            // A folder with no manifest is not a plugin and not a fault: the library also holds the
            // README and whatever the user keeps there.
            Directory.CreateDirectory(Path.Combine(root, "notes"));

            var scan = PluginCatalog.Scan(root);
            Assert.Single(scan.Loaded);
            Assert.Equal("good", scan.Loaded[0].Id);
            Assert.Single(scan.Rejected);
            Assert.Contains("revertible", scan.Rejected[0].Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
