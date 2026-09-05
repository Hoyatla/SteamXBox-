using SenSÉ.Tools.Documents;
using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// What travelled with a message, and where it is allowed to land.
/// </summary>
/// <remarks>
/// Converting a message and dropping its attachments produces a document that lies about what the
/// message was — in most correspondence the attachment is the point and the note says "please find
/// attached". So they are written out beside the document and listed inside it.
///
/// <para>
/// Which makes the name a security question rather than a cosmetic one, and half of these tests are
/// about that: the name was written by whoever sent the mail, and it decides where bytes land on the
/// recipient's disk.
/// </para>
/// </remarks>
public class MailAttachmentTests
{
    private const string Boundary = "----=_Part_1";

    private static string Message(string partHeaders, string content) =>
        $"""
         From: someone@example.org
         To: someone-else@example.org
         Subject: Test
         Content-Type: multipart/mixed; boundary="{Boundary}"

         --{Boundary}
         Content-Type: text/plain; charset=utf-8

         Le corps du message.

         --{Boundary}
         {partHeaders}

         {content}
         --{Boundary}--
         """.Replace("\r\n", "\n");

    // The base case, and the one that matters: a file comes back whole.
    [Fact]
    public void AnAttachmentIsFoundAndDecoded()
    {
        var raw = Message(
            "Content-Type: application/pdf; name=\"rapport.pdf\"\n"
            + "Content-Transfer-Encoding: base64\n"
            + "Content-Disposition: attachment; filename=\"rapport.pdf\"",
            Convert.ToBase64String("%PDF-1.4 contenu"u8.ToArray()));

        var found = MailFile.Attachments(raw);

        Assert.Single(found);
        Assert.Equal("rapport.pdf", found[0].Name);
        Assert.Equal("%PDF-1.4 contenu", System.Text.Encoding.UTF8.GetString(found[0].Bytes));
    }

    // The body reader must still find the text: the two walkers share one splitter precisely so
    // they cannot start disagreeing about where the parts are.
    [Fact]
    public void TheBodyIsStillReadableAlongsideAnAttachment()
    {
        var raw = Message(
            "Content-Type: text/plain; name=\"notes.txt\"\n"
            + "Content-Disposition: attachment; filename=\"notes.txt\"",
            "des notes");

        Assert.Contains("Le corps du message", MailFile.Parse(raw).Body);
    }

    // An inline picture in a signature has a name and is a file somebody may want.
    [Fact]
    public void AnInlinePartWithANameCountsToo()
    {
        var raw = Message(
            "Content-Type: image/png; name=\"logo.png\"\n"
            + "Content-Transfer-Encoding: base64\n"
            + "Content-Disposition: inline; filename=\"logo.png\"",
            Convert.ToBase64String([1, 2, 3, 4]));

        Assert.Equal("logo.png", Assert.Single(MailFile.Attachments(raw)).Name);
    }

    // A part with no name at all is the message, and belongs to the other reader.
    [Fact]
    public void APartWithoutANameIsNotAnAttachment()
        => Assert.Empty(MailFile.Attachments(Message("Content-Type: text/html", "<p>bonjour</p>")));

    // A name accented in French does not travel as text, and this was written for a Swiss mailbox.
    [Fact]
    public void AnEncodedNameComesBackReadable()
    {
        var raw = Message(
            "Content-Type: application/pdf\n"
            + "Content-Disposition: attachment; filename=\"=?UTF-8?B?"
            + Convert.ToBase64String("décompte été.pdf"u8.ToArray()) + "?=\"",
            "contenu");

        Assert.Equal("décompte été.pdf", Assert.Single(MailFile.Attachments(raw)).Name);
    }

    // The one that would be a real incident. A name is a claim made by the sender, and it decides
    // where bytes land in a folder the user chose.
    [Theory]
    [InlineData(@"..\..\Startup\x.lnk", "x.lnk")]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData(@"C:\Windows\System32\evil.dll", "evil.dll")]
    [InlineData("rapport.pdf", "rapport.pdf")]
    public void ANameCanOnlyLandWhereItWasTold(string claimed, string expected)
        => Assert.Equal(expected, MimeAttachments.Safe(claimed));

    // Left alone these produce "." and "..", or a file the user cannot see.
    [Theory]
    [InlineData(".", "piece-jointe")]
    [InlineData("..", "piece-jointe")]
    [InlineData("", "piece-jointe")]
    [InlineData(".caché.txt", "caché.txt")]
    public void ANameThatWouldBeNoNameGetsOne(string claimed, string expected)
        => Assert.Equal(expected, MimeAttachments.Safe(claimed));

    // Characters Windows refuses; a name of nothing but those still has to produce a file.
    [Fact]
    public void ForbiddenCharactersAreDropped()
        => Assert.Equal("rapport.pdf", MimeAttachments.Safe("rap*port?.pdf"));

    // The extension is kept as sent. Renaming a document to look harmless is its own kind of lie,
    // and nothing here opens or runs what it writes.
    [Fact]
    public void TheExtensionIsKeptAsSent()
        => Assert.Equal("facture.pdf.exe", MimeAttachments.Safe("facture.pdf.exe"));

    // The converter has to know a message when it sees one, and leave everything else alone.
    [Theory]
    [InlineData("message.eml", "docx", true)]
    [InlineData("message.wdseml", "txt", true)]
    [InlineData("message.eml", "pptx", false)]
    [InlineData("rapport.pdf", "docx", false)]
    public void OnlyMessagesTakeTheMailRoute(string input, string format, bool expected)
        => Assert.Equal(expected, MailToDocument.Handles(input, format));
}
