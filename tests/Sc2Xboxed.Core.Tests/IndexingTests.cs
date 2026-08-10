using SteamXBox.Tools.Indexing;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class IndexableDocumentTests
{
    // Re-indexing the same file must replace its document, not add a second copy. Without a stable
    // id every run doubles the index.
    [Fact]
    public void TheSamePathAlwaysGivesTheSameId()
        => Assert.Equal(
            IndexableDocument.IdFor(@"\\serveur\docs\a.pdf"),
            IndexableDocument.IdFor(@"\\serveur\docs\a.pdf"));

    // Windows does not distinguish case: one file must not become two documents.
    [Fact]
    public void CaseDoesNotChangeTheId()
        => Assert.Equal(
            IndexableDocument.IdFor(@"C:\Docs\A.pdf"),
            IndexableDocument.IdFor(@"c:\docs\a.pdf"));

    [Fact]
    public void DifferentPathsGiveDifferentIds()
        => Assert.NotEqual(
            IndexableDocument.IdFor(@"C:\docs\a.pdf"),
            IndexableDocument.IdFor(@"C:\docs\b.pdf"));

    // Meilisearch accepts only letters, digits, hyphens and underscores in an id, which is why a
    // path cannot be one.
    [Fact]
    public void AnIdIsAcceptableToTheIndex()
    {
        var id = IndexableDocument.IdFor(@"\\serveur\dossier avec espaces\é&#.pdf");

        Assert.Matches("^[a-z0-9]+$", id);
    }

    [Fact]
    public void ADocumentTakesItsTitleFromTheFileName()
    {
        var document = IndexableDocument.From(@"\\serveur\docs\Compte rendu.txt", "du texte");

        Assert.Equal("Compte rendu.txt", document.Title);
        Assert.Equal(@"\\serveur\docs\Compte rendu.txt", document.Path);
    }
}

public class IndexableFilesTests
{
    // The reason a naive indexer takes hours and returns rubbish.
    [Theory]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("obj")]
    [InlineData("System Volume Information")]
    [InlineData(".vscode")]
    public void TheFoldersThatHoldNoDocumentsAreSkipped(string folder)
        => Assert.False(IndexableFiles.ShouldDescend(folder));

    [Theory]
    [InlineData("Documents")]
    [InlineData("Comptes rendus")]
    [InlineData("2026")]
    public void OrdinaryFoldersAreDescendedInto(string folder)
        => Assert.True(IndexableFiles.ShouldDescend(folder));

    // A book pushed whole makes one document that matches almost any query.
    [Fact]
    public void TextIsCutToWhatIsWorthIndexing()
    {
        var long_ = new string('a', IndexableFiles.MaxCharacters + 5000);

        Assert.Equal(IndexableFiles.MaxCharacters, IndexableFiles.Trim(long_).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingGivesNothing(string? text)
        => Assert.Equal("", IndexableFiles.Trim(text));
}

public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new();

    [Theory]
    [InlineData(".txt")]
    [InlineData(".MD")]
    [InlineData(".csv")]
    [InlineData(".html")]
    [InlineData(".srt")]
    public void TheFormatsThatAreAlreadyTextAreHandled(string extension)
        => Assert.True(_extractor.Handles(extension));

    // Not this extractor's business. PDF and Office are handled by their own extractors in the
    // indexer, which carry the libraries; keeping them out of here is what lets SteamXBox.Tools stay
    // free of dependencies and reachable by the launcher.
    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    public void TheFormatsNeedingALibraryBelongToTheirOwnExtractor(string extension)
        => Assert.False(_extractor.Handles(extension));

    // Nothing reads these, anywhere in the pipeline. The old binary Office formats are a different
    // format that the Open XML SDK does not open at all, and a share of a school or an association
    // is full of them — worth knowing rather than discovering from a thin index.
    [Theory]
    [InlineData(".doc")]
    [InlineData(".xls")]
    [InlineData(".ppt")]
    [InlineData(".jpg")]
    public void TheFormatsNothingReadsAreNamed(string extension)
        => Assert.False(_extractor.Handles(extension));

    [Fact]
    public void AMissingFileGivesNothingRatherThanThrowing()
        => Assert.Equal("", _extractor.Extract(@"C:\ce\fichier\n-existe\pas.txt"));
}
