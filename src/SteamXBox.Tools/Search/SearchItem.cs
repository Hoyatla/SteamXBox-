using System.Globalization;
using System.Text;

namespace SteamXBox.Tools.Search;

/// <summary>What kind of thing a result is, which the ranking and the icon both read.</summary>
public enum SearchItemKind
{
    File,
    Folder,
    Application,
    Drive,

    /// <summary>A result from the web, which lives nowhere on this machine.</summary>
    Web,

    /// <summary>A document from the institution's corpus, found by its content.</summary>
    Document,
}

/// <summary>
/// One entry of the index: something the user can find and open.
/// </summary>
/// <remarks>
/// <see cref="SearchName"/> and <see cref="SearchPath"/> are the normalised forms, computed once when
/// the entry is built. The ranking runs over every entry on every keystroke, so normalising there
/// would redo the same work thousands of times per letter typed.
/// </remarks>
/// <param name="Name">Shown to the user, as the file system spells it.</param>
/// <param name="Path">Full path, or the launch target for a Store application.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Priority">Base score before the query is considered.</param>
/// <param name="LastWrite">When it last changed, which nudges recent things upwards.</param>
/// <param name="IsUserPath">Under the user's own folders.</param>
/// <param name="IsWindowsPath">Under the Windows directory: findable, but pushed down.</param>
public sealed record SearchItem(
    string Name,
    string Path,
    SearchItemKind Kind,
    int Priority,
    DateTime LastWrite,
    bool IsUserPath = false,
    bool IsWindowsPath = false)
{
    /// <summary>The name, normalised for comparison.</summary>
    public string SearchName { get; } = SearchText.Normalize(Name);

    /// <summary>The path, normalised for comparison.</summary>
    public string SearchPath { get; } = SearchText.Normalize(Path);
}

/// <summary>Text reduced to what matters for matching.</summary>
public static class SearchText
{
    /// <summary>
    /// Lower case, without accents.
    /// </summary>
    /// <remarks>
    /// Decomposed to <c>FormD</c>, non-spacing marks dropped, recomposed. That is what makes
    /// <c>reglages</c> find <c>Réglages</c> — on a keyboard where the accent is a dead key, requiring
    /// it would mean the user has to type the answer to find it.
    /// </remarks>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
