using System.Text;

namespace SteamXBox.Tools.Search;

/// <summary>Where one message sits inside a file.</summary>
/// <param name="Offset">Byte offset of its first line.</param>
/// <param name="Length">Byte length, up to but not including the next message.</param>
public readonly record struct MailLocation(long Offset, int Length);

/// <summary>
/// Reads the mailboxes Thunderbird keeps everything in.
/// </summary>
/// <remarks>
/// A mailbox is every message it has ever held, concatenated into one file with no extension —
/// <c>INBOX</c>, <c>Sent Items</c>. On the machine this was written against those files are three
/// hundred and thirty-eight, a hundred and thirty-two and ninety megabytes. The per-message
/// <c>.wdseml</c> files beside them cover only what Thunderbird exported for Windows Search, five
/// hundred and forty-two messages; everything older is here and nowhere else.
///
/// <para>
/// <b>Streamed, never loaded.</b> Reading a three-hundred-megabyte mailbox into a string to split it
/// would cost more memory than the whole environment uses, for a file that is mostly quoted history
/// and base64 attachments.
/// </para>
/// </remarks>
public static class MboxReader
{
    /// <summary>A message longer than this is attachments, not correspondence.</summary>
    private const int MaxMessageBytes = 1024 * 1024;

    /// <summary>
    /// Whether a file looks like a mailbox rather than one of the index files beside it.
    /// </summary>
    /// <remarks>
    /// By content, not by name. A profile folder holds <c>.msf</c> summaries, <c>.dat</c> files and
    /// folders named like the mailboxes; the one thing that identifies a mailbox is that it opens
    /// with the separator line every message in it begins with.
    /// </remarks>
    public static bool LooksLikeMailbox(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, Encoding.Latin1);

            var first = reader.ReadLine();

            return first is not null && IsSeparator(first);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Every message in a mailbox, as its location and its raw text.
    /// </summary>
    /// <remarks>
    /// Read as Latin-1 rather than UTF-8, and that is deliberate rather than careless. A mailbox is
    /// not one encoding: each message declares its own, and its headers carry theirs encoded again
    /// inside them. Latin-1 maps every byte to exactly one character and back, so the offsets stay
    /// exact and nothing is destroyed; the text is decoded properly per message afterwards, which is
    /// the only level at which the question has an answer.
    /// </remarks>
    public static IEnumerable<(MailLocation Where, string Raw)> Messages(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.Latin1, false, 1 << 16);

        var message = new StringBuilder();
        long offset = 0;
        long start = 0;
        var started = false;

        while (reader.ReadLine() is { } line)
        {
            // The reader consumed the line and its terminator. Mailboxes are written with CRLF here,
            // and counting one byte would drift the offset by one per line — a message located a
            // thousand lines in would be read from the middle of another.
            var consumed = line.Length + 2;

            if (IsSeparator(line))
            {
                if (started)
                {
                    yield return (new MailLocation(start, (int)Math.Min(offset - start, int.MaxValue)), message.ToString());
                }

                message.Clear();
                start = offset;
                started = true;
            }
            else if (started && message.Length < MaxMessageBytes)
            {
                message.Append(line).Append('\n');
            }

            offset += consumed;
        }

        if (started)
        {
            yield return (new MailLocation(start, (int)Math.Min(offset - start, int.MaxValue)), message.ToString());
        }
    }

    /// <summary>
    /// Whether a line is the one that begins a message.
    /// </summary>
    /// <remarks>
    /// <c>From </c> at the start of a line, with no colon after the word — the separator is not a
    /// header. A body line reading "From here it gets complicated" would be mistaken for one, which
    /// is a known flaw of the format itself; requiring what follows to look like an address and a
    /// date removes almost all of it.
    /// </remarks>
    private static bool IsSeparator(string line)
    {
        if (!line.StartsWith("From ", StringComparison.Ordinal))
        {
            return false;
        }

        // "From sender@example.org Mon Jan  1 00:00:00 2024" — two runs of non-space at least, and a
        // day name in the second field is what an ordinary sentence will not have.
        var rest = line[5..].TrimStart();
        var space = rest.IndexOf(' ');

        if (space <= 0)
        {
            return false;
        }

        var after = rest[(space + 1)..].TrimStart();

        return after.Length >= 3 && DayNames.Any(day =>
            after.StartsWith(day, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] DayNames =
        ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
}
