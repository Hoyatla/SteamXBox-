using SenSÉ.Tools.Documents;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Which route a conversion takes, now that none of them is LibreOffice.
/// </summary>
/// <remarks>
/// LibreOffice opens a PDF in Draw, so every one of its PDF conversions is a drawing of the page
/// rather than the page's contents — ten thousand floating objects for four hundred paragraphs,
/// measured. Each target therefore has a route written here instead.
///
/// <para>
/// The rule that matters is that the free format and the proprietary one go the same way. A customer
/// choosing OpenDocument because it costs nothing must not be the one who gets the unusable file.
/// </para>
/// </remarks>
public class ConversionRouteTests
{
    // Prose keeps the words and drops the layout; a slide keeps both, because a slide is already a
    // rectangle of positioned boxes.
    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    [InlineData("rtf")]
    [InlineData("html")]
    [InlineData("txt")]
    public void EveryProseFormatTakesTheDocumentRoute(string format)
        => Assert.True(PdfToDocument.Handles("rapport.pdf", format));

    [Theory]
    [InlineData("pptx")]
    [InlineData("odp")]
    public void EveryPresentationFormatTakesTheSlideRoute(string format)
        => Assert.True(PdfToPresentation.Handles("rapport.pdf", format));

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    [InlineData("ods")]
    public void EverySheetFormatTakesTheTableRoute(string format)
        => Assert.True(PdfToTable.Handles("rapport.pdf", format));

    // The pairs that must not diverge. Each is a proprietary format and its free equivalent, and if
    // one of them ever falls through to LibreOffice this is the test that says so.
    [Theory]
    [InlineData("docx", "odt")]
    [InlineData("pptx", "odp")]
    [InlineData("xlsx", "ods")]
    public void AFreeFormatIsNeverTreatedWorseThanItsPaidEquivalent(string paid, string free)
        => Assert.Equal(Routed("rapport.pdf", paid), Routed("rapport.pdf", free));

    // No conversion may be claimed twice, or which route runs depends on the order they are tried.
    [Theory]
    [InlineData("rapport.pdf", "docx")]
    [InlineData("rapport.pdf", "odt")]
    [InlineData("rapport.pdf", "rtf")]
    [InlineData("rapport.pdf", "html")]
    [InlineData("rapport.pdf", "txt")]
    [InlineData("rapport.pdf", "pptx")]
    [InlineData("rapport.pdf", "odp")]
    [InlineData("rapport.pdf", "csv")]
    [InlineData("rapport.pdf", "xlsx")]
    [InlineData("rapport.pdf", "ods")]
    [InlineData("message.eml", "docx")]
    public void NoConversionIsClaimedTwice(string input, string format)
    {
        var claims = new[]
        {
            PdfToPresentation.Handles(input, format),
            PdfToDocument.Handles(input, format),
            PdfToTable.Handles(input, format),
            MailToDocument.Handles(input, format),
        };

        Assert.Equal(1, claims.Count(claimed => claimed));
    }

    // A document that is not a PDF still belongs to LibreOffice: converting a spreadsheet to a
    // document is a job it does properly, and nothing here replaces that.
    [Theory]
    [InlineData("feuille.xlsx", "csv")]
    [InlineData("note.docx", "odt")]
    [InlineData("presentation.pptx", "pdf")]
    public void EverythingThatIsNotAPdfIsLeftAlone(string input, string format)
        => Assert.False(Routed(input, format));

    private static bool Routed(string input, string format)
        => PdfToPresentation.Handles(input, format)
           || PdfToDocument.Handles(input, format)
           || PdfToTable.Handles(input, format);
}

/// <summary>
/// How an extracted row is written out as comma-separated values.
/// </summary>
/// <remarks>
/// The extraction guesses at columns; the writing of them must not guess at anything. A cell holding
/// a comma written plain silently becomes two cells, and nothing downstream can tell it happened —
/// which in a spreadsheet of amounts is a wrong number rather than a visible mistake.
/// </remarks>
public class CsvWritingTests
{
    [Fact]
    public void OrdinaryCellsAreWrittenPlain()
        => Assert.Equal("a,b,c", PdfToTable.Csv(["a", "b", "c"]));

    [Fact]
    public void ACellHoldingASeparatorIsQuoted()
        => Assert.Equal("\"1,50\",kg", PdfToTable.Csv(["1,50", "kg"]));

    [Fact]
    public void AQuotationMarkIsDoubledInside()
        => Assert.Equal("\"il a dit \"\"non\"\"\"", PdfToTable.Csv(["il a dit \"non\""]));

    [Fact]
    public void ALineBreakInsideACellIsQuoted()
        => Assert.Equal("\"deux\nlignes\"", PdfToTable.Csv(["deux\nlignes"]));

    // An empty cell is a column that exists and holds nothing, which is not the same as a column
    // that is not there.
    [Fact]
    public void EmptyCellsKeepTheirPlace()
        => Assert.Equal("a,,c", PdfToTable.Csv(["a", "", "c"]));
}
