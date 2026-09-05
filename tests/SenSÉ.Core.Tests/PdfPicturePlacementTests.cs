using SenSÉ.Tools.Documents;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Where a picture lands in the text it was next to on the page.
/// </summary>
/// <remarks>
/// A PDF has no notion of a picture belonging to a paragraph: it has a picture at a coordinate and
/// words at coordinates. Reproducing that exactly would mean a document made of positioned boxes,
/// which is the thing this whole route was written to replace — so the picture is put into the flow
/// instead, and the only question left is between which two paragraphs.
///
/// <para>
/// It is answered by how much reading happens above the picture. These are that rule's cases.
/// </para>
/// </remarks>
public class PdfPicturePlacementTests
{
    // A picture above everything opens the page.
    [Fact]
    public void APictureWithNothingAboveItComesFirst()
        => Assert.Equal(0, PdfToDocument.PlaceAt([10, 10, 10], wordsAbove: 0));

    // A picture below everything closes it.
    [Fact]
    public void APictureWithTheWholePageAboveItComesLast()
        => Assert.Equal(3, PdfToDocument.PlaceAt([10, 10, 10], wordsAbove: 30));

    // And in between it goes where the reading got to.
    [Theory]
    [InlineData(10, 1)]
    [InlineData(20, 2)]
    public void APictureLandsWhereTheReadingReachedIt(int wordsAbove, int expected)
        => Assert.Equal(expected, PdfToDocument.PlaceAt([10, 10, 10], wordsAbove));

    // Part-way through a paragraph is still that paragraph's boundary: a picture cannot be put
    // inside a sentence without breaking it, so it waits for the end of the one it interrupted.
    [Fact]
    public void APictureBesideAParagraphWaitsForItsEnd()
        => Assert.Equal(1, PdfToDocument.PlaceAt([10, 10, 10], wordsAbove: 4));

    // More words above than the page holds: a picture the extractor placed past the end still has
    // to go somewhere, and the end is where it goes rather than nowhere.
    [Fact]
    public void APictureCountedPastTheEndStillLands()
        => Assert.Equal(3, PdfToDocument.PlaceAt([10, 10, 10], wordsAbove: 900));

    // A page with a picture and no text at all — a cover, a full-page figure. The picture is the
    // page, and dropping it was a real defect: the first version lost the cover of every book.
    [Fact]
    public void APictureOnAPageWithoutTextStillLands()
        => Assert.Equal(0, PdfToDocument.PlaceAt([], wordsAbove: 0));
}
