using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SteamXBox.Tools.Search;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Turns a mail message into a document, and puts back what came with it.
/// </summary>
/// <remarks>
/// Nothing here parses mail. The reader was written for the index — folded headers, encoded words,
/// quoted-printable, base64, declared charsets, the seventy-eight per cent of real messages that are
/// multipart — and has been read against a real mailbox for weeks. This adds a way out of it.
///
/// <para>
/// <b>Attachments are extracted beside the document and listed inside it.</b> The alternatives were
/// to drop them, which makes a document that lies about what the message was, or to list them
/// without producing them, which tells the reader what they are missing without giving it to them.
/// In most correspondence the attachment is the point and the covering note says "please find
/// attached".
/// </para>
///
/// <para>
/// The file names are treated as hostile. They were written by whoever sent the message, and they
/// decide where bytes land on the recipient's disk — see <see cref="MimeAttachments.Safe"/>. Nothing
/// is opened, nothing is run: files are written and their names are shown.
/// </para>
/// </remarks>
public static class MailToDocument
{
    /// <summary>Which targets this route can write.</summary>
    public static IReadOnlySet<string> Formats { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "docx", "txt" };

    /// <summary>Whether a conversion should take this route.</summary>
    public static bool Handles(string input, string format)
        => MailFile.Extensions.Contains(Path.GetExtension(input)) && Formats.Contains(format);

    /// <summary>Reads the message and writes the document beside it.</summary>
    public static LibreOffice.Result Convert(string input, string format, string outputDirectory)
    {
        try
        {
            // One character per byte, which is what the reader was built on: the bytes stay intact
            // until a part declares which encoding they are in. Reading as UTF-8 here would destroy
            // every message that is not.
            var raw = File.ReadAllText(input, Encoding.Latin1);

            var message = MailFile.Parse(raw);
            var attachments = MailFile.Attachments(raw);

            Directory.CreateDirectory(outputDirectory);

            var stem = Path.GetFileNameWithoutExtension(input);
            var output = Path.Combine(outputDirectory, stem + "." + format.ToLowerInvariant());

            var written = Extract(attachments, outputDirectory, stem);

            if (format.Equals("txt", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(output, PlainText(message, written), Encoding.UTF8);
            }
            else
            {
                WriteDocx(output, message, written);
            }

            return new LibreOffice.Result(output, "");
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
        }
    }

    /// <summary>What an attachment ended up being called on disk.</summary>
    private readonly record struct Written(string Name, int Length);

    /// <summary>
    /// Writes the attachments into a folder of their own.
    /// </summary>
    /// <remarks>
    /// A folder rather than loose beside the document, because a single message can carry twenty
    /// files and the user pointed at that directory to convert one thing, not to have it filled.
    ///
    /// <para>
    /// Nothing is overwritten. Two parts of one message are routinely called <c>image001.png</c>, and
    /// a folder that already holds a file of that name belongs to the user, not to this. A number is
    /// added until the name is free.
    /// </para>
    /// </remarks>
    private static List<Written> Extract(
        IReadOnlyList<MailAttachment> attachments,
        string outputDirectory,
        string stem)
    {
        var written = new List<Written>();

        if (attachments.Count == 0)
        {
            return written;
        }

        var folder = Free(Path.Combine(outputDirectory, MimeAttachments.Safe(stem) + " - pièces jointes"));

        Directory.CreateDirectory(folder);

        foreach (var attachment in attachments)
        {
            var path = Free(Path.Combine(folder, attachment.Name));

            File.WriteAllBytes(path, attachment.Bytes);

            written.Add(new Written(Path.GetFileName(path), attachment.Length));
        }

        return written;
    }

    /// <summary>A path nothing occupies yet.</summary>
    private static string Free(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var number = 2; number < 1000; number++)
        {
            var candidate = Path.Combine(folder, $"{name} ({number}){extension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }

    /// <summary>The message as a plain text file.</summary>
    private static string PlainText(MailMessage message, IReadOnlyList<Written> attachments)
    {
        var text = new StringBuilder();

        foreach (var (label, value) in Fields(message))
        {
            text.Append(label).Append(" : ").AppendLine(value);
        }

        text.AppendLine().AppendLine(message.Body);

        if (attachments.Count > 0)
        {
            text.AppendLine().AppendLine($"Pièces jointes ({attachments.Count})");

            foreach (var attachment in attachments)
            {
                text.AppendLine($"  {attachment.Name}  ({Size(attachment.Length)})");
            }
        }

        return text.ToString();
    }

    /// <summary>The headers worth keeping, in the order a reader expects them.</summary>
    private static IEnumerable<(string Label, string Value)> Fields(MailMessage message)
    {
        if (message.Subject.Length > 0) { yield return ("Objet", message.Subject); }
        if (message.From.Length > 0) { yield return ("De", message.From); }
        if (message.To.Length > 0) { yield return ("À", message.To); }
        if (message.Date.Length > 0) { yield return ("Date", message.Date); }
    }

    /// <summary>
    /// The message as a Word document.
    /// </summary>
    /// <remarks>
    /// Paragraphs and nothing else, for the same reason the PDF route makes none: a document that
    /// opens instantly and can be edited is what somebody converting a message is after.
    /// </remarks>
    private static void WriteDocx(string output, MailMessage message, IReadOnlyList<Written> attachments)
    {
        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();

        var body = main.Document.AppendChild(new Body());

        foreach (var (label, value) in Fields(message))
        {
            body.AppendChild(new Paragraph(
                new Run(new RunProperties(new Bold()), new Text(label + " : ")),
                new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve })));
        }

        body.AppendChild(new Paragraph());

        foreach (var line in message.Body.Replace("\r\n", "\n").Split('\n'))
        {
            body.AppendChild(new Paragraph(
                new Run(new Text(Printable(line)) { Space = SpaceProcessingModeValues.Preserve })));
        }

        if (attachments.Count == 0)
        {
            main.Document.Save();
            return;
        }

        body.AppendChild(new Paragraph());
        body.AppendChild(new Paragraph(
            new Run(new RunProperties(new Bold()), new Text($"Pièces jointes ({attachments.Count})"))));

        foreach (var attachment in attachments)
        {
            body.AppendChild(new Paragraph(
                new Run(new Text($"{attachment.Name}  ({Size(attachment.Length)})")
                {
                    Space = SpaceProcessingModeValues.Preserve,
                })));
        }

        main.Document.Save();
    }

    /// <summary>
    /// Drops the characters a document cannot contain.
    /// </summary>
    /// <remarks>
    /// The same guard the PDF route needs, for the same reason: a Word document is XML, XML forbids
    /// almost every control character, and one of them anywhere loses the whole document rather than
    /// one character. Mail carries them from every direction.
    /// </remarks>
    private static string Printable(string text)
    {
        if (!text.Any(character => char.IsControl(character) && character is not '\t'))
        {
            return text;
        }

        var clean = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (!char.IsControl(character) || character is '\t')
            {
                clean.Append(character);
            }
        }

        return clean.ToString();
    }

    private static string Size(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => $"{bytes / 1024} Ko",
        _ => $"{bytes / 1024 / 1024} Mo",
    };
}
