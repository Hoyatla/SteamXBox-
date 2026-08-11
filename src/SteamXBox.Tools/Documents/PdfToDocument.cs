using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Turns a PDF into an editable document, by taking its text rather than redrawing its pages.
/// </summary>
/// <remarks>
/// LibreOffice can convert a PDF, and the result is unusable. Measured on its own welcome guide:
/// a three-megabyte <c>.docx</c> whose <c>document.xml</c> is five megabytes and holds
/// <b>five thousand seven hundred shapes, three thousand nine hundred pictures and one thousand
/// four hundred drawings — for four hundred and forty-one paragraphs of text</b>. Every line of the
/// page had become a floating box, so Word laid out ten thousand objects for one turn of the mouse
/// wheel and took seconds to do it.
///
/// <para>
/// The reason is structural rather than a setting: LibreOffice opens a PDF in Draw, so its
/// conversion is a drawing of the page, not the page's text. No option changes that.
/// </para>
///
/// <para>
/// <b>The trade, stated plainly.</b> This keeps the words and loses the layout. That is what "pdf to
/// word" is usually wanted for — text somebody can edit — and a light document that can be worked
/// on beats a faithful one that cannot be scrolled. Anyone who needs the layout should keep the PDF.
/// </para>
/// </remarks>
public static class PdfToDocument
{
    /// <summary>Which targets this route can write.</summary>
    /// <remarks>
    /// Every prose format, from one reading, and none of them through LibreOffice. The proprietary
    /// one and the free one have to come out the same: a customer who chooses OpenDocument because
    /// it costs nothing should not be the one who gets the unusable document.
    /// </remarks>
    public static IReadOnlySet<string> Formats { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "docx", "odt", "rtf", "html", "txt",
        };

    /// <summary>Whether a conversion should take this route rather than LibreOffice.</summary>
    public static bool Handles(string input, string format)
        => Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           && Formats.Contains(format);

    /// <summary>What goes into the document, in reading order.</summary>
    /// <param name="Text">A paragraph, or empty when this is a picture.</param>
    /// <param name="Image">The picture's bytes, or null when this is text.</param>
    /// <param name="Jpeg">Whether those bytes are a JPEG rather than a PNG.</param>
    /// <param name="WidthPt">How wide the picture was on the page, in points.</param>
    /// <param name="HeightPt">How tall it was.</param>
    /// <remarks>
    /// Visible to the assembly because four writers consume it now — Word, OpenDocument, rich text
    /// and web. The reading is the hard part and is done once; what differs between those formats is
    /// only how the same paragraphs and pictures are spelled.
    /// </remarks>
    internal readonly record struct Piece(
        string Text,
        byte[]? Image,
        bool Jpeg,
        double WidthPt,
        double HeightPt)
    {
        public bool IsPicture => Image is not null;
    }

    /// <summary>Reads the PDF and writes the document beside it.</summary>
    public static LibreOffice.Result Convert(string input, string format, string outputDirectory)
    {
        try
        {
            var pieces = Read(input);

            if (!pieces.Any(piece => piece.Text.Length > 0))
            {
                return new LibreOffice.Result(
                    "", "Ce PDF ne contient pas de texte : c'est une image, il faudrait le reconnaître d'abord.");
            }

            Directory.CreateDirectory(outputDirectory);

            var output = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(input) + "." + format.ToLowerInvariant());

            switch (format.ToLowerInvariant())
            {
                case "txt":
                    // A picture cannot go into a text file, and dropping it silently would leave a
                    // hole where the reader has no way of knowing something was removed.
                    File.WriteAllText(output, string.Join(
                        Environment.NewLine + Environment.NewLine,
                        pieces.Select(piece => piece.IsPicture ? "[image]" : piece.Text)));
                    break;

                case "odt":
                    OpenDocumentText.Write(output, pieces);
                    break;

                case "rtf":
                    PdfToRichText.Write(output, pieces);
                    break;

                case "html":
                    PdfToWeb.Write(output, Path.GetFileNameWithoutExtension(input), pieces);
                    break;

                default:
                    WriteDocx(output, pieces);
                    break;
            }

            return new LibreOffice.Result(output, "");
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
        }
    }

    /// <summary>
    /// The PDF's text, one entry per paragraph.
    /// </summary>
    /// <remarks>
    /// Read page by page with the layout-aware extractor, which puts the words back in reading
    /// order — a PDF stores them in the order they were drawn, which is not the order they are read
    /// in, and taking them raw gives a column of a two-column page interleaved with the other.
    ///
    /// <para>
    /// Lines are joined into paragraphs on the blank line, because a PDF has no paragraphs: it has
    /// lines at coordinates. Joining on the blank line is the one rule that holds across documents
    /// without guessing at indentation.
    /// </para>
    /// </remarks>
    internal static List<Piece> Read(string input)
    {
        var pieces = new List<Piece>();

        using var pdf = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = true });

        // The text first, for every page, before a single picture is touched. Measured on a
        // six-megabyte scan: thirty-two seconds spent re-encoding full-page images, then thrown away
        // when the document turned out to have no text at all. Asking the cheap question first
        // brought that to a third of a second.
        //
        // Asked of the whole document rather than of each page, which was the first attempt and was
        // wrong in the other direction: it dropped the cover of a book, and every full-page figure
        // with it. A page carrying only a picture is normal; a document carrying only pictures is
        // a scan, and that is the one this route cannot convert.
        var text = new List<List<string>>();

        foreach (var page in pdf.GetPages())
        {
            text.Add(Paragraphs(page));
        }

        if (text.All(paragraphs => paragraphs.Count == 0))
        {
            return pieces;
        }

        for (var number = 1; number <= text.Count; number++)
        {
            var paragraphs = text[number - 1];
            var pictures = Pictures(pdf.GetPage(number));

            if (pictures.Count == 0)
            {
                pieces.AddRange(paragraphs.Select(paragraph => new Piece(paragraph, null, false, 0, 0)));
                continue;
            }

            Interleave(pieces, paragraphs, pictures);
        }

        return pieces;
    }

    /// <summary>One page's text, one entry per paragraph.</summary>
    private static List<string> Paragraphs(Page page)
    {
        var paragraphs = new List<string>();
        var text = ContentOrderTextExtractor.GetText(page);

        if (string.IsNullOrWhiteSpace(text))
        {
            return paragraphs;
        }

        var current = new StringBuilder();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                Flush(paragraphs, current);
                continue;
            }

            if (current.Length > 0 && Continues(current, line))
            {
                current.Append(' ').Append(line);
                continue;
            }

            Flush(paragraphs, current);
            current.Append(line);
        }

        Flush(paragraphs, current);

        return paragraphs;
    }

    /// <summary>
    /// The pictures on a page, with what was drawn above each one.
    /// </summary>
    /// <remarks>
    /// Anything under twenty points on a side is left out. At that size a PDF image is a bullet, a
    /// rule, a logo in a header or a spacer — every document is full of them, and reproducing them
    /// would bring back the ten thousand objects this route exists to avoid.
    ///
    /// <para>
    /// <b>JPEG first, PNG second.</b> PdfPig's PNG conversion refuses a picture stored with
    /// <c>DCTDecode</c>, which is most photographs in most PDFs — the first version of this silently
    /// dropped the only picture in the test document for that reason. A <c>DCTDecode</c> stream is
    /// already a JPEG file, byte for byte, so it is taken as it stands and nothing is re-encoded.
    /// </para>
    /// </remarks>
    private static List<Picture> Pictures(Page page)
    {
        var pictures = new List<Picture>();
        var words = page.GetWords().ToList();

        foreach (var image in page.GetImages())
        {
            var bounds = image.BoundingBox;

            if (bounds.Width < 20 || bounds.Height < 20)
            {
                continue;
            }

            var raw = image.RawMemory.ToArray();
            var jpeg = IsJpeg(raw);

            byte[] bytes;

            if (jpeg)
            {
                bytes = raw;
            }
            else if (image.TryGetPng(out var png) && png is { Length: > 0 })
            {
                bytes = png;
            }
            else
            {
                continue;
            }

            // A PDF measures upward from the foot of the page, so a word sitting higher than the
            // picture's top edge has the larger coordinate. Counting them is how the picture finds
            // its place in the text: it is the amount of reading that happens before it.
            var above = words.Count(word => word.BoundingBox.Bottom >= bounds.Top);

            pictures.Add(new Picture(bytes, jpeg, bounds.Width, bounds.Height, above));
        }

        return pictures;
    }

    /// <summary>A picture on a page, and how much reading comes before it.</summary>
    private readonly record struct Picture(
        byte[] Bytes,
        bool Jpeg,
        double Width,
        double Height,
        int WordsAbove);

    /// <summary>Whether these bytes open a JPEG file.</summary>
    private static bool IsJpeg(byte[] bytes)
        => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    /// <summary>
    /// Puts a page's pictures back among its paragraphs, as near as the text can say.
    /// </summary>
    /// <remarks>
    /// <b>Why not exactly.</b> Placing a picture exactly means placing it at a coordinate, and a
    /// document made of positioned boxes is the one this route was written to replace. So the
    /// picture goes into the flow, between two paragraphs, and the question becomes which two.
    ///
    /// <para>
    /// It is answered by counting words. The extractor gives the page's words with their positions,
    /// so the number of words above the picture is known; the paragraphs are then counted off until
    /// that many words have gone by, and the picture is inserted there. A picture at the top of a
    /// page lands at the top, one at the foot lands at the foot, one beside the third paragraph
    /// lands by the third paragraph.
    /// </para>
    ///
    /// <para>
    /// It is an estimate and it will be wrong on a page laid out in columns, where reading order and
    /// height disagree. Wrong by a paragraph, in a document that opens and scrolls — which is the
    /// trade this whole route makes.
    /// </para>
    /// </remarks>
    private static void Interleave(
        List<Piece> pieces,
        List<string> paragraphs,
        List<Picture> pictures)
    {
        var counts = paragraphs.Select(CountWords).ToList();

        // Sorted so that two pictures landing on the same boundary keep their order down the page.
        var placed = pictures
            .OrderBy(picture => picture.WordsAbove)
            .Select(picture => (Index: PlaceAt(counts, picture.WordsAbove), Picture: picture))
            .ToList();

        for (var index = 0; index <= paragraphs.Count; index++)
        {
            foreach (var (_, picture) in placed.Where(entry => entry.Index == index))
            {
                pieces.Add(new Piece("", picture.Bytes, picture.Jpeg, picture.Width, picture.Height));
            }

            if (index < paragraphs.Count)
            {
                pieces.Add(new Piece(paragraphs[index], null, false, 0, 0));
            }
        }
    }

    /// <summary>
    /// Which paragraph boundary a given number of words falls on.
    /// </summary>
    /// <remarks>
    /// The whole of the placement rule, and public because it is the one part of this route a user
    /// can see the result of and disagree with. Given how much of the page has been read before a
    /// picture, it says which gap in the text the picture belongs in.
    /// </remarks>
    public static int PlaceAt(IReadOnlyList<int> wordsPerParagraph, int wordsAbove)
    {
        var running = 0;

        for (var index = 0; index < wordsPerParagraph.Count; index++)
        {
            if (running >= wordsAbove)
            {
                return index;
            }

            running += wordsPerParagraph[index];
        }

        return wordsPerParagraph.Count;
    }

    private static int CountWords(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Whether a line carries on the one before it rather than starting something new.
    /// </summary>
    /// <remarks>
    /// A PDF has no paragraphs — it has lines at coordinates — so this has to be inferred, and the
    /// first attempt got it badly wrong. Joining until a blank line seemed obvious and produced two
    /// paragraphs for a six-hundred-kilobyte document: extracted PDF text has almost no blank lines,
    /// so every page became one block.
    ///
    /// <para>
    /// A sentence that was wrapped ends without punctuation and continues in lower case. Anything
    /// else — a line ending in a full stop, a new line starting with a capital, a bullet, a number —
    /// is treated as its own paragraph. It is a guess, and it errs towards too many paragraphs
    /// rather than too few: a split paragraph is a keystroke to repair, a page-long block is not.
    /// </para>
    /// </remarks>
    private static bool Continues(StringBuilder previous, string line)
    {
        var last = previous[^1];

        if (last is '.' or '!' or '?' or ':' or ';' or '•' or '-')
        {
            return false;
        }

        var first = line[0];

        return !char.IsUpper(first) && !char.IsDigit(first) && first is not ('•' or '-' or '–' or '*');
    }

    private static void Flush(List<string> paragraphs, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        var clean = Printable(current.ToString());

        if (clean.Length > 0)
        {
            paragraphs.Add(clean);
        }

        current.Clear();
    }

    /// <summary>
    /// Drops the characters a document cannot contain.
    /// </summary>
    /// <remarks>
    /// A real PDF stopped the whole conversion with "hexadecimal value 0x16 is an invalid
    /// character": a Word document is XML, and XML forbids almost every control character. PDFs
    /// carry them — from the fonts, from the producer, from whatever made the file — and one of them
    /// anywhere would otherwise lose the entire document rather than one character.
    /// </remarks>
    private static string Printable(string text)
    {
        if (!text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
        {
            return text;
        }

        var clean = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (!char.IsControl(character) || character is '\t')
            {
                clean.Append(character);
            }
        }

        return clean.ToString().Trim();
    }

    /// <summary>
    /// Writes the pieces as a Word document.
    /// </summary>
    /// <remarks>
    /// Paragraphs and inline pictures, and nothing else — no frames, no shapes, no anchoring. That is
    /// still the whole point: the document Word struggled with had ten thousand positioned objects,
    /// and this has none. A picture here sits in the text like a letter does, so it moves when the
    /// text above it is edited instead of staying behind at a coordinate.
    /// </remarks>
    private static void WriteDocx(string output, IReadOnlyList<Piece> pieces)
    {
        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();

        var body = main.Document.AppendChild(new Body());
        var picture = 0U;

        foreach (var piece in pieces)
        {
            if (piece.IsPicture)
            {
                body.AppendChild(PictureParagraph(main, piece, ++picture));
                continue;
            }

            body.AppendChild(new Paragraph(
                new Run(new Text(piece.Text) { Space = SpaceProcessingModeValues.Preserve })));
        }

        main.Document.Save();
    }

    /// <summary>
    /// One picture, in the flow, at the size it had on the page.
    /// </summary>
    /// <remarks>
    /// Kept at its printed size rather than its pixel size. A PDF often stores a picture far larger
    /// than it is drawn — a photograph placed small still carries all its pixels — so taking the
    /// pixels as the size would blow a thumbnail up to fill several pages.
    ///
    /// <para>
    /// Word measures in English metric units: nine hundred and fourteen thousand four hundred to the
    /// inch, so twelve thousand seven hundred to the point, which is what a PDF measures in.
    /// </para>
    /// </remarks>
    private static Paragraph PictureParagraph(MainDocumentPart main, Piece piece, uint number)
    {
        const long UnitsPerPoint = 12700;

        var part = main.AddImagePart(piece.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png);

        using (var bytes = new MemoryStream(piece.Image!))
        {
            part.FeedData(bytes);
        }

        var width = (long)Math.Max(1, piece.WidthPt * UnitsPerPoint);
        var height = (long)Math.Max(1, piece.HeightPt * UnitsPerPoint);
        var name = $"Image {number}";

        var graphic = new DocumentFormat.OpenXml.Drawing.Graphic(
            new DocumentFormat.OpenXml.Drawing.GraphicData(
                new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties
                        {
                            Id = number,
                            Name = name,
                        },
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                    new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                        new DocumentFormat.OpenXml.Drawing.Blip { Embed = main.GetIdOfPart(part) },
                        new DocumentFormat.OpenXml.Drawing.Stretch(
                            new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                    new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                        new DocumentFormat.OpenXml.Drawing.Transform2D(
                            new DocumentFormat.OpenXml.Drawing.Offset { X = 0, Y = 0 },
                            new DocumentFormat.OpenXml.Drawing.Extents { Cx = width, Cy = height }),
                        new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                            new DocumentFormat.OpenXml.Drawing.AdjustValueList())
                        {
                            Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle,
                        })))
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
            });

        var inline = new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = width, Cy = height },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent
            {
                LeftEdge = 0,
                TopEdge = 0,
                RightEdge = 0,
                BottomEdge = 0,
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = number, Name = name },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };

        return new Paragraph(new Run(new Drawing(inline)));
    }
}
