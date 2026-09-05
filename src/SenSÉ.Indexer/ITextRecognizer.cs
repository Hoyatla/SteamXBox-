namespace SenSÉ.Indexer;

/// <summary>
/// Reads the characters in an image.
/// </summary>
/// <remarks>
/// The other half of reading a scanned document, and the one that decides what the search finds. A
/// contract rather than a call to one engine: what runs is detected on the machine, and nothing here
/// is bundled with the product.
///
/// <para>
/// <b>Detected, never bundled</b> — the rule this project already applies to LibreOffice, for the
/// reason written beside it: an engine that parses files coming from outside is an attack surface,
/// and nobody wants to become the party that ships its security fixes to a school. A recognizer the
/// administrator installed is a recognizer the administrator updates.
/// </para>
/// </remarks>
public interface ITextRecognizer
{
    /// <summary>Whether this machine has what it takes. False turns the whole feature off, loudly.</summary>
    bool IsAvailable { get; }

    /// <summary>What was found, and in which languages, for the log.</summary>
    string Describe();

    /// <summary>
    /// Reads an image and returns what it could make of it, or an empty string.
    /// </summary>
    /// <remarks>
    /// Empty rather than an exception when the image holds no text: a blank scanned page is ordinary
    /// in any share, and it is not a fault to report.
    /// </remarks>
    string Read(string imagePath);
}
