using SenSÉ.Tools.Documents;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Which conversions take the presentation route, and which the word-processor one.
/// </summary>
/// <remarks>
/// The two routes read the same PDF and make opposite decisions about its layout, so which one runs
/// is the whole difference between a document that opens instantly and one that does not. Both
/// presentation formats go the same way: leaving OpenDocument to LibreOffice would have sent it
/// back through the PDF import both routes exist to avoid.
/// </remarks>
public class PresentationFormatTests
{
    [Theory]
    [InlineData("rapport.pdf", "pptx", true)]
    [InlineData("rapport.pdf", "odp", true)]
    [InlineData("rapport.PDF", "ODP", true)]
    public void APdfBecomesAPresentationWithoutLibreOffice(string input, string format, bool expected)
        => Assert.Equal(expected, PdfToPresentation.Handles(input, format));

    // A word-processor target is the other route's business, and a document that is not a PDF is
    // nobody's here.
    [Theory]
    [InlineData("rapport.pdf", "docx")]
    [InlineData("rapport.pdf", "txt")]
    [InlineData("rapport.docx", "odp")]
    [InlineData("message.eml", "pptx")]
    public void EverythingElseGoesElsewhere(string input, string format)
        => Assert.False(PdfToPresentation.Handles(input, format));

    // The three routes must not both claim the same conversion, or which one runs depends on the
    // order they happen to be tried in.
    [Theory]
    [InlineData("rapport.pdf", "pptx")]
    [InlineData("rapport.pdf", "odp")]
    [InlineData("rapport.pdf", "docx")]
    [InlineData("message.eml", "docx")]
    public void NoConversionIsClaimedTwice(string input, string format)
    {
        var claims = new[]
        {
            PdfToPresentation.Handles(input, format),
            PdfToDocument.Handles(input, format),
            MailToDocument.Handles(input, format),
        };

        Assert.Equal(1, claims.Count(claimed => claimed));
    }
}
