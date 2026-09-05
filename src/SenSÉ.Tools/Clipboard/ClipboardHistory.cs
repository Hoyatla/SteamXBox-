using System.Collections.ObjectModel;

namespace SenSÉ.Tools.Clipboard;

/// <summary>What a remembered clipboard entry holds.</summary>
public enum ClipboardKind
{
    Text,
    Image,
}

/// <summary>One thing that was copied.</summary>
/// <param name="Kind">Whether it is text or an image.</param>
/// <param name="Text">The text, or a short description for an image.</param>
/// <param name="Image">
/// The image, kept as <see cref="object"/> on purpose. The rules below have nothing to say about
/// pixels, and typing this as a WPF <c>BitmapSource</c> would drag the whole presentation framework
/// into a library whose only reason to exist is being testable without it. SenSÉ.Desktop puts a
/// BitmapSource in and WPF binds to it unchanged.
/// </param>
/// <param name="When">When it was copied.</param>
public sealed record ClipboardEntry(ClipboardKind Kind, string Text, object? Image, DateTimeOffset When)
{
    /// <summary>How many characters of a copy are shown in the list before it is cut.</summary>
    public const int PreviewLength = 120;

    /// <summary>A single line for the list, whatever the entry actually is.</summary>
    public string Preview => Kind == ClipboardKind.Image ? Text : Collapse(Text);

    /// <summary>
    /// Flattens a multi-line copy into one readable line.
    /// </summary>
    /// <remarks>
    /// Copied text is very often a whole paragraph or a block of code. Shown raw it would make one
    /// row as tall as the window and push everything else out of reach, which defeats the point of
    /// a history.
    /// </remarks>
    public static string Collapse(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
        {
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        }

        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "…";
    }
}

/// <summary>
/// The remembered copies, newest first.
/// </summary>
/// <remarks>
/// In memory only. A clipboard history on disk is a file containing every password, address and
/// private message the user has copied; that is a liability, and offering it would need encryption,
/// a retention policy and a way to purge it. Until those exist, the history dies with the
/// environment, which is a defensible default rather than a silent risk.
/// </remarks>
public sealed class ClipboardHistory
{
    /// <summary>How many copies are kept. Beyond this the oldest is dropped.</summary>
    public const int Capacity = 40;

    public ObservableCollection<ClipboardEntry> Entries { get; } = [];

    /// <summary>Records a copy, unless it repeats the one already at the top.</summary>
    /// <returns>True when it was added.</returns>
    public bool Add(ClipboardEntry entry)
    {
        // Applications copy the same text more than once — a click that re-copies, a tool that
        // round-trips the clipboard, a paste that rewrites it. Without this the history fills with
        // duplicates of whatever was last touched and the older entries fall off the end.
        //
        // Only against the top entry, not the whole list: copying A, then B, then A again is three
        // real events, and the user expects A back at the top rather than left where it was.
        if (Entries.Count > 0 && IsSame(Entries[0], entry))
        {
            return false;
        }

        Entries.Insert(0, entry);

        while (Entries.Count > Capacity)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        return true;
    }

    public void Clear() => Entries.Clear();

    /// <summary>
    /// Whether two entries are the same copy.
    /// </summary>
    /// <remarks>
    /// Images are never equal to each other here. Comparing them would mean comparing pixels, which
    /// this library cannot do and should not: two screenshots taken a second apart are two events
    /// even when they look identical.
    /// </remarks>
    private static bool IsSame(ClipboardEntry a, ClipboardEntry b)
        => a.Kind == ClipboardKind.Text
           && b.Kind == ClipboardKind.Text
           && string.Equals(a.Text, b.Text, StringComparison.Ordinal);
}
