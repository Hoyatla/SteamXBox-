using System.Text;

namespace SenSÉ.Tools.Search;

/// <summary>
/// Fetches one message back from wherever it was indexed from.
/// </summary>
/// <remarks>
/// Read at the moment it is shown, never kept. The index holds enough to <i>find</i> a message —
/// a few thousand characters, stripped of markup and cut short — and that is the wrong thing to put
/// on screen: it is the searchable form, not the readable one. The file still has the message.
///
/// <para>
/// It also keeps the index small. Storing every message in full would have made the index as large
/// as the mailboxes it came from, for text that is read once in a hundred searches.
/// </para>
/// </remarks>
public static class MailReader
{
    /// <summary>Beyond this the preview is scrolled, not longer.</summary>
    public const int MaxPreviewCharacters = 8_000;

    /// <summary>
    /// Reads the message an entry points at.
    /// </summary>
    /// <remarks>
    /// A length of zero means the file is the message; anything else is a slice of a mailbox. Latin-1
    /// again, for the reason the mailbox reader uses it: the bytes are handed back exactly as they
    /// were counted, and the message decodes its own text afterwards.
    /// </remarks>
    public static MailMessage? Read(MailEntry entry, Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(entry.Path))
            {
                log?.Invoke($"mail preview: {entry.Path} is gone.");
                return null;
            }

            // Latin-1 for both, and that is required rather than incidental. It maps every byte to
            // one character and back, so the message's own charset — declared per part, and absent
            // on most — can still be applied afterwards. Reading as UTF-8 here would decode the
            // bytes twice and turn every accent into mojibake.
            var raw = entry.Length <= 0
                ? File.ReadAllText(entry.Path, Encoding.Latin1)
                : Slice(entry.Path, entry.Offset, entry.Length);

            return MailFile.Parse(raw);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // A mailbox being written by Thunderbird while this reads it, or an index built before
            // the mailbox was compacted. One preview is lost; the result stays in the list.
            log?.Invoke($"mail preview failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Reads one message out of a mailbox without reading the mailbox.</summary>
    private static string Slice(string path, long offset, int length)
    {
        using var stream = File.OpenRead(path);

        if (offset >= stream.Length)
        {
            // The mailbox shrank since the index was built — Thunderbird compacts them. Better to
            // show nothing than a fragment of whatever now sits at that offset.
            return "";
        }

        stream.Seek(offset, SeekOrigin.Begin);

        var wanted = (int)Math.Min(length, stream.Length - offset);
        var buffer = new byte[wanted];
        var read = stream.ReadAtLeast(buffer, wanted, throwOnEndOfStream: false);

        return Encoding.Latin1.GetString(buffer, 0, read);
    }

    /// <summary>
    /// The message laid out for reading.
    /// </summary>
    /// <remarks>
    /// The separator line a mailbox puts before every message is dropped: it is the format's
    /// bookkeeping, not part of what was written.
    /// </remarks>
    public static string ToPreview(MailMessage message)
    {
        var text = new StringBuilder();

        Append("Objet", message.Subject);
        Append("De", message.From);
        Append("À", message.To);
        Append("Date", message.Date);

        if (text.Length > 0)
        {
            text.AppendLine();
        }

        // Collapsed here and nowhere else: the index keeps the addresses so they stay searchable,
        // and only what is put on screen loses them.
        //
        // Before the cut, not after. Trimming first would spend the eight thousand characters on
        // tracking addresses and stop before the prose that follows them.
        var body = IndexableMail.CollapseLinks(message.Body);

        text.Append(body.Length > MaxPreviewCharacters
            ? string.Concat(body.AsSpan(0, MaxPreviewCharacters), "\n\n[…]")
            : body);

        return text.ToString();

        void Append(string label, string value)
        {
            if (value.Length > 0)
            {
                text.Append(label).Append(" : ").AppendLine(value);
            }
        }
    }
}
