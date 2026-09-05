namespace SenSÉ.Tools.Declarative;

/// <summary>What a tool asked the save panel to offer.</summary>
/// <param name="SuggestedName">The name shown when the panel opens, without a path.</param>
/// <param name="Extension">Extension the file should carry, with or without its dot.</param>
public readonly record struct SaveRequest(string SuggestedName, string Extension);

/// <summary>
/// Permission to write one file, granted because the user pointed at it.
/// </summary>
/// <remarks>
/// The powerbox, in the sense macOS gives the word. A tool never sees the file system: it asks the
/// host for the save panel, the user designates a file, and the host hands back permission to write
/// <b>that one</b>. The tool learns no directory, no neighbouring file, and nothing about what it
/// was not shown.
///
/// <para>
/// This is what lets the policy forbid a tool from writing on its own initiative without also
/// forbidding it from saving anything — the two were the same sentence in the first draft of the
/// rules, and that draft made a text editor impossible.
/// </para>
///
/// <para>
/// A grant is deliberately not reusable and not storable. The host issues one per save, and a tool
/// that wants to write again asks again. A grant kept between sessions would be a directory
/// permission wearing a file's name.
/// </para>
/// </remarks>
public sealed class FileGrant
{
    private FileGrant(string path) => Path = path;

    /// <summary>The one file this grant allows, as an absolute path.</summary>
    public string Path { get; }

    /// <summary>Whether it has been spent. A grant covers one write.</summary>
    public bool Used { get; private set; }

    /// <summary>
    /// Issued by the host once the user has designated a file. Null if no file was designated.
    /// </summary>
    /// <remarks>
    /// Only the host calls this, and only from its own save panel. There is no way for a tool to
    /// make one: a grant a tool could mint would grant nothing.
    /// </remarks>
    public static FileGrant? Issue(string? chosenPath)
    {
        if (string.IsNullOrWhiteSpace(chosenPath))
        {
            return null;
        }

        try
        {
            var full = System.IO.Path.GetFullPath(chosenPath);

            // A path with no file name is a directory, and a directory is precisely what a grant
            // must never be.
            return string.IsNullOrEmpty(System.IO.Path.GetFileName(full)) ? null : new FileGrant(full);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Whether this grant covers <paramref name="path"/>.</summary>
    /// <remarks>
    /// Compared on the resolved absolute path, so a tool cannot reach a different file by writing to
    /// the granted one with <c>..</c> in the middle of it.
    /// </remarks>
    public bool Covers(string? path)
    {
        if (Used || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return string.Equals(
                System.IO.Path.GetFullPath(path),
                Path,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Marks the grant spent, after the host has written the file.</summary>
    public void Spend() => Used = true;

    /// <summary>
    /// The name the panel should offer, cleaned of anything that is not a file name.
    /// </summary>
    /// <remarks>
    /// A tool proposes a name, not a location. A suggestion carrying a path — or <c>..</c> — would be
    /// a tool choosing where to write while appearing to choose what to call it.
    /// </remarks>
    public static string SafeSuggestion(SaveRequest request)
    {
        var name = System.IO.Path.GetFileName(request.SuggestedName ?? "") ?? "";

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();

        if (name.Length == 0)
        {
            name = "document";
        }

        var extension = (request.Extension ?? "").Trim().TrimStart('.');

        if (extension.Length == 0)
        {
            return name;
        }

        return name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.{extension}";
    }
}
