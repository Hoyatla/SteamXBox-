using System.Text;
using SteamXBox.Tools.Indexing;
using UglyToad.PdfPig;

namespace SteamXBox.Indexer;

/// <summary>
/// Reads a PDF whose pages are images, by drawing them and recognising the characters.
/// </summary>
/// <remarks>
/// A scanned invoice, a signed form, a twenty-year-old circular passed through a photocopier: a
/// share is full of PDFs that hold no text at all. <see cref="PdfTextExtractor"/> returns nothing
/// for them — correctly, there is nothing to return — and they end up findable by their file name
/// and nothing else, which for a filing system is the same as lost.
///
/// <para>
/// <b>Page by page, not document by document.</b> A scan is rarely uniform: a report often carries a
/// text cover and photographed pages after it, and a document is often a text PDF with two scanned
/// appendices. Deciding for the whole file would either waste minutes drawing pages that already
/// held their text, or skip the only pages that needed it.
/// </para>
///
/// <para>
/// <b>Off unless both halves are present</b>, and it says which one is missing. Drawing without
/// reading, or reading without drawing, is not a degraded mode — it is nothing at all, and a feature
/// that silently does nothing is how "the search does not find this document" becomes impossible to
/// diagnose.
/// </para>
/// </remarks>
public sealed class ScannedPdfTextExtractor : ITextExtractor
{
    /// <summary>
    /// Below this many characters, a page is treated as an image.
    /// </summary>
    /// <remarks>
    /// Not zero. A scanned page routinely carries a scrap of real text — a stamp, a page number
    /// dropped in by the scanner's own software, the odd header — and a page holding six characters
    /// has not been read, it has been glimpsed.
    /// </remarks>
    private const int TextlessPage = 24;

    private readonly IPageRenderer _renderer;
    private readonly ITextRecognizer _recognizer;
    private readonly int _maxPages;

    /// <param name="maxPages">
    /// How many pages of one document may be recognised. A six-hundred-page scan must not hold the
    /// queue for an hour on its own; the cap is what keeps a share indexable overnight.
    /// </param>
    public ScannedPdfTextExtractor(
        IPageRenderer renderer,
        ITextRecognizer recognizer,
        int maxPages = 20)
    {
        _renderer = renderer;
        _recognizer = recognizer;
        _maxPages = maxPages;
    }

    public bool IsAvailable => _renderer.IsAvailable && _recognizer.IsAvailable;

    /// <summary>What this machine can do, in one line for the administrator's log.</summary>
    public string Describe() => $"{_renderer.Describe()} ; {_recognizer.Describe()}";

    public bool Handles(string extension)
        => IsAvailable && extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pages actually recognised during the run, for the report at the end.</summary>
    public int RecognizedPages { get; private set; }

    public string Extract(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length > IndexableFiles.MaxBytes)
            {
                return "";
            }

            // Lenient for the same reason as PdfTextExtractor: a share holds PDFs produced by twenty
            // years of tools, and strict parsing rejects files every reader opens fine.
            using var document = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true });

            var text = new StringBuilder();
            var recognized = 0;
            var number = 0;

            foreach (var page in document.GetPages())
            {
                number++;

                if (text.Length >= IndexableFiles.MaxCharacters)
                {
                    break;
                }

                var written = page.Text;

                if (written.Trim().Length >= TextlessPage)
                {
                    text.Append(written).Append('\n');
                    continue;
                }

                if (recognized >= _maxPages)
                {
                    continue;
                }

                var image = _renderer.RenderPage(path, number);

                if (image is null)
                {
                    continue;
                }

                try
                {
                    var read = _recognizer.Read(image);

                    if (read.Trim().Length > 0)
                    {
                        recognized++;
                        RecognizedPages++;
                        text.Append(read).Append('\n');
                    }
                }
                finally
                {
                    Delete(image);
                }
            }

            return IndexableFiles.Trim(text.ToString());
        }
        catch (Exception)
        {
            // Encrypted, truncated, or not really a PDF. One file, not the run.
            return "";
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Le nettoyage du dossier de travail s'en chargera.
        }
    }
}
