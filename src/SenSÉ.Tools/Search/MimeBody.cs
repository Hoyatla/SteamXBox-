namespace SenSÉ.Tools.Search;

/// <summary>
/// Digs the readable text out of a message.
/// </summary>
/// <remarks>
/// A modern message is not text with headers on top. Measured on a real mailbox: <b>seventy-eight
/// per cent are multipart</b> — the same message written twice, once as plain text and once as
/// HTML, wrapped in separator lines, with attachments alongside — and only two per cent are plain
/// text throughout. Four hundred and forty-three encode their body as quoted-printable and thirteen
/// as base64.
///
/// <para>
/// Taking everything after the first blank line, which is what a naive reader does, therefore shows
/// separator lines, the headers of each part, and pages of base64 for anything attached. That is the
/// whole of "the preview is unreadable".
/// </para>
///
/// <para>
/// Not a MIME library. It walks parts, prefers the plain text one, decodes the two transfer
/// encodings that occur, and honours the declared charset. Signatures, encryption and calendar
/// invitations are out of scope on purpose: each is a large surface handling somebody's private
/// correspondence, for a preview.
/// </para>
/// </remarks>
public static class MimeBody
{
    /// <summary>How deep a message may nest before this stops following it.</summary>
    /// <remarks>
    /// Multipart nests legitimately — a mixed message containing an alternative one — but a malformed
    /// message can also declare a part whose boundary is its own. A depth limit is cheaper than
    /// detecting that, and three levels covers every shape that occurs.
    /// </remarks>
    private const int MaxDepth = 3;

    /// <summary>
    /// The readable text of a message whose bytes arrived as Latin-1 characters.
    /// </summary>
    /// <param name="headers">The message's own headers, already unfolded.</param>
    /// <param name="body">Everything after the blank line, one character per original byte.</param>
    /// <remarks>
    /// One character per byte is what makes the charset work: the bytes are still intact, so they can
    /// be handed to whichever encoding the part declares. Decoding to text earlier would have
    /// destroyed them.
    /// </remarks>
    public static string Extract(IReadOnlyDictionary<string, string> headers, string body)
        => Extract(
            headers.GetValueOrDefault("content-type", ""),
            headers.GetValueOrDefault("content-transfer-encoding", ""),
            body,
            depth: 0);

    private static string Extract(string contentType, string transferEncoding, string body, int depth)
    {
        var boundary = Mime.ParameterOf(contentType, "boundary");

        if (boundary.Length > 0 && depth < MaxDepth)
        {
            return FromParts(contentType, body, boundary, depth);
        }

        var decoded = Mime.Text(body, transferEncoding, Mime.ParameterOf(contentType, "charset"));

        return contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            ? IndexableMail.StripMarkup(decoded)
            : IndexableMail.StripMarkup(decoded);
    }

    /// <summary>
    /// Picks one part of a multipart message and reads that.
    /// </summary>
    /// <remarks>
    /// Plain text wins over HTML when both are offered, which is the point of
    /// <c>multipart/alternative</c>: the sender wrote the same thing twice and the plain one is
    /// already what a reader wants. Anything that is not text is skipped rather than decoded — an
    /// attachment's bytes are not a preview.
    /// </remarks>
    private static string FromParts(string contentType, string body, string boundary, int depth)
    {
        string? html = null;

        foreach (var part in Mime.Split(body, boundary))
        {
            var (partHeaders, partBody) = Mime.SplitHeaders(part);

            var partType = partHeaders.GetValueOrDefault("content-type", "text/plain");
            var partEncoding = partHeaders.GetValueOrDefault("content-transfer-encoding", "");

            // An attachment says so, whatever its type. Skipping it here also skips the pages of
            // base64 that make up most of a large message.
            if (partHeaders.GetValueOrDefault("content-disposition", "")
                .Contains("attachment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Mime.ParameterOf(partType, "boundary").Length > 0)
            {
                var nested = Extract(partType, partEncoding, partBody, depth + 1);

                if (nested.Length > 0)
                {
                    return nested;
                }

                continue;
            }

            if (partType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var text = IndexableMail.StripMarkup(
                    Mime.Text(partBody, partEncoding, Mime.ParameterOf(partType, "charset")));

                if (text.Length > 0)
                {
                    return text;
                }
            }
            else if (partType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                html ??= IndexableMail.StripMarkup(
                    Mime.Text(partBody, partEncoding, Mime.ParameterOf(partType, "charset")));
            }
        }

        // The HTML one only if no plain part offered anything.
        return html ?? "";
    }

}
