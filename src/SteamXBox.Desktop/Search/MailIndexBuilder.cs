using System.IO;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// Builds the personal mail index from what is on this machine.
/// </summary>
/// <remarks>
/// Thunderbird for now, because it is what is installed and because it stores messages in a form
/// that can be read honestly: with its Windows Search integration on it writes one
/// <c>.wdseml</c> file per message, in the standard mail format, under the profile's
/// <c>*.mozmsgs</c> folders. Five hundred and forty-two of them on the machine this was written
/// against, two kilobytes each.
///
/// <para>
/// The mailboxes beside them hold everything — three hundred megabytes in a single file — and are
/// not read here. Splitting one into messages is a different job, and doing it badly would put a
/// whole mailbox in the index as one entry that answers every query.
/// </para>
/// </remarks>
public static class MailIndexBuilder
{
    /// <summary>A file bigger than this is not a message anybody will read in a result.</summary>
    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>Whether there is anything on this machine to index.</summary>
    public static bool AnythingToIndex() => Roots().Any(Directory.Exists);

    /// <summary>
    /// Reads every message and returns the index. Slow, and never to be called on the interface
    /// thread.
    /// </summary>
    public static MailIndex Build(Action<string>? log = null)
    {
        var entries = new List<MailEntry>();

        // The mailboxes first, deliberately. They hold everything, while the exported files are only
        // what Thunderbird happened to hand to Windows Search — so when the same message is in both,
        // the copy that is kept is the one whose neighbours are also indexed.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unreadable = 0;
        var duplicates = 0;

        foreach (var root in Roots().Where(Directory.Exists))
        {
            foreach (var mailbox in Walk(root, log).Mailboxes)
            {
                try
                {
                    foreach (var (where, raw) in MboxReader.Messages(mailbox))
                    {
                        Add(MailFile.Parse(raw), mailbox, where.Offset, where.Length);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                    log?.Invoke($"mail index: {Path.GetFileName(mailbox)} unreadable ({exception.GetType().Name}).");
                }
            }
        }

        foreach (var root in Roots().Where(Directory.Exists))
        {
            foreach (var file in Walk(root, log).Messages)
            {
                try
                {
                    if (new FileInfo(file).Length > MaxBytes)
                    {
                        continue;
                    }

                    // Latin-1, like the mailboxes and like the reader: the bytes have to survive
                    // intact so that each part's own charset can be applied to them.
                    Add(MailFile.Parse(File.ReadAllText(file, System.Text.Encoding.Latin1)), file, 0, 0);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                }
            }
        }

        log?.Invoke(
            $"mail index: {entries.Count} message(s), {duplicates} duplicate(s) dropped"
            + (unreadable > 0 ? $", {unreadable} unreadable." : "."));

        return new MailIndex(entries);

        void Add(MailMessage message, string path, long offset, int length)
        {
            var entry = new MailEntry(
                message.Subject.Length > 0 ? message.Subject : "(sans objet)",
                message.From,
                message.Date,
                path,
                offset,
                length,
                message.Searchable,
                message.MessageId);

            if (!seen.Add(entry.Key))
            {
                duplicates++;
                return;
            }

            entries.Add(entry);
        }
    }

    /// <summary>Where messages are looked for.</summary>
    private static IEnumerable<string> Roots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderbird", "Profiles");
    }

    /// <summary>
    /// Every message file below a root.
    /// </summary>
    /// <remarks>
    /// Walked by hand rather than with <c>AllDirectories</c>, for the reason the other two indexes in
    /// this product learned separately: that option abandons the whole walk at the first folder it
    /// cannot open, and a mail profile has locked folders in it while Thunderbird is running.
    /// </remarks>
    private static (IReadOnlyList<string> Messages, IReadOnlyList<string> Mailboxes) Walk(
        string root, Action<string>? log)
    {
        var messages = new List<string>();
        var mailboxes = new List<string>();

        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            string[] files;

            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"mail index: skipped {directory} ({exception.GetType().Name}).");
                continue;
            }

            foreach (var file in files)
            {
                if (MailFile.Extensions.Contains(Path.GetExtension(file)))
                {
                    messages.Add(file);
                    continue;
                }

                // A mailbox has no extension and is recognised by what it opens with, never by its
                // name: the folder beside it holds summaries and settings called the same thing.
                if (Path.GetExtension(file).Length == 0
                    && new FileInfo(file).Length > SmallestMailbox
                    && MboxReader.LooksLikeMailbox(file))
                {
                    mailboxes.Add(file);
                }
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Enqueue(child);
            }
        }

        return (messages, mailboxes);
    }

    /// <summary>Below this a file is not a mailbox worth opening, whatever it starts with.</summary>
    private const long SmallestMailbox = 1024;
}
