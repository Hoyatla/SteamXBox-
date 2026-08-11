using System.Text.Json;

namespace SteamXBox.Tools.Search;

/// <summary>One indexed message.</summary>
/// <param name="Subject">Shown as the result's title.</param>
/// <param name="From">Shown under it, so two messages with the same subject can be told apart.</param>
/// <param name="Date">As the message wrote it; shown, never parsed.</param>
/// <param name="Path">The file holding it: a message of its own, or a whole mailbox.</param>
/// <param name="Offset">Where it starts in that file; zero when the file is one message.</param>
/// <param name="Length">How many bytes it occupies; zero means "to the end of the file".</param>
/// <param name="Text">Subject, correspondents and body, normalised once for matching.</param>
/// <param name="MessageId">What tells one message from another copy of itself.</param>
/// <remarks>
/// The location is what lets a mailbox be a source. A message in <c>Sent Items</c> — three hundred
/// and twenty-three megabytes on the machine this was written against — has no file of its own, so
/// without an offset there is nothing to show but what the index happened to keep. With one, the
/// index holds only enough to <i>find</i> a message and the file still holds everything needed to
/// <i>read</i> it.
/// </remarks>
public sealed record MailEntry(
    string Subject,
    string From,
    string Date,
    string Path,
    long Offset,
    int Length,
    string Text,
    string MessageId = "")
{
    /// <summary>The searchable text, normalised for comparison.</summary>
    public string SearchText { get; } = Search.SearchText.Normalize(Text);

    /// <summary>
    /// What identifies this message for de-duplication.
    /// </summary>
    /// <remarks>
    /// The sender's identifier when there is one. Some messages carry none — drafts, and anything
    /// written by software that skipped the header — so those fall back to the subject, the date and
    /// the sender together, which collide only for a message sent twice in the same minute to the
    /// same person.
    /// </remarks>
    public string Key => MessageId.Length > 0 ? MessageId : $"{Subject}|{Date}|{From}";
}

/// <summary>
/// This person's mail, searchable without anything leaving the machine.
/// </summary>
/// <remarks>
/// A file, not a server, and that is the whole design. The institution's corpus is a Meilisearch
/// instance because it is shared and administered; mail is neither. Meilisearch has no per-document
/// permissions, so a mailbox placed in a shared index is readable by everyone who can query it —
/// which for a school or a company handling sensitive data ends the conversation.
///
/// <para>
/// The measured corpus is five hundred and forty-two messages and three megabytes of text. A
/// hundred times that still fits in memory and searches in a few milliseconds, so a server would buy
/// nothing and cost an installation, a port, a service and a second thing to keep patched.
/// </para>
/// </remarks>
public sealed class MailIndex
{
    /// <summary>Nothing is ranked past this; the tail is never read.</summary>
    public const int MaxResults = 40;

    public MailIndex(IReadOnlyList<MailEntry> entries) => Entries = entries;

    public IReadOnlyList<MailEntry> Entries { get; }

    public int Count => Entries.Count;

    /// <summary>An index with nothing in it, which searches to nothing rather than throwing.</summary>
    public static MailIndex Empty { get; } = new([]);

    /// <summary>
    /// The messages matching every word typed, best first.
    /// </summary>
    /// <remarks>
    /// Every word must appear somewhere in the message, in any order and in any field. That is what
    /// people expect of a mailbox — <c>facture janvier</c> should find a message about an invoice
    /// sent in January whether the month is in the subject or the body — and it is why this does not
    /// reuse the file ranking, which scores a name and a path rather than a body of text.
    /// </remarks>
    public IReadOnlyList<MailEntry> Search(string query)
    {
        var words = SearchText.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return [];
        }

        var matches = new List<(MailEntry Entry, int Score)>();

        foreach (var entry in Entries)
        {
            var score = ScoreOf(entry, words);

            if (score > 0)
            {
                matches.Add((entry, score));
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Entry.Subject, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResults)
            .Select(match => match.Entry)
            .ToArray();
    }

    /// <summary>
    /// How well one message answers the words, or zero when it does not answer at all.
    /// </summary>
    /// <remarks>
    /// A word found in the subject counts for far more than the same word in the body. Mail quotes
    /// itself endlessly — a reply carries every message before it — so body matches are common and
    /// weak, while a subject match is usually the message somebody was actually looking for.
    /// </remarks>
    private static int ScoreOf(MailEntry entry, IReadOnlyList<string> words)
    {
        var subject = SearchText.Normalize(entry.Subject);
        var score = 0;

        foreach (var word in words)
        {
            if (!entry.SearchText.Contains(word, StringComparison.Ordinal))
            {
                // Every word must appear. One missing means this is not the message.
                return 0;
            }

            score += subject.Contains(word, StringComparison.Ordinal) ? 10 : 1;
        }

        return score;
    }

    // ---- Where it lives ----

    /// <summary>The file holding the index, under this user's own profile.</summary>
    /// <remarks>
    /// <c>LocalApplicationData</c> rather than <c>ApplicationData</c>: a roaming profile would copy
    /// somebody's mail index to every machine they sign in to, and onto the server in between.
    /// </remarks>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "mail-index.json");

    /// <summary>Reads the index, or an empty one when there is none yet.</summary>
    public static MailIndex Load(Action<string>? log = null)
    {
        try
        {
            var json = PersonalFile.Read(Path, out var wasPlain, log);

            if (json is null)
            {
                return Empty;
            }

            var entries = JsonSerializer.Deserialize<MailEntry[]>(json);

            if (entries is null)
            {
                return Empty;
            }

            var index = new MailIndex(entries);

            // An index built before this file was protected. Written back at once rather than at the
            // next indexing run, which may be weeks away — the plain copy is the thing being got rid
            // of, and leaving it until convenient is leaving it.
            if (wasPlain)
            {
                log?.Invoke("mail index: found in plain text; writing it back protected.");
                index.Save(log);
            }

            return index;
        }
        catch (Exception exception)
        {
            // A truncated or hand-edited file costs the mail search and nothing else.
            log?.Invoke($"mail index unreadable: {exception.GetType().Name}: {exception.Message}");
            return Empty;
        }
    }

    /// <summary>Writes the index, and says whether it got there.</summary>
    public bool Save(Action<string>? log = null)
    {
        try
        {
            // Encrypted for this Windows account, on this machine. The index keeps each message's
            // readable text so that a search does not reopen a three-hundred-megabyte mailbox for
            // every keystroke — which makes this file a copy of somebody's correspondence, and it
            // sat in plain text until 11 August 2026.
            return PersonalFile.Write(Path, JsonSerializer.Serialize(Entries), log);
        }
        catch (Exception exception)
        {
            log?.Invoke($"mail index could not be saved: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>Removes the index from the machine.</summary>
    /// <remarks>
    /// Exists because it must: a personal mail index has to be deletable by the person it belongs
    /// to, and a product that can build one without offering to remove it is not defensible.
    /// </remarks>
    public static bool Forget(Action<string>? log = null)
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
                log?.Invoke("mail index deleted.");
            }

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"mail index could not be deleted: {exception.Message}");
            return false;
        }
    }
}
