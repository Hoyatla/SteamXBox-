using System.Text.Json;
using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The contract a declarative tool has to meet, checked where it is decided.
/// </summary>
/// <remarks>
/// These are the rules of <c>Plugins/README.md</c> turned into assertions. A plugin format's value
/// is that a user can install a tool found anywhere without reading its code; that only holds if the
/// refusals are real, so each one has a test.
/// </remarks>
public class PluginToolValidationTests
{
    private static PluginManifest Tool(Action<PluginManifest> adjust)
    {
        var manifest = new PluginManifest
        {
            Id = "minuteur",
            Name = "Minuteur",
            Category = "tool",
            Version = "1.0.0",
            Licence = "MIT",
            Glyph = "E916",
            Surface = "tile",
            Does = PluginActions.Search,
        };

        adjust(manifest);

        return manifest;
    }

    [Fact]
    public void AWellFormedToolIsAccepted()
        => Assert.Null(PluginCatalog.Validate(Tool(_ => { })));

    [Fact]
    public void ATileWithoutAnActionIsRefused()
        => Assert.Contains("does", PluginCatalog.Validate(Tool(m => m.Does = ""))!, StringComparison.Ordinal);

    // The line the contract draws: a tool may only name what the host already does for itself.
    [Fact]
    public void AnActionOutsideTheVocabularyIsRefused()
        => Assert.Contains(
            "action inconnue",
            PluginCatalog.Validate(Tool(m => m.Does = "evaluer-une-expression"))!,
            StringComparison.Ordinal);

    [Fact]
    public void AnActionThatNeedsATargetIsRefusedWithoutOne()
        => Assert.Contains(
            "target",
            PluginCatalog.Validate(Tool(m => { m.Does = PluginActions.Application; m.Target = ""; }))!,
            StringComparison.Ordinal);

    [Fact]
    public void AnActionThatNeedsNoTargetIsFineWithoutOne()
        => Assert.Null(PluginCatalog.Validate(Tool(m => m.Does = PluginActions.ClearScreen)));

    // A tile with no icon cannot be found by eye in a grid.
    [Fact]
    public void AToolWithoutAGlyphIsRefused()
        => Assert.Contains("glyph", PluginCatalog.Validate(Tool(m => m.Glyph = ""))!, StringComparison.Ordinal);

    [Fact]
    public void AnUnknownSurfaceIsRefused()
        => Assert.Contains(
            "surface inconnue",
            PluginCatalog.Validate(Tool(m => m.Surface = "fenetre"))!,
            StringComparison.Ordinal);

    [Fact]
    public void APanelWithNothingInItIsRefused()
        => Assert.Contains(
            "content",
            PluginCatalog.Validate(Tool(m => { m.Surface = "panel"; m.Does = ""; }))!,
            StringComparison.Ordinal);

    [Fact]
    public void AnUnknownContentElementIsRefused()
        => Assert.Contains(
            "élément inconnu",
            PluginCatalog.Validate(Tool(m =>
            {
                m.Surface = "panel";
                m.Does = "";
                m.Content = [new PluginContentItem { Kind = "canvas" }];
            }))!,
            StringComparison.Ordinal);

    [Fact]
    public void AChoiceWithoutOptionsIsRefused()
        => Assert.Contains(
            "sans options",
            PluginCatalog.Validate(Tool(m =>
            {
                m.Surface = "panel";
                m.Does = "";
                m.Content = [new PluginContentItem { Kind = "choice", Id = "x" }];
            }))!,
            StringComparison.Ordinal);

    [Fact]
    public void AnActionInsideAPanelIsCheckedToo()
        => Assert.Contains(
            "action inconnue",
            PluginCatalog.Validate(Tool(m =>
            {
                m.Surface = "panel";
                m.Does = "";
                m.Content = [new PluginContentItem { Kind = "action", Id = "go", Does = "lancer-une-fusee" }];
            }))!,
            StringComparison.Ordinal);

    // The rules that predate tools still apply to them.
    [Fact]
    public void AToolWithoutALicenceIsRefused()
        => Assert.Contains("licence", PluginCatalog.Validate(Tool(m => m.Licence = ""))!, StringComparison.Ordinal);
}

public class PluginToolScanTests
{
    private static string Folder(string manifest)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tool = Path.Combine(root, "essai");

        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, PluginCatalog.ManifestFileName), manifest);

        return root;
    }

    // A tool is a folder: dropped in, it is installed. Nothing is compiled and nothing is registered
    // anywhere else.
    [Fact]
    public void AFolderWithAManifestIsAToolWithoutAnythingElse()
    {
        var root = Folder("""
            {
              "id": "essai", "name": "Essai", "category": "tool", "version": "1.0.0",
              "licence": "MIT", "glyph": "E916", "surface": "tile",
              "does": "clear-screen"
            }
            """);

        try
        {
            var scan = PluginCatalog.Scan(root);

            Assert.Single(scan.Loaded);
            Assert.Empty(scan.Rejected);
            Assert.Equal(PluginCategory.Tool, scan.Loaded[0].Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Ignored with a reason, never an error that takes the environment or the other tools down.
    [Fact]
    public void AManifestThatWillNotParseIsRejectedRatherThanThrowing()
    {
        var root = Folder("{ ceci n'est pas du JSON");

        try
        {
            var scan = PluginCatalog.Scan(root);

            Assert.Empty(scan.Loaded);
            Assert.Single(scan.Rejected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AMissingFolderIsAnEmptyScanRatherThanAFailure()
    {
        var scan = PluginCatalog.Scan(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        Assert.Empty(scan.Loaded);
        Assert.Empty(scan.Rejected);
    }

    /// <summary>The tool shipped with the product has to satisfy its own contract.</summary>
    [Fact]
    public void TheShippedExampleIsValid()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            """
            {
              "id": "raccourcis-windows", "name": "Raccourcis Windows", "category": "tool",
              "version": "1.0.0", "licence": "Proprietary", "glyph": "E713",
              "surface": "panel", "remembers": ["panneau"],
              "content": [
                { "kind": "choice", "id": "panneau", "label": "Panneau", "value": "bluetooth",
                  "options": ["bluetooth", "display"] },
                { "kind": "action", "label": "Ouvrir", "does": "windows-setting", "target": "{panneau}" }
              ]
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Null(PluginCatalog.Validate(manifest!));
    }
}
