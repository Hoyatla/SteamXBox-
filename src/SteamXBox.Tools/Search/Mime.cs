using System.Text;

namespace SteamXBox.Tools.Search;

/// <summary>
/// The part of the mail format that both reading a message and unpacking one need.
/// </summary>
/// <remarks>
/// Split out when a second reader appeared. <see cref="MimeBody"/> walks a message to find its
/// readable text and steps over everything attached; <see cref="MimeAttachments"/> walks the same
/// message for exactly what the first one steps over. They disagree about what they are looking for
/// and agree about everything else — where the parts are, where a part's headers stop, how a
/// parameter is spelled, how the bytes were packed.
///
/// <para>
/// Kept in one place rather than copied, because the disagreements between two copies of a parser
/// are found by the message that trips one and not the other, and that message belongs to somebody
/// who then cannot open their mail.
/// </para>
/// </remarks>
public static class Mime
{
    /// <summary>Cuts a multipart body at its separator lines.</summary>
    public static IEnumerable<string> Split(string body, string boundary)
    {
        var marker = "--" + boundary;
        var parts = body.Split(marker);

        // The first piece is the text before the opening separator — "this is a MIME message",
        // written for readers from before MIME — and the last is what follows the closing one.
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];

            if (part.StartsWith("--", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return part.TrimStart('\r', '\n');
        }
    }

    /// <summary>Separates a part's own headers from its content.</summary>
    public static (Dictionary<string, string> Headers, string Body) SplitHeaders(string part)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = part.Replace("\r\n", "\n").Split('\n');
        var index = 0;
        var lastName = "";

        for (; index < lines.Length; index++)
        {
            var line = lines[index];

            if (line.Length == 0)
            {
                index++;
                break;
            }

            if (line[0] is ' ' or '\t' && lastName.Length > 0)
            {
                headers[lastName] += ' ' + line.Trim();
                continue;
            }

            var colon = line.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            lastName = line[..colon].Trim();
            headers[lastName] = line[(colon + 1)..].Trim();
        }

        return (headers, index < lines.Length ? string.Join('\n', lines[index..]) : "");
    }

    /// <summary>One parameter of a header, such as the boundary, the charset or a file name.</summary>
    public static string ParameterOf(string header, string name)
    {
        var at = header.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return "";
        }

        var value = header[(at + name.Length + 1)..].TrimStart();

        if (value.StartsWith('"'))
        {
            var close = value.IndexOf('"', 1);
            return close > 0 ? value[1..close] : value[1..];
        }

        var end = value.IndexOfAny([';', ' ', '\r', '\n']);

        return end > 0 ? value[..end] : value;
    }

    /// <summary>
    /// A part's bytes, as they were before being packed for transport.
    /// </summary>
    /// <remarks>
    /// The end of the road for anything that is not text. An attachment is bytes and has to stay
    /// bytes: putting a charset anywhere near a photograph or a spreadsheet corrupts it.
    /// </remarks>
    public static byte[] Bytes(string body, string transferEncoding)
        => transferEncoding.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? FromBase64(body)
            : transferEncoding.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase)
                ? FromQuotedPrintable(body)
                : Encoding.Latin1.GetBytes(body);

    /// <summary>
    /// A part's text.
    /// </summary>
    /// <remarks>
    /// The transfer encoding first, then the charset, because that is the order they were applied
    /// in. An unknown charset falls back to UTF-8 rather than failing: on the mailbox measured, one
    /// thousand seven hundred and eighty messages declare none at all.
    /// </remarks>
    public static string Text(string body, string transferEncoding, string charset)
        => EncodingFor(charset).GetString(Bytes(body, transferEncoding));

    private static Encoding EncodingFor(string charset)
    {
        if (charset.Length == 0)
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static byte[] FromBase64(string body)
    {
        var cleaned = new StringBuilder(body.Length);

        foreach (var character in body)
        {
            if (!char.IsWhiteSpace(character))
            {
                cleaned.Append(character);
            }
        }

        try
        {
            return Convert.FromBase64String(cleaned.ToString());
        }
        catch (FormatException)
        {
            // Truncated by the preview's own size limit, or malformed. The undecoded form is noise;
            // nothing is better than a page of it.
            return [];
        }
    }

    /// <summary>Quoted-printable: <c>=XX</c> is a byte, <c>=</c> at end of line is a soft break.</summary>
    private static byte[] FromQuotedPrintable(string body)
    {
        var bytes = new List<byte>(body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '=')
            {
                if (body[i] != '\r')
                {
                    bytes.Add((byte)body[i]);
                }

                continue;
            }

            // A line ending right after the equals sign is a line the sender wrapped, not a byte.
            if (i + 1 < body.Length && (body[i + 1] == '\r' || body[i + 1] == '\n'))
            {
                i += body[i + 1] == '\r' && i + 2 < body.Length && body[i + 2] == '\n' ? 2 : 1;
                continue;
            }

            if (i + 2 < body.Length
                && byte.TryParse(body.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                bytes.Add(value);
                i += 2;
                continue;
            }

            bytes.Add((byte)'=');
        }

        return bytes.ToArray();
    }
}
