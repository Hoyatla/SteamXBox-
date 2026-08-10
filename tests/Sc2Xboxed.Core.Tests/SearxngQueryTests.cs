using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class SearxngQueryTests
{
    [Fact]
    public void TheAddressCarriesTheQueryAndTheLevel()
    {
        var url = SearxngQuery.Url("https://recherche.ecole", "manettes ps5", SafeSearch.Strict);

        Assert.Equal("https://recherche.ecole/search?q=manettes%20ps5&safesearch=2", url);
    }

    [Theory]
    [InlineData(SafeSearch.Off, "safesearch=0")]
    [InlineData(SafeSearch.Moderate, "safesearch=1")]
    [InlineData(SafeSearch.Strict, "safesearch=2")]
    public void EachLevelHasItsNumber(SafeSearch level, string expected)
        => Assert.Contains(expected, SearxngQuery.Url("https://a.example", "x", level));

    // Whatever the administrator typed, only the host is kept: a policy value is written by a
    // person, and a trailing path would send the query somewhere other than the search endpoint.
    [Theory]
    [InlineData("https://recherche.ecole/")]
    [InlineData("https://recherche.ecole/quelque/chose")]
    [InlineData("  https://recherche.ecole  ")]
    public void OnlyTheHostOfTheInstanceIsUsed(string typed)
        => Assert.StartsWith("https://recherche.ecole/search?", SearxngQuery.Url(typed, "x", SafeSearch.Moderate));

    // An address with another scheme is a way to launch something, not to search.
    [Theory]
    [InlineData("file:///C:/Windows")]
    [InlineData("javascript:alert(1)")]
    [InlineData("pas une adresse")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotHttpGivesNothing(string? instance)
        => Assert.Equal("", SearxngQuery.Url(instance, "x", SafeSearch.Moderate));

    [Fact]
    public void NoKeywordsGiveNoAddress()
        => Assert.Equal("", SearxngQuery.Url("https://a.example", "   ", SafeSearch.Moderate));
}
