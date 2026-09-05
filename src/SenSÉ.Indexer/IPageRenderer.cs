namespace SenSÉ.Indexer;

/// <summary>
/// Turns the pages of a document into images, for the pages that carry no text.
/// </summary>
/// <remarks>
/// A contract, deliberately naming no technology. The renderer of the day is detected on the
/// machine and can be replaced without any of the indexer knowing — which is the point: the day the
/// host is no longer Windows, only the implementation changes.
///
/// <para>
/// Separate from <see cref="ITextRecognizer"/> because the two halves fail independently. A machine
/// can have a renderer and no recognizer, and the difference decides what to tell the administrator:
/// "nothing to read the image with" is not "nothing to draw the page with".
/// </para>
/// </remarks>
public interface IPageRenderer
{
    /// <summary>Whether this machine has what it takes. False turns the whole feature off, loudly.</summary>
    bool IsAvailable { get; }

    /// <summary>What was found, for the log. An administrator needs to know which tool ran.</summary>
    string Describe();

    /// <summary>
    /// Renders one page to a PNG file and returns its path, or null when the page cannot be drawn.
    /// </summary>
    /// <param name="path">The document.</param>
    /// <param name="page">The page number, counted from one, as every PDF tool counts them.</param>
    /// <returns>A path in a temporary folder the caller must delete, or null.</returns>
    string? RenderPage(string path, int page);
}
