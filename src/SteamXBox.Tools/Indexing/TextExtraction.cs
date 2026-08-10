namespace SteamXBox.Tools.Indexing;

/// <summary>Pulls readable text out of one kind of file.</summary>
/// <remarks>
/// An interface because the formats that matter — PDF, Word, Excel — need libraries this assembly
/// deliberately does not carry. Their licences have to be approved before they enter a product
/// distributed in a paid edition, and until then the pipeline works on everything else rather than
/// waiting.
/// </remarks>
public interface ITextExtractor
{
    /// <summary>Whether this extractor handles the file, judged on its extension.</summary>
    bool Handles(string extension);

    /// <summary>The text, or empty when there is none to be had.</summary>
    string Extract(string path);
}

/// <summary>
/// Which files are worth indexing at all, and how much of them.
/// </summary>
/// <remarks>
/// The judgement rather than the reading, so it can be tested without a disk. What it decides is
/// what an institution's index will contain, which is the part somebody will ask about.
/// </remarks>
public static class IndexableFiles
{
    /// <summary>
    /// Past this, a file is not a document.
    /// </summary>
    /// <remarks>
    /// A hundred megabytes of text is a log or a database dump, not something anybody searches for
    /// by content. Reading it costs minutes and fills the index with noise that outranks real
    /// documents by sheer volume of matching words.
    /// </remarks>
    public const long MaxBytes = 32 * 1024 * 1024;

    /// <summary>
    /// How much text is kept from one file.
    /// </summary>
    /// <remarks>
    /// Meilisearch indexes what it is given, and a book pushed whole makes one document that matches
    /// almost any query. The first part of a document is also where its subject is stated.
    /// </remarks>
    public const int MaxCharacters = 200_000;

    /// <summary>Folders never descended into, whatever they contain.</summary>
    /// <remarks>
    /// Version control and dependency directories hold thousands of files nobody searches for, and
    /// they are the reason a naive indexer takes hours and returns rubbish.
    /// </remarks>
    public static readonly IReadOnlySet<string> SkippedFolders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "node_modules", "bin", "obj", "__pycache__",
            "$RECYCLE.BIN", "System Volume Information",
        };

    /// <summary>Whether a folder should be descended into.</summary>
    public static bool ShouldDescend(string folderName)
        => !SkippedFolders.Contains(folderName) && !folderName.StartsWith('.');

    /// <summary>Cuts text to what is worth indexing.</summary>
    public static string Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        return text.Length <= MaxCharacters ? text : text[..MaxCharacters];
    }
}

/// <summary>
/// Reads the formats that are already text.
/// </summary>
/// <remarks>
/// No dependency, so the pipeline is useful the day it is written. On a school's share this alone
/// covers notes, exports, subtitles and anything anybody saved as plain text; the formats that need
/// a library are added beside it without changing anything here.
/// </remarks>
public sealed class PlainTextExtractor : ITextExtractor
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".csv", ".tsv", ".log",
            ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg",
            ".htm", ".html", ".srt", ".vtt", ".rtf",
        };

    public bool Handles(string extension) => Extensions.Contains(extension);

    public string Extract(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length > IndexableFiles.MaxBytes)
            {
                return "";
            }

            // Detected from the byte-order mark when there is one, UTF-8 otherwise. A share holds
            // files written by twenty years of different tools, and refusing the ones without a mark
            // would drop most of them.
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

            return IndexableFiles.Trim(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // One unreadable file is not a reason to stop indexing a share of forty thousand.
            return "";
        }
    }
}
