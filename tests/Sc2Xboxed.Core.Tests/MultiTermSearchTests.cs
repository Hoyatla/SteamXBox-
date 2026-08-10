using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Finding something by a few words instead of by its whole path.
/// </summary>
public class MultiTermSearchTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);

    private static SearchItem Item(string name, string path)
        => new(name, path, SearchItemKind.Application, 0, Now.AddDays(-10));

    private static readonly SearchItem SteamXBox =
        Item("SteamXBox.exe", @"D:\sauv.minecraft\Modspack perso\Mods\Projets\SteamXBox-portable-win-x64\SteamXBox.exe");

    [Theory]
    [InlineData("D, steamxbox")]
    [InlineData("D:, steamxbox")]
    [InlineData("steamxbox, projets")]
    [InlineData("d: projets steamxbox")]
    public void AFewWordsFindTheFile(string typed)
        => Assert.NotNull(SearchRanking.Score(SteamXBox, SearchRanking.Terms(typed), pathMode: false, Now));

    // A bare letter becomes a drive, so "D" means D: and not every word containing a d.
    [Fact]
    public void ALoneLetterIsReadAsADrive()
        => Assert.Equal(["d:", "steamxbox"], SearchRanking.Terms("D, steamxbox"));

    // All terms required, never any: a second word is how somebody narrows a search, so treating
    // the terms as alternatives would make the answer worse the more they typed.
    [Fact]
    public void EveryTermMustMatch()
        => Assert.Null(SearchRanking.Score(
            SteamXBox, SearchRanking.Terms("steamxbox, gimp"), pathMode: false, Now));

    [Fact]
    public void TheWrongDriveExcludesIt()
        => Assert.Null(SearchRanking.Score(
            SteamXBox, SearchRanking.Terms("E:, steamxbox"), pathMode: false, Now));

    // Two terms narrow the field: the one on the named drive wins over the same name elsewhere.
    [Fact]
    public void TheDriveTermSeparatesTwoFilesOfTheSameName()
    {
        var terms = SearchRanking.Terms("D, steamxbox");

        var onD = SearchRanking.Score(SteamXBox, terms, pathMode: false, Now);
        var onE = SearchRanking.Score(
            Item("SteamXBox.exe", @"E:\copie\SteamXBox.exe"), terms, pathMode: false, Now);

        Assert.NotNull(onD);
        Assert.Null(onE);
    }

    // Averaged rather than summed, so two terms do not simply score twice as high as one and
    // reorder everything against single-term results.
    [Fact]
    public void TwoTermsStayComparableWithOne()
    {
        var one = SearchRanking.Score(SteamXBox, SearchRanking.Terms("steamxbox"), pathMode: false, Now) ?? 0;
        var two = SearchRanking.Score(SteamXBox, SearchRanking.Terms("D, steamxbox"), pathMode: false, Now) ?? 0;

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
    [Fact]
    public void ASingleTermIsUnchanged()
        => Assert.Equal(
            SearchRanking.Score(SteamXBox, "steamxbox", pathMode: false, Now),
            SearchRanking.Score(SteamXBox, SearchRanking.Terms("steamxbox"), pathMode: false, Now));
}
