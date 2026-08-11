using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class MailFileTests
{
    [Fact]
    public void TheBlankLineSeparatesHeadersFromTheBody()
    {
        var message = MailFile.Parse("Subject: Réunion\nFrom: a@b.ch\n\nLe corps du message.");

        Assert.Equal("Réunion", message.Subject);
        Assert.Equal("a@b.ch", message.From);
        Assert.Equal("Le corps du message.", message.Body);
    }

    // A long subject arrives wrapped across lines, continued by a leading space. Read line by line
    // it would keep only its first few words.
    [Fact]
    public void AHeaderContinuedOnTheNextLineIsFoldedBack()
    {
        var message = MailFile.Parse("Subject: Convocation à la séance\n du conseil de classe\n\nx");

        Assert.Equal("Convocation à la séance du conseil de classe", message.Subject);
    }

    // Without this every accented subject in a French mailbox is searchable only by its encoded
    // form, which is to say not at all.
    [Fact]
    public void ABase64EncodedSubjectIsDecoded()
        => Assert.Equal(
            "Prière de confirmer",
            MailFile.Decode("=?UTF-8?B?UHJpw6hyZSBkZSBjb25maXJtZXI=?="));

    [Fact]
    public void AQuotedPrintableSubjectIsDecoded()
        => Assert.Equal("Réunion demain", MailFile.Decode("=?UTF-8?Q?R=C3=A9union_demain?="));

    [Fact]
    public void AnEncodedWordAmongPlainTextKeepsBoth()
        => Assert.Equal("Fwd: Réunion", MailFile.Decode("Fwd: =?UTF-8?B?UsOpdW5pb24=?="));

    // An unknown charset or bad padding must not throw, and must not invent text either.
    [Theory]
    [InlineData("=?NOT-A-CHARSET?B?abcd?=")]
    [InlineData("=?UTF-8?B?not valid base64!?=")]
    [InlineData("=?UTF-8?X?unknown encoding?=")]
    public void AHeaderThatWillNotDecodeIsLeftAsItIs(string header)
        => Assert.Contains("?", MailFile.Decode(header));

    [Fact]
    public void PlainTextWithoutEncodingIsUntouched()
        => Assert.Equal("Meeting tomorrow", MailFile.Decode("Meeting tomorrow"));

    [Fact]
    public void AMessageWithNoHeadersStillGivesItsText()
    {
        var message = MailFile.Parse("\nJuste du texte.");

        Assert.Equal("", message.Subject);
        Assert.Equal("Juste du texte.", message.Body);
    }

    [Theory]
    [InlineData(".eml")]
    [InlineData(".WDSEML")]
    public void TheOneMessagePerFileFormatsAreRecognised(string extension)
        => Assert.Contains(extension, MailFile.Extensions);
}

public class IndexableMailTests
{
    // Most mail is HTML. Indexed raw, a query for "span" matches half the mailbox.
    [Fact]
    public void TagsAreRemovedFromAnHtmlBody()
        => Assert.Equal(
            "Bonjour Charly",
            IndexableMail.StripMarkup("<html><body><p>Bonjour</p><b>Charly</b></body></html>"));

    // Dropped whole rather than stripped of tags: their contents are not text anybody wrote.
    [Fact]
    public void StylesAndScriptsGoAltogether()
    {
        var text = IndexableMail.StripMarkup(
            "<style>.a{color:red}</style><p>Visible</p><script>var x = 1;</script>");

        Assert.Equal("Visible", text);
        Assert.DoesNotContain("color", text);
        Assert.DoesNotContain("var", text);
    }

    [Fact]
    public void TheEntitiesThatAppearInProseAreTurnedBack()
        => Assert.Equal("Bonjour & bienvenue", IndexableMail.StripMarkup("<p>Bonjour&nbsp;&amp; bienvenue</p>"));

    [Fact]
    public void PlainTextIsNotTreatedAsMarkup()
        => Assert.Equal("2 < 3 et 4 > 1", IndexableMail.StripMarkup("2 < 3 et 4 > 1"));

    [Fact]
    public void WhitespaceIsSqueezed()
        => Assert.Equal("un deux trois", IndexableMail.StripMarkup("un\n\n   deux\t\ttrois  "));

    // Beyond the cap a message is quoted history rather than a message.
    [Fact]
    public void AVeryLongMessageIsCut()
    {
        var long_ = new string('a', IndexableMail.MaxCharacters + 5_000);

        Assert.Equal(IndexableMail.MaxCharacters, IndexableMail.StripMarkup(long_).Length);
    }

    [Fact]
    public void NothingGivesNothing()
        => Assert.Equal("", IndexableMail.StripMarkup(""));
}

public class MailQueryPrefixTests
{
    [Fact]
    public void TheMailPrefixRoutesToMail()
    {
        var routed = QueryPrefix.Parse("mail: convocation");

        Assert.Equal(QueryRoute.Mail, routed.Route);
        Assert.Equal("convocation", routed.Rest);
    }

    // Mail has its own route so that it can never be pointed at the shared corpus by a setting.
    [Fact]
    public void MailIsNotTheSameRouteAsDocs()
        => Assert.NotEqual(QueryPrefix.Parse("docs: x").Route, QueryPrefix.Parse("mail: x").Route);

    [Fact]
    public void ADriveLetterIsStillNotAPrefix()
        => Assert.Equal(QueryRoute.Local, QueryPrefix.Parse(@"D:\Projets").Route);
}
