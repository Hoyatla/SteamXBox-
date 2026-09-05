using System.Text;
using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

public class MboxReaderTests
{
    private const string Separator = "From charly@example.ch Mon Jan  1 09:00:00 2024";

    private static string Mailbox(params string[] messages)
        => string.Join("", messages.Select(m => Separator + "\r\n" + m.ReplaceLineEndings("\r\n") + "\r\n"));

    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, content, Encoding.Latin1);
        return path;
    }

    [Fact]
    public void EachSeparatorStartsAMessage()
    {
        var path = Write(Mailbox("Subject: Un\n\nPremier.", "Subject: Deux\n\nSecond."));

        try
        {
            var messages = MboxReader.Messages(path).ToArray();

            Assert.Equal(2, messages.Length);
            Assert.Equal("Un", MailFile.Parse(messages[0].Raw).Subject);
            Assert.Equal("Deux", MailFile.Parse(messages[1].Raw).Subject);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The offsets are the whole point of the format being usable: a message in a three-hundred-
    // megabyte mailbox is read back from its position, and a drift of one byte per line reads a
    // neighbour instead.
    [Fact]
    public void AMessageIsReadBackFromItsRecordedPosition()
    {
        var path = Write(Mailbox(
            "Subject: Premier\n\nLigne\nLigne\nLigne.",
            "Subject: Cherché\n\nLe bon corps.",
            "Subject: Dernier\n\nAutre chose."));

        try
        {
            var wanted = MboxReader.Messages(path).ToArray()[1];

            var entry = new MailEntry(
                "Cherché", "", "", path, wanted.Where.Offset, wanted.Where.Length, "");

            var read = MailReader.Read(entry);

            Assert.NotNull(read);
            Assert.Equal("Cherché", read!.Value.Subject);
            Assert.Contains("Le bon corps", read.Value.Body, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileThatDoesNotStartWithASeparatorIsNotAMailbox()
    {
        var path = Write("Ceci est un fichier de résumé, pas une boîte.");

        try
        {
            Assert.False(MboxReader.LooksLikeMailbox(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMailboxIsRecognisedByWhatItOpensWith()
    {
        var path = Write(Mailbox("Subject: Un\n\nx"));

        try
        {
            Assert.True(MboxReader.LooksLikeMailbox(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A body line beginning with "From " is the known flaw of the format. Requiring a day name after
    // the address removes almost all of it.
    [Fact]
    public void ASentenceBeginningWithFromDoesNotSplitAMessage()
    {
        var path = Write(Mailbox("Subject: Un\n\nFrom here it gets complicated.\nSuite."));

        try
        {
            Assert.Single(MboxReader.Messages(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class MailPreviewTests
{
    [Fact]
    public void ThePreviewLeadsWithTheHeadersThatIdentifyAMessage()
    {
        var preview = MailReader.ToPreview(
            new MailMessage("Réunion", "a@b.ch", "c@d.ch", "Mon, 1 Jan 2024", "Le corps."));

        Assert.Contains("Objet : Réunion", preview, StringComparison.Ordinal);
        Assert.Contains("De : a@b.ch", preview, StringComparison.Ordinal);
        Assert.Contains("Le corps.", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderThatIsMissingLeavesNoEmptyLabel()
    {
        var preview = MailReader.ToPreview(new MailMessage("Objet seul", "", "", "", "x"));

        Assert.DoesNotContain("De :", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void AVeryLongMessageIsCutAndSaysSo()
    {
        var preview = MailReader.ToPreview(
            new MailMessage("x", "", "", "", new string('a', MailReader.MaxPreviewCharacters + 1_000)));

        Assert.Contains("[…]", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressBecomesAMarkerInThePreview()
    {
        var preview = MailReader.ToPreview(new MailMessage(
            "x", "", "", "", "Voir https://example.org/a/very/long/tracking/path?token=abc pour la suite."));

        Assert.Contains("Voir [lien] pour la suite.", preview, StringComparison.Ordinal);
    }

    // A footer of fifteen links would otherwise become fifteen markers in a row, which is as
    // unreadable as what it replaced.
    [Fact]
    public void ARunOfAddressesCollapsesToOneMarker()
    {
        var collapsed = IndexableMail.CollapseLinks(
            "https://a.ch/x · https://b.ch/y · https://c.ch/z fin");

        Assert.Equal("[lien]fin", collapsed.Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAddressWithoutASchemeIsAlsoCaught()
        => Assert.Equal("Voir [lien] svp", IndexableMail.CollapseLinks("Voir www.exemple.ch/page svp"));

    // The index keeps them: somebody searching for a domain or a ticket number in a link must still
    // find the message.
    [Fact]
    public void WhatIsIndexedKeepsItsAddresses()
    {
        var message = MailFile.Parse("Subject: x\n\nVoir https://linkedin.com/jobs/1234 pour l'offre.");

        Assert.Contains("linkedin.com", message.Searchable, StringComparison.Ordinal);
        Assert.DoesNotContain(IndexableMail.Marker, message.Searchable, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWithoutAddressesIsUntouched()
        => Assert.Equal("Rien à remplacer.", IndexableMail.CollapseLinks("Rien à remplacer."));

    [Fact]
    public void AMissingFileGivesNothingRatherThanThrowing()
        => Assert.Null(MailReader.Read(
            new MailEntry("x", "", "", @"C:\ce\fichier\n-existe\pas.eml", 0, 0, "")));
}
