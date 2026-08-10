using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class QueryPrefixTests
{
    [Theory]
    [InlineData("web: manettes ps5", "manettes ps5")]
    [InlineData("web:manettes", "manettes")]
    [InlineData("WEB:  Foo  ", "Foo")]
    public void TheWebPrefixDivertsTheQuery(string typed, string rest)
    {
        var routed = QueryPrefix.Parse(typed);

        Assert.Equal(QueryRoute.Web, routed.Route);
        Assert.Equal(rest, routed.Rest);
    }

    // A colon alone is not a prefix. Paths are the most ordinary thing typed here, and reading
    // "D:\Projets" as an unknown prefix would break the commonest search there is.
    [Theory]
    [InlineData(@"D:\Projets")]
    [InlineData("C:")]
    [InlineData("notepad")]
    [InlineData("truc: machin")]
    [InlineData(":web")]
    [InlineData("")]
    public void EverythingElseStaysLocal(string typed)
        => Assert.Equal(QueryRoute.Local, QueryPrefix.Parse(typed).Route);

    [Fact]
    public void TheLocalRouteKeepsTheWholeQueryIncludingItsColon()
        => Assert.Equal(@"D:\Projets", QueryPrefix.Parse(@"  D:\Projets").Rest);

    [Fact]
    public void KeywordsAreEscapedIntoTheAddress()
        => Assert.Contains("manettes%20ps5", QueryPrefix.WebSearchUrl("manettes ps5"));
}

public class LaunchHistoryTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SomethingNeverOpenedGainsNothing()
        => Assert.Equal(0, new LaunchHistory().Bonus(@"C:\a.exe", Now));

    [Fact]
    public void OpeningItRaisesIt()
    {
        var history = new LaunchHistory();
        history.Record(@"C:\a.exe", Now);

        Assert.True(history.Bonus(@"C:\a.exe", Now) > 0);
    }

    // Quickly then flat: the difference between never and twice matters far more than between forty
    // and fifty times, and an unbounded counter would eventually outrank the query itself.
    [Fact]
    public void FrequencyFlattensAndStaysCapped()
    {
        var history = new LaunchHistory();

        for (var i = 0; i < 500; i++)
        {
            history.Record(@"C:\a.exe", Now);
        }

        var bonus = history.Bonus(@"C:\a.exe", Now);

        Assert.True(bonus <= LaunchHistory.MaxFrequencyBonus + LaunchHistory.MaxRecencyBonus);
    }

    // A nudge, never a verdict: the whole history is worth less than the difference between an exact
    // name match and a prefix match, so it can reorder near-equals and nothing more.
    [Fact]
    public void TheWholeBonusStaysBelowWhatAMatchIsWorth()
        => Assert.True(LaunchHistory.MaxFrequencyBonus + LaunchHistory.MaxRecencyBonus < 1200 - 900 + 600);

    [Fact]
    public void RecencyFadesAndNeverGoesNegative()
    {
        var history = new LaunchHistory();
        history.Record(@"C:\a.exe", Now.AddDays(-400));

        var old = history.Bonus(@"C:\a.exe", Now);

        history.Record(@"C:\b.exe", Now);
        var fresh = history.Bonus(@"C:\b.exe", Now);

        Assert.True(old >= 0);
        Assert.True(fresh > old);
    }

    // A launcher confidently offering a program that was uninstalled is worse than one that forgot.
    [Fact]
    public void WhatNoLongerExistsIsForgotten()
    {
        var history = new LaunchHistory();
        history.Record(@"C:\gone.exe", Now);
        history.Record(@"C:\here.exe", Now);

        history.Forget(path => path.EndsWith("here.exe", StringComparison.Ordinal), Now);

        Assert.False(history.All.ContainsKey(@"C:\gone.exe"));
        Assert.True(history.All.ContainsKey(@"C:\here.exe"));
    }

    [Fact]
    public void WhatHasNotBeenTouchedInHalfAYearIsForgotten()
    {
        var history = new LaunchHistory();
        history.Record(@"C:\old.exe", Now.AddDays(-200));

        history.Forget(_ => true, Now);

        Assert.Empty(history.All);
    }

    [Fact]
    public void ItSurvivesBeingSavedAndReloaded()
    {
        var history = new LaunchHistory();
        history.Record(@"C:\a.exe", Now);
        history.Record(@"C:\a.exe", Now);

        var reloaded = new LaunchHistory(history.All);

        Assert.Equal(history.Bonus(@"C:\a.exe", Now), reloaded.Bonus(@"C:\a.exe", Now));
    }
}
