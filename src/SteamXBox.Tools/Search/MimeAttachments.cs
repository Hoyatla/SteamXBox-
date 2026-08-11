using System.Text;

namespace SteamXBox.Tools.Search;

/// <summary>
/// One file that travelled with a message.
/// </summary>
/// <param name="Name">What to call it on disk, already made safe.</param>
/// <param name="Bytes">Its contents, unpacked.</param>
public readonly record struct MailAttachment(string Name, byte[] Bytes)
{
    public int Length => Bytes.Length;
}

/// <summary>
/// Walks a message for what was attached to it.
/// </summary>
/// <remarks>
/// The mirror of <see cref="MimeBody"/>: that one steps over every attachment to find the readable
/// text, this one steps over the readable text to find the attachments. They share
/// <see cref="Mime"/> so the two can never disagree about where a part begins.
///
/// <para>
/// <b>Why this exists.</b> Converting a message to a document and quietly dropping what came with it
/// produces a document that lies about what the message was. In most correspondence the attachment
/// is the point and the covering note is "please find attached".
/// </para>
/// </remarks>
public static class MimeAttachments
{
    /// <summary>How deep a message may nest before this stops following it.</summary>
    private const int MaxDepth = 3;

    /// <summary>Everything attached to a message whose bytes arrived as Latin-1 characters.</summary>
    public static IReadOnlyList<MailAttachment> List(IReadOnlyDictionary<string, string> headers, string body)
    {
        var found = new List<MailAttachment>();

        Walk(
            headers.GetValueOrDefault("content-type", ""),
            body,
            depth: 0,
            found);

        return found;
    }

    private static void Walk(string contentType, string body, int depth, List<MailAttachment> found)
    {
        var boundary = Mime.ParameterOf(contentType, "boundary");

        if (boundary.Length == 0 || depth >= MaxDepth)
        {
            return;
        }

        foreach (var part in Mime.Split(body, boundary))
        {
            var (partHeaders, partBody) = Mime.SplitHeaders(part);

            var partType = partHeaders.GetValueOrDefault("content-type", "text/plain");
            var partEncoding = partHeaders.GetValueOrDefault("content-transfer-encoding", "");
            var disposition = partHeaders.GetValueOrDefault("content-disposition", "");

            if (Mime.ParameterOf(partType, "boundary").Length > 0)
            {
                Walk(partType, partBody, depth + 1, found);
                continue;
            }

            var name = NameOf(disposition, partType);

            if (name.Length == 0)
            {
                continue;
            }

            // A part with a name is a file whether or not it says "attachment". An inline image in a
            // signature is disposed inline and is still a picture somebody may want; a part with no
            // name at all is the message text and belongs to the other reader.
            var bytes = Mime.Bytes(partBody, partEncoding);

            if (bytes.Length > 0)
            {
                found.Add(new MailAttachment(name, bytes));
            }
        }
    }

    /// <summary>
    /// What the sender called the file, made safe to write.
    /// </summary>
    /// <remarks>
    /// <b>The name in a message is not a name, it is a claim.</b> It was written by whoever sent the
    /// mail, which in the case of unsolicited correspondence is somebody the recipient has never met,
    /// and it lands in a folder the user chose. A name of <c>..\..\Startup\x.lnk</c> is a valid MIME
    /// header and would be a valid escape from that folder.
    ///
    /// <para>
    /// So only the last segment survives, separators and every character Windows forbids are dropped,
    /// leading dots go, and a name left empty gets one. The extension is kept as sent — renaming a
    /// document to look harmless is its own kind of lie — and nothing here runs anything.
    /// </para>
    /// </remarks>
    public static string NameOf(string disposition, string contentType)
    {
        var raw = Mime.ParameterOf(disposition, "filename");

        if (raw.Length == 0)
        {
            raw = Mime.ParameterOf(contentType, "name");
        }

        if (raw.Length == 0)
        {
            return "";
        }

        return Safe(MailFile.Decode(raw));
    }

    /// <summary>Reduces a claimed name to something that can only land where it was told to.</summary>
    public static string Safe(string name)
    {
        // Any separator, in either direction, and any drive colon. Taken before the invalid-character
        // pass so that a path becomes its last segment rather than one long run-together name.
        var cut = name.LastIndexOfAny(['/', '\\', ':']);

        if (cut >= 0)
        {
            name = name[(cut + 1)..];
        }

        var clean = new StringBuilder(name.Length);
        var forbidden = Path.GetInvalidFileNameChars();

        foreach (var character in name)
        {
            if (!forbidden.Contains(character) && !char.IsControl(character))
            {
                clean.Append(character);
            }
        }

        // Leading dots would make the file hidden on the systems that care and produce "." or ".."
        // on all of them.
        var result = clean.ToString().TrimStart('.', ' ').TrimEnd(' ');

        return result.Length > 0 ? result : "piece-jointe";
    }
}
