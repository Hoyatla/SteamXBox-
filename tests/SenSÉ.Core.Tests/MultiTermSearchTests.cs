using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Finding something by a few words instead of by its whole path.
/// </summary>
public class MultiTermSearchTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);

    private static SearchItem Item(string name, string path)
        => new(name, path, SearchItemKind.Application, 0, Now.AddDays(-10));

    private static readonly SearchItem SenSÉ =
        Item("SenSÉ.exe", @"D:\sauv.minecraft\Modspack perso\Mods\Projets\SenSÉ-portable-win-x64\SenSÉ.exe");

    [Theory]
    [InlineData("D, SenSÉ")]
    [InlineData("D:, SenSÉ")]
    [InlineData("SenSÉ, projets")]
    [InlineData("d: projets SenSÉ")]
    public void AFewWordsFindTheFile(string typed)
        => Assert.NotNull(SearchRanking.Score(SenSÉ, SearchRanking.Terms(typed), pathMode: false, Now));

    // A bare letter becomes a drive, so "D" means D: and not every word containing a d.
    //
    // The terms come back normalised — lower case, accents dropped — because that is the form
    // Score compares against SearchName and SearchPath, which are normalised once when the entry is
    // built. Expecting "SenSÉ" here asserted the opposite of what the ranking needs.
    //
    // The expectation went unnoticed while the product was called SteamXBox: a name with no accent,
    // written lower case in the expectation, survived normalising unchanged. Renaming to SenSÉ is
    // what made the two forms differ, and the test was reading its own input rather than the
    // contract.
    [Fact]
    public void ALoneLetterIsReadAsADrive()
        => Assert.Equal(["d:", "sense"], SearchRanking.Terms("D, SenSÉ"));

    // All terms required, never any: a second word is how somebody narrows a search, so treating
    // the terms as alternatives would make the answer worse the more they typed.
    [Fact]
    public void EveryTermMustMatch()
        => Assert.Null(SearchRanking.Score(
            SenSÉ, SearchRanking.Terms("SenSÉ, gimp"), pathMode: false, Now));

    [Fact]
    public void TheWrongDriveExcludesIt()
        => Assert.Null(SearchRanking.Score(
            SenSÉ, SearchRanking.Terms("E:, SenSÉ"), pathMode: false, Now));

    // Two terms narrow the field: the one on the named drive wins over the same name elsewhere.
    [Fact]
    public void TheDriveTermSeparatesTwoFilesOfTheSameName()
    {
        var terms = SearchRanking.Terms("D, SenSÉ");

        var onD = SearchRanking.Score(SenSÉ, terms, pathMode: false, Now);
        var onE = SearchRanking.Score(
            Item("SenSÉ.exe", @"E:\copie\SenSÉ.exe"), terms, pathMode: false, Now);

        Assert.NotNull(onD);
        Assert.Null(onE);
    }

    // Averaged rather than summed, so two terms do not simply score twice as high as one and
    // reorder everything against single-term results.
    [Fact]
    public void TwoTermsStayComparableWithOne()
    {
        var one = SearchRanking.Score(SenSÉ, SearchRanking.Terms("SenSÉ"), pathMode: false, Now) ?? 0;
        var two = SearchRanking.Score(SenSÉ, SearchRanking.Terms("D, SenSÉ"), pathMode: false, Now) ?? 0;

        Assert.True(two < one * 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",  ,")]
    public void NothingTypedMatchesNothing(string typed)
        => Assert.Empty(SearchRanking.Terms(typed));

    // One term still behaves exactly as before: the whole point is that nothing regressed for the
    // ordinary case of typing a single word.
    //
    // The single-term overload documents its query as "already normalised", and it is the caller's
    // job to have done it — Terms does exactly that for the other overload. Handing it the raw
    // "SenSÉ" made it compare an accented query against a normalised name, find nothing, and return
    // null, so the test was comparing a failure to a success and calling the difference a
    // regression.
    [Fact]
    public void ASingleTermIsUnchanged()
        => Assert.Equal(
            SearchRanking.Score(SenSÉ, SearchText.Normalize("SenSÉ"), pathMode: false, Now),
            SearchRanking.Score(SenSÉ, SearchRanking.Terms("SenSÉ"), pathMode: false, Now));
}
