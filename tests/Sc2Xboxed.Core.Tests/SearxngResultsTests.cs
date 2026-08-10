using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Reading what an instance answered — which is a server on somebody else's network, and can answer
/// anything at all.
/// </summary>
public class SearxngResultsTests
{
    private const string Answer = """
        {
          "query": "manettes",
          "results": [
            { "url": "https://example.org/a", "title": "Premier", "content": "Un résumé." },
            { "url": "https://example.org/b", "title": "Second", "content": "" }
          ]
        }
        """;

    [Fact]
    public void TheResultsAreReadInOrder()
    {
        var results = SearxngResults.Parse(Answer);

        Assert.Equal(2, results.Count);
        Assert.Equal("Premier", results[0].Title);
        Assert.Equal("https://example.org/a", results[0].Url);
        Assert.Equal("Un résumé.", results[0].Snippet);
    }

    // A result carrying a file: or javascript: address is not a search result, whatever the instance
    // calls it.
    [Fact]
    public void AnAddressThatIsNotHttpIsDropped()
    {
        var results = SearxngResults.Parse("""
            { "results": [
              { "url": "file:///C:/Windows/win.ini", "title": "Non" },
              { "url": "javascript:alert(1)", "title": "Non plus" },
              { "url": "https://example.org/oui", "title": "Oui" }
            ] }
            """);

        Assert.Single(results);
        Assert.Equal("Oui", results[0].Title);
    }

    [Fact]
    public void AResultWithoutAnAddressIsDropped()
        => Assert.Empty(SearxngResults.Parse("""{ "results": [ { "title": "Sans adresse" } ] }"""));

    // Something has to be shown, and the host is what the user would recognise.
    [Fact]
    public void AResultWithoutATitleFallsBackToItsHost()
    {
        var results = SearxngResults.Parse("""{ "results": [ { "url": "https://exemple.fr/page" } ] }""");

        Assert.Equal("exemple.fr", results[0].Title);
    }

    // An instance serving HTML because JSON was never enabled in its settings — the likeliest fault
    // on a fresh deployment, and it must not throw.
    [Theory]
    [InlineData("<!doctype html><html><body>…</body></html>")]
    [InlineData("pas du json")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotAnAnswerGivesNothing(string? body)
        => Assert.Empty(SearxngResults.Parse(body));

    [Fact]
    public void AnAnswerWithoutAResultsArrayGivesNothing()
        => Assert.Empty(SearxngResults.Parse("""{ "query": "x", "erreur": "quelque chose" }"""));

    // However many the instance sends, the list stays a list somebody can read.
    [Fact]
    public void TheNumberOfResultsIsCapped()
    {
        var many = string.Join(",", Enumerable.Range(0, 200)
            .Select(i => $$"""{ "url": "https://example.org/{{i}}", "title": "R{{i}}" }"""));

        Assert.Equal(SearxngResults.Max, SearxngResults.Parse($$"""{ "results": [{{many}}] }""").Count);
    }

    [Fact]
    public void TheJsonFormatIsAskedForExplicitly()
        => Assert.EndsWith("&format=json", SearxngQuery.JsonUrl("https://a.example", "x", SafeSearch.Strict));
}
