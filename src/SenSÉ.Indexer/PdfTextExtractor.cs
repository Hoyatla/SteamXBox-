using System.Text;
using SenSÉ.Tools.Indexing;
using UglyToad.PdfPig;

namespace SenSÉ.Indexer;

/// <summary>
/// Reads the text of a PDF.
/// </summary>
/// <remarks>
/// PdfPig, Apache 2.0. It decodes the format itself: no Acrobat, no Reader, nothing installed on the
/// machine. That matters more than the licence does — the indexer runs on a server, and a server has
/// no PDF reader.
///
/// <para>
/// A PDF is a drawing format, not a text one. What comes out is the text that was placed on the
/// page, in the order the file lists it, which is usually reading order and sometimes is not. Good
/// enough to find a document by its words; not something to display as the document's content.
/// </para>
/// </remarks>
public sealed class PdfTextExtractor : ITextExtractor
{
    public bool Handles(string extension)
        => extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public string Extract(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length > IndexableFiles.MaxBytes)
            {
                return "";
            }

            // Lenient on purpose. A share holds PDFs produced by twenty years of tools, plenty of
            // them slightly malformed; strict parsing rejects files that every reader opens fine.
            using var document = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true });

            var text = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                // Stopped as soon as there is enough. A thousand-page manual costs a minute to read
                // whole, and everything past the cap is discarded anyway.
                if (text.Length >= IndexableFiles.MaxCharacters)
                {
                    break;
                }

                text.Append(page.Text).Append('\n');
            }

            return IndexableFiles.Trim(text.ToString());
        }
        catch (Exception exception)
        {
            // Encrypted, truncated, or not really a PDF. One file, not the run: a share always has
            // a few, and stopping on them would index nothing.
            Console.Error.WriteLine($"PDF illisible : {path} ({exception.GetType().Name})");
            return "";
        }
    }
}
