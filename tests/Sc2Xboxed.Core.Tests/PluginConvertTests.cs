using System.Text.Json;
using SteamXBox.Plugins;
using SteamXBox.Tools.Documents;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The first action of the second level: a tool that makes a document become another document.
/// </summary>
/// <remarks>
/// It passes the contract's own test — the host already converts documents, since the indexer has
/// read the old Office formats through LibreOffice for weeks. So the property that makes a
/// downloaded manifest safe to install survives: it still does no more than what it shows to
/// whoever reads it.
/// </remarks>
public class PluginConvertTests
{
    private static PluginManifest Panel(params PluginContentItem[] content) => new()
    {
        Id = "convertir", Name = "Convertir", Category = "tool", Version = "1.0.0",
        Licence = "Proprietary", Glyph = "E8B5", Surface = "panel",
        Content = [.. content],
    };

    [Fact]
    public void ConvertIsPartOfTheVocabulary()
        => Assert.Contains(PluginActions.Convert, PluginActions.Known);

    // Converting acts on a document the user designated, and a tile has no panel to designate one
    // in. A tile naming it would be a button that could never do anything.
    [Fact]
    public void ATileCannotNameAnActionThatNeedsAPanel()
    {
        var tile = new PluginManifest
        {
            Id = "x", Name = "X", Category = "tool", Version = "1.0.0",
            Licence = "MIT", Glyph = "E8B5", Surface = "tile",
            Does = PluginActions.Convert, Target = "a|docx",
        };

        Assert.Contains("panneau", PluginCatalog.Validate(tile)!, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileElementIsAccepted()
        => Assert.Null(PluginCatalog.Validate(Panel(
            new PluginContentItem { Kind = "file", Id = "document", Label = "Document" },
            new PluginContentItem { Kind = "action", Does = PluginActions.Convert, Target = "{document}|docx" })));

    // Without an identifier there is nothing for the action to refer to.
    [Fact]
    public void AFileElementWithoutAnIdentifierIsRefused()
        => Assert.Contains(
            "id",
            PluginCatalog.Validate(Panel(new PluginContentItem { Kind = "file", Label = "Document" }))!,
            StringComparison.Ordinal);

    [Fact]
    public void ConvertNeedsATarget()
        => Assert.Contains(
            "target",
            PluginCatalog.Validate(Panel(
                new PluginContentItem { Kind = "action", Id = "go", Does = PluginActions.Convert }))!,
            StringComparison.Ordinal);

    /// <summary>The tool shipped with the product has to satisfy its own contract.</summary>
    [Fact]
    public void TheShippedConverterIsValid()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            """
            {
              "id": "convertir-document", "name": "Convertir un document", "category": "tool",
              "version": "1.0.0", "licence": "Proprietary", "glyph": "E8B5",
              "surface": "panel", "remembers": ["format"],
              "content": [
                { "kind": "file", "id": "document", "label": "Document", "options": ["pdf", "docx"] },
                { "kind": "choice", "id": "format", "label": "Convertir en", "value": "docx",
                  "options": ["docx", "pdf"] },
                { "kind": "action", "label": "Convertir", "does": "convert", "target": "{document}|{format}" }
              ]
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Null(PluginCatalog.Validate(manifest!));
    }
}

public class LibreOfficeTests
{
    // Measured, not guessed, and two of them are traps: a spreadsheet needs the filter's full name
    // because the short alias exits without error and writes nothing at all.
    [Theory]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("odt")]
    public void TheFormatsAToolMayAskForAreKnown(string format)
        => Assert.True(LibreOffice.Formats.ContainsKey(format));

    [Fact]
    public void TheSpreadsheetFilterCarriesItsFullName()
        => Assert.Contains("StarCalc", LibreOffice.Formats["csv"], StringComparison.Ordinal);

    [Fact]
    public void AnUnknownFormatIsRefusedRatherThanAttempted()
        => Assert.Equal(
            "Format inconnu : wordperfect.",
            LibreOffice.Convert("x.pdf", "wordperfect", Path.GetTempPath()).Problem);

    // Absent LibreOffice, the feature says so. That is a thinner product, not a broken one.
    [Fact]
    public void AMissingDocumentIsReportedRatherThanThrowing()
    {
        var result = LibreOffice.Convert(
            Path.Combine(Path.GetTempPath(), "ce-fichier-n-existe-pas.pdf"), "docx", Path.GetTempPath());

        Assert.False(result.Worked);
        Assert.NotEqual("", result.Problem);
    }
}
