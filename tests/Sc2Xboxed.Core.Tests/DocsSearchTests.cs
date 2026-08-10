using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class DocsPolicyTests
{
    [Fact]
    public void OffByDefaultAndUnusableUntilConfigured()
    {
        var resolved = DocsPolicy.Resolve(user: null, policy: null);

        Assert.False(resolved.Effective.Enabled);
        Assert.False(resolved.IsUsable);
    }

    // Enabled is not enough: an instance without an address answers nothing, and the launcher must
    // say so rather than fail at the moment somebody types.
    [Fact]
    public void EnabledWithoutAnAddressIsStillUnusable()
        => Assert.False(DocsPolicy.Resolve(new DocsSettings(Enabled: true), null).IsUsable);

    [Fact]
    public void FullyConfiguredIsUsable()
    {
        var settings = new DocsSettings(true, "https://recherche.interne", "documents", "cle");

        Assert.True(DocsPolicy.Resolve(settings, null).IsUsable);
    }

    // The address and the key are exactly what must not be repointed by a user in a company with
    // sensitive data.
    [Fact]
    public void APolicyFixesTheAddressAndTheKey()
    {
        var user = new DocsSettings(true, "https://ailleurs.example", "documents", "la-mienne");

        var resolved = DocsPolicy.Resolve(user, new DocsPolicy.PolicyValues
        {
            InstanceUrl = "https://corpus.interne",
            ApiKey = "celle-de-la-dsi",
        });

        Assert.Equal("https://corpus.interne", resolved.Effective.InstanceUrl);
        Assert.Equal("celle-de-la-dsi", resolved.Effective.ApiKey);
        Assert.True(resolved.IsLocked(DocsPolicy.InstanceUrlField));
        Assert.True(resolved.IsLocked(DocsPolicy.ApiKeyField));
        Assert.False(resolved.IsLocked(DocsPolicy.EnabledField));
    }

    [Fact]
    public void APolicyCanTurnTheCorpusOff()
    {
        var user = new DocsSettings(true, "https://a.example", "documents");

        var resolved = DocsPolicy.Resolve(user, new DocsPolicy.PolicyValues { Enabled = false });

        Assert.False(resolved.Effective.Enabled);
        Assert.False(resolved.IsUsable);
        Assert.True(resolved.IsLocked(DocsPolicy.EnabledField));
    }
}

public class MeilisearchQueryTests
{
    private static readonly DocsSettings Settings =
        new(true, "https://corpus.interne", "documents", "cle");

    [Fact]
    public void TheSearchAddressNamesTheIndex()
        => Assert.Equal(
            "https://corpus.interne/indexes/documents/search",
            MeilisearchQuery.SearchUrl(Settings));

    // A policy value is typed by a person; only the host is kept.
    [Theory]
    [InlineData("https://corpus.interne/")]
    [InlineData("https://corpus.interne/quelque/chose")]
    public void OnlyTheHostOfTheInstanceIsUsed(string typed)
        => Assert.Equal(
            "https://corpus.interne/indexes/documents/search",
            MeilisearchQuery.SearchUrl(Settings with { InstanceUrl = typed }));

    [Theory]
    [InlineData("file:///C:/")]
    [InlineData("pas une adresse")]
    [InlineData("")]
    public void AnythingThatIsNotHttpGivesNothing(string instance)
        => Assert.Equal("", MeilisearchQuery.SearchUrl(Settings with { InstanceUrl = instance }));

    // A query is user input: a body built by concatenation breaks on the first quotation mark.
    // Asserted by reading the body back rather than on the escaping itself — the serialiser escapes
    // a quotation mark as ", which is correct and is not what a hand-written expectation
    // guesses.
    [Fact]
    public void TheQuerySurvivesQuotationMarks()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            MeilisearchQuery.SearchBody("des \"guillemets\""));

        Assert.Equal("des \"guillemets\"", body.RootElement.GetProperty("q").GetString());
    }

    [Fact]
    public void TheDocumentsAreReadFromTheAgreedShape()
    {
        var results = MeilisearchQuery.Parse("""
            { "hits": [
              { "id": "1", "title": "Compte rendu mars", "path": "\\\\serveur\\cr-mars.pdf", "content": "Ordre du jour…" }
            ] }
            """);

        Assert.Single(results);
        Assert.Equal("Compte rendu mars", results[0].Title);
        Assert.Equal(@"\\serveur\cr-mars.pdf", results[0].Path);
        Assert.Equal("Ordre du jour…", results[0].Snippet);
    }

    // A row that cannot be acted on is worse than a shorter list.
    [Fact]
    public void ADocumentWithoutAPathIsDropped()
        => Assert.Empty(MeilisearchQuery.Parse("""{ "hits": [ { "title": "Sans chemin" } ] }"""));

    [Fact]
    public void AMissingTitleFallsBackToTheFileName()
    {
        var results = MeilisearchQuery.Parse("""{ "hits": [ { "path": "C:\\docs\\rapport.pdf" } ] }""");

        Assert.Equal("rapport.pdf", results[0].Title);
    }

    [Fact]
    public void ALongContentIsCutToOneLine()
    {
        var long_ = new string('a', 400);
        var results = MeilisearchQuery.Parse($$"""{ "hits": [ { "path": "C:\\a", "content": "{{long_}}" } ] }""");

        Assert.True(results[0].Snippet.Length < 200);
        Assert.EndsWith("…", results[0].Snippet);
    }

    [Theory]
    [InlineData("<!doctype html>")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotAnAnswerGivesNothing(string? body)
        => Assert.Empty(MeilisearchQuery.Parse(body));

    [Fact]
    public void TheNumberOfDocumentsIsCapped()
    {
        var many = string.Join(",", Enumerable.Range(0, 200)
            .Select(i => $$"""{ "path": "C:\\d{{i}}", "title": "D{{i}}" }"""));

        Assert.Equal(MeilisearchQuery.Max, MeilisearchQuery.Parse($$"""{ "hits": [{{many}}] }""").Count);
    }
}

public class DocsPrefixTests
{
    [Theory]
    [InlineData("docs: compte rendu", "compte rendu")]
    [InlineData("DOCS:rapport", "rapport")]
    public void TheDocsPrefixDivertsTheQuery(string typed, string rest)
    {
        var routed = QueryPrefix.Parse(typed);

        Assert.Equal(QueryRoute.Docs, routed.Route);
        Assert.Equal(rest, routed.Rest);
    }

    [Fact]
    public void TheWebPrefixStillWorks()
        => Assert.Equal(QueryRoute.Web, QueryPrefix.Parse("web: quelque chose").Route);

    [Fact]
    public void APathIsStillNotAPrefix()
        => Assert.Equal(QueryRoute.Local, QueryPrefix.Parse(@"D:\Projets").Route);
}
