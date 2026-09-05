using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The deciding half of QuickCtrlSearch, ported from PowerShell with its weights intact.
/// </summary>
public class SearchTextTests
{
    // What makes "reglages" find "Réglages". On a keyboard where the accent is a dead key,
    // requiring it means the user has to type the answer in order to find it.
    [Theory]
    [InlineData("Réglages", "reglages")]
    [InlineData("ÉLÈVE", "eleve")]
    [InlineData("Über", "uber")]
    [InlineData("Documents", "documents")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AccentsAndCaseAreRemoved(string? input, string expected)
        => Assert.Equal(expected, SearchText.Normalize(input));
}

public class PathQueryTests
{
    [Theory]
    [InlineData(@"C:\Users")]
    [InlineData(@"\\serveur\partage")]
    [InlineData("~/Documents")]
    [InlineData(@".\local")]
    [InlineData("../parent")]
    [InlineData("dossier/fichier")]
    public void APathIsRecognised(string text) => Assert.True(PathQuery.LooksLikeAPath(text));

    [Theory]
    [InlineData("notepad")]
    [InlineData("mon document")]
    [InlineData("")]
    [InlineData(null)]
    public void APlainNameIsNotAPath(string? text) => Assert.False(PathQuery.LooksLikeAPath(text));

    [Fact]
    public void TheTildeBecomesTheUserProfile()
        => Assert.Equal(
            System.IO.Path.Combine(@"C:\Users\Someone", "Documents"),
            PathQuery.Expand("~/Documents", @"C:\Users\Someone"));
}

public class SearchRankingTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);

    private static SearchItem Item(
        string name,
        string path,
        SearchItemKind kind = SearchItemKind.File,
        int priority = 0,
        bool userPath = false,
        bool windowsPath = false,
        int ageInDays = 400)
        => new(name, path, kind, priority, Now.AddDays(-ageInDays), userPath, windowsPath);

    [Fact]
    public void SomethingThatDoesNotMatchScoresNothing()
        => Assert.Null(SearchRanking.Score(Item("notepad", @"C:\notepad.exe"), "gimp", false, Now));

    // The order the weights encode: exact, then prefix, then the start of a word, then anywhere in
    // the name, then only in the path.
    [Fact]
    public void TheBetterMatchAlwaysWins()
    {
        int Score(string name) =>
            SearchRanking.Score(Item(name, @"C:\x\" + name), "note", false, Now) ?? 0;

        Assert.True(Score("note") > Score("notepad"));
        Assert.True(Score("notepad") > Score("release_note"));
        Assert.True(Score("release_note") > Score("carnotel"));
    }

    // Separators only, so "note" finds "release_note" without every name that merely contains the
    // letters ranking as highly.
    [Theory]
    [InlineData("release_note")]
    [InlineData("release note")]
    [InlineData("release.note")]
    [InlineData("release-note")]
    public void AQueryThatBeginsAWordRanksAboveOneBuriedInside(string name)
    {
        var atWordStart = SearchRanking.Score(Item(name, @"C:\x"), "note", false, Now);
        var buried = SearchRanking.Score(Item("carnotel", @"C:\x"), "note", false, Now);

        Assert.True(atWordStart > buried);
    }

    // A name matching the last segment is not rejected — the original keeps it as a last resort so
    // a mistyped folder still shows something — but it ranks far below a real path match.
    [Fact]
    public void PathModeRanksThePathFarAboveTheName()
    {
        var query = SearchText.Normalize(@"c:\users\someone\doc");

        var byPath = SearchRanking.Score(
            Item("truc", @"C:\Users\Someone\Documents"), query, pathMode: true, Now);

        var byName = SearchRanking.Score(
            Item("doc", @"D:\ailleurs\truc"), query, pathMode: true, Now);

        Assert.NotNull(byPath);
        Assert.NotNull(byName);
        Assert.True(byPath - byName >= 700);
    }

    [Fact]
    public void PathModeRejectsWhatMatchesNeitherPathNorLastSegment()
        => Assert.Null(SearchRanking.Score(
            Item("truc", @"D:\ailleurs\truc"),
            SearchText.Normalize(@"c:\users\someone\doc"),
            pathMode: true,
            Now));

    // Findable but never first: Windows holds thousands of files whose names collide with
    // everything the user might type.
    [Fact]
    public void AWindowsFileIsPushedDown()
    {
        var mine = SearchRanking.Score(Item("config", @"C:\Users\Someone\config"), "conf", false, Now);
        var windows = SearchRanking.Score(
            Item("config", @"C:\Windows\System32\config", windowsPath: true), "conf", false, Now);

        Assert.True(mine - windows >= 450);
    }

    // Unless it is exactly what was asked for, which is the one case where the user did mean it.
    [Fact]
    public void AnExactMatchEscapesTheWindowsPenalty()
    {
        var exact = SearchRanking.Score(
            Item("regedit", @"C:\Windows\regedit.exe", windowsPath: true), "regedit", false, Now);

        var partial = SearchRanking.Score(
            Item("regedit", @"C:\Windows\regedit.exe", windowsPath: true), "reg", false, Now);

        Assert.True(exact - partial > 450);
    }

    [Fact]
    public void RecencyIsWorthAtMostEightyPoints()
    {
        var today = SearchRanking.Score(Item("note", @"C:\note", ageInDays: 0), "note", false, Now);
        var ancient = SearchRanking.Score(Item("note", @"C:\note", ageInDays: 4000), "note", false, Now);

        Assert.Equal(80, today - ancient);
    }

    // Never negative, whatever the clock says. A file dated in the future would otherwise earn more
    // than eighty, and a machine with a wrong clock would reorder every result.
    [Fact]
    public void AFileDatedInTheFutureEarnsNoMoreThanOneDatedToday()
    {
        var future = SearchRanking.Score(Item("note", @"C:\note", ageInDays: -900), "note", false, Now);
        var today = SearchRanking.Score(Item("note", @"C:\note", ageInDays: 0), "note", false, Now);

        Assert.Equal(today, future);
    }
}
