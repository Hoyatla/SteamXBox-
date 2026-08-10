using System.Security.Cryptography;
using System.Text;

namespace SteamXBox.Tools.Indexing;

/// <summary>
/// A document as the index holds it.
/// </summary>
/// <remarks>
/// The shape the launcher reads, written here so the two cannot drift: <c>id</c>, <c>title</c>,
/// <c>path</c>, <c>content</c>. Anything else an institution wants in the index can be added, and
/// the launcher will ignore it.
/// </remarks>
/// <param name="Id">Derived from the path; see <see cref="IdFor"/>.</param>
/// <param name="Title">The file's name, which is what somebody recognises.</param>
/// <param name="Path">Where it is, as the people who will open it see it.</param>
/// <param name="Content">The text found inside.</param>
public sealed record IndexableDocument(string Id, string Title, string Path, string Content)
{
    /// <summary>
    /// A stable identifier for a path.
    /// </summary>
    /// <remarks>
    /// Meilisearch only accepts letters, digits, hyphens and underscores in a document id, so a path
    /// cannot be one. A hash of the path is used instead, which gives the property that matters:
    /// re-indexing the same file replaces its document rather than adding a second copy. Without
    /// that, every run would double the index.
    ///
    /// <para>
    /// Case-insensitive, because Windows is: <c>C:\Docs\A.pdf</c> and <c>c:\docs\a.pdf</c> are one
    /// file and must not become two documents.
    /// </para>
    /// </remarks>
    public static string IdFor(string path)
    {
        var normalized = (path ?? "").Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>Builds a document from a path and the text found in it.</summary>
    public static IndexableDocument From(string path, string content)
        => new(IdFor(path), System.IO.Path.GetFileName(path), path, content);
}
