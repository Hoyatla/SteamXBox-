using System.Text;

namespace SenSÉ.Tools.Search;

/// <summary>One message, reduced to what a search needs.</summary>
/// <param name="Subject">The subject line, or an empty string.</param>
/// <param name="From">The sender, as written in the header.</param>
/// <param name="To">The recipients, as written in the header.</param>
/// <param name="Date">The date header, unparsed — it is searched as text, not sorted on.</param>
/// <param name="Body">The readable part of the message, already trimmed.</param>
/// <param name="MessageId">
/// The identifier the sender gave it, which is what tells two copies of one message apart from two
/// different messages.
/// </param>
/// <remarks>
/// <see cref="MessageId"/> earns its place: Thunderbird keeps every message in a mailbox and also
/// exports some of them as separate files for Windows Search, so the same message is on disk twice
/// and would be indexed twice. Matching on subject and date would merge a mail sent to two people
/// on the same minute; the identifier is the only field meant for this.
/// </remarks>
public readonly record struct MailMessage(
    string Subject, string From, string To, string Date, string Body, string MessageId = "")
{
    /// <summary>Everything worth matching a query against, in one string.</summary>
    public string Searchable => string.Join('\n', new[] { Subject, From, To, Date, Body }
        .Where(part => part.Length > 0));
}

/// <summary>
/// Reads a single mail message stored as a file.
/// </summary>
/// <remarks>
/// Thunderbird writes one of these per message when its Windows Search integration is on — the
/// <c>.wdseml</c> files under a profile's <c>*.mozmsgs</c> folders. On the machine this was written
/// for there were five hundred and forty-two of them, a couple of kilobytes each, already in the
/// standard mail format. That is a far better starting point than the mailboxes beside them, which
/// hold everything in single files of three hundred megabytes and have to be cut into messages
/// first.
///
/// <para>
/// Deliberately not a mail library. What a search needs is the subject, who it was from and to, the
/// date, and the readable text; parsing beyond that — nested parts, calendar invitations, signature
/// verification — is a large surface for no gain, and every byte of it would be handling somebody's
/// private correspondence.
/// </para>
/// </remarks>
public static class MailFile
{
    /// <summary>The extensions holding exactly one message.</summary>
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".eml", ".wdseml" };

    /// <summary>Splits a message into its headers and its readable text.</summary>
    /// <remarks>
    /// The blank line is the whole of the format: everything before it is headers, everything after
    /// is the body. A header continued on the next line starts with a space or a tab, which is why
    /// continuation lines are folded back rather than treated as new headers — a long subject
    /// arrives wrapped, and reading it line by line would keep only its first few words.
    /// </remarks>
    public static MailMessage Parse(string raw)
    {
        var subject = new StringBuilder();
        var from = new StringBuilder();
        var to = new StringBuilder();
        var date = new StringBuilder();
        var messageId = new StringBuilder();

        // Kept whole as well, because the body cannot be read without them: the content type carries
        // the multipart boundary and the charset, and the transfer encoding says how the bytes were
        // packed. Seventy-eight per cent of real messages need all three.
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lastName = "";

        StringBuilder? current = null;

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var index = 0;

        for (; index < lines.Length; index++)
        {
            var line = lines[index];

            if (line.Length == 0)
            {
                index++;
                break;
            }

            if (line[0] is ' ' or '\t')
            {
                current?.Append(' ').Append(line.Trim());

                if (lastName.Length > 0)
                {
                    all[lastName] += ' ' + line.Trim();
                }

                continue;
            }

            var colon = line.IndexOf(':');

            if (colon <= 0)
            {
                current = null;
                lastName = "";
                continue;
            }

            var value = line[(colon + 1)..].Trim();

            lastName = line[..colon].Trim();
            all[lastName] = value;

            current = line[..colon].ToLowerInvariant() switch
            {
                "subject" => subject,
                "from" => from,
                "to" => to,
                "date" => date,
                "message-id" => messageId,
                _ => null,
            };

            current?.Append(value);
        }

        var body = index < lines.Length
            ? string.Join('\n', lines[index..])
            : "";

        return new MailMessage(
            Decode(subject.ToString()),
            Decode(from.ToString()),
            Decode(to.ToString()),
            date.ToString(),
            MimeBody.Extract(all, body),
            messageId.ToString().Trim('<', '>', ' '));
    }

    /// <summary>
    /// Everything that travelled with a message, unpacked.
    /// </summary>
    /// <remarks>
    /// A second pass over the same text rather than another return value on <see cref="Parse"/>.
    /// Every caller so far wants a message to read and none of them wants its attachments in memory;
    /// the one that does asks for them, and pays for them then.
    /// </remarks>
    public static IReadOnlyList<MailAttachment> Attachments(string raw)
    {
        var (headers, body) = Mime.SplitHeaders(raw);

        return MimeAttachments.List(headers, body);
    }

    /// <summary>
    /// Turns an encoded header back into readable text.
    /// </summary>
    /// <remarks>
    /// A subject with an accent in it does not travel as text: it arrives as
    /// <c>=?UTF-8?B?…?=</c> or <c>=?UTF-8?Q?…?=</c>. Left alone, every French subject in the index
    /// would be searchable only by its encoded form, which is to say not at all — and this was
    /// written for a Swiss French mailbox.
    /// </remarks>
    public static string Decode(string header)
    {
        if (!header.Contains("=?", StringComparison.Ordinal))
        {
            return header;
        }

        var result = new StringBuilder();
        var rest = header.AsSpan();

        while (true)
        {
            var start = rest.IndexOf("=?", StringComparison.Ordinal);

            if (start < 0)
            {
                result.Append(rest);
                break;
            }

            result.Append(rest[..start]);
            rest = rest[(start + 2)..];

            var end = rest.IndexOf("?=", StringComparison.Ordinal);

            if (end < 0)
            {
                result.Append("=?").Append(rest);
                break;
            }

            var word = rest[..end].ToString();
            rest = rest[(end + 2)..];

            result.Append(DecodeWord(word));
        }

        return result.ToString().Trim();
    }

    /// <summary>One <c>charset?encoding?text</c> group, or the original when it will not decode.</summary>
    private static string DecodeWord(string word)
    {
        var parts = word.Split('?');

        if (parts.Length < 3)
        {
            return word;
        }

        try
        {
            var encoding = Encoding.GetEncoding(parts[0]);
            var text = string.Join('?', parts[2..]);

            if (parts[1].Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return encoding.GetString(Convert.FromBase64String(text));
            }

            if (parts[1].Equals("Q", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeQuoted(text, encoding);
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            // An unknown charset or malformed padding. The encoded form is poor but honest; guessing
            // would put mojibake in somebody's mailbox index.
        }

        return word;
    }

    /// <summary>Quoted-printable, where <c>_</c> is a space and <c>=XX</c> is a byte.</summary>
    private static string DecodeQuoted(string text, Encoding encoding)
    {
        var bytes = new List<byte>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
            {
                bytes.Add((byte)' ');
            }
            else if (text[i] == '=' && i + 2 < text.Length
                     && byte.TryParse(text.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                bytes.Add(value);
                i += 2;
            }
            else
            {
                bytes.Add((byte)text[i]);
            }
        }

        return encoding.GetString(bytes.ToArray());
    }
}
