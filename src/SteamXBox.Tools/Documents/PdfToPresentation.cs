using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using A = DocumentFormat.OpenXml.Drawing;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Turns a PDF into a presentation, one page to a slide, everything where it was.
/// </summary>
/// <remarks>
/// <b>This one keeps the layout, and that is not a contradiction.</b> The PDF-to-Word route throws
/// the layout away on purpose: a Word document made of positioned boxes is the thing that took
/// seconds per turn of the mouse wheel, because a word processor lays out a flow and boxes fight it.
///
/// <para>
/// A slide has no flow. It is a fixed rectangle holding boxes at coordinates — which is exactly what
/// a PDF page is. So here the faithful conversion is also the cheap one, and the compromise the
/// other route had to make does not arise. The same input gives a worse Word document than PowerPoint
/// document, and the reason is the target, not the reader.
/// </para>
///
/// <para>
/// Text is grouped into blocks before being placed. Setting one box per word would be faithful and
/// useless — nobody can edit a slide holding four hundred boxes — so the words are clustered back
/// into the paragraphs they were printed as, and each of those becomes one editable box.
/// </para>
/// </remarks>
public static class PdfToPresentation
{
    /// <summary>Which targets this route can write.</summary>
    /// <remarks>
    /// Both presentation formats, from one reading. Leaving OpenDocument to LibreOffice would have
    /// sent it back through the PDF import this route exists to avoid — so a customer choosing the
    /// free format would have got the ten-thousand-object document, and the paid one the good one.
    /// For a product sold to schools and non-profits that is the wrong way round.
    /// </remarks>
    public static IReadOnlySet<string> Formats { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pptx", "odp" };

    /// <summary>Whether a conversion should take this route rather than LibreOffice.</summary>
    public static bool Handles(string input, string format)
        => Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           && Formats.Contains(format);

    /// <summary>A point, in the units a presentation measures in.</summary>
    private const long UnitsPerPoint = 12700;

    /// <summary>A hundredth of a point, which is how a font size is written.</summary>
    private const int HundredthsPerPoint = 100;

    /// <summary>One thing on a slide: a block of text or a picture, at its place on the page.</summary>
    /// <remarks>
    /// Visible to the assembly because two writers consume it. The reading is the hard part and is
    /// done once; what differs between a presentation and an OpenDocument presentation is only how
    /// the same rectangles are spelled.
    /// </remarks>
    internal readonly record struct Placed(
        string Text,
        byte[]? Image,
        bool Jpeg,
        double Left,
        double Top,
        double Width,
        double Height,
        double PointSize)
    {
        public bool IsPicture => Image is not null;
    }

    /// <summary>Everything one page carries.</summary>
    internal readonly record struct Sheet(double Width, double Height, List<Placed> Items);

    /// <summary>Reads the PDF and writes the presentation beside it.</summary>
    public static LibreOffice.Result Convert(string input, string format, string outputDirectory)
    {
        try
        {
            var sheets = Read(input);

            if (sheets.Count == 0)
            {
                return new LibreOffice.Result("", "Ce PDF ne contient aucune page lisible.");
            }

            if (sheets.All(sheet => sheet.Items.Count == 0))
            {
                return new LibreOffice.Result(
                    "", "Ce PDF ne contient ni texte ni image exploitable.");
            }

            Directory.CreateDirectory(outputDirectory);

            var output = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(input) + "." + format.ToLowerInvariant());

            if (format.Equals("odp", StringComparison.OrdinalIgnoreCase))
            {
                OpenDocumentPresentation.Write(output, sheets);
            }
            else
            {
                Write(output, sheets);
            }

            return new LibreOffice.Result(output, "");
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
        }
    }

    /// <summary>
    /// Every page, with its blocks and pictures where the page put them.
    /// </summary>
    /// <remarks>
    /// Words are clustered with the nearest-neighbour extractor and then grouped into blocks by
    /// Docstrum, which measures the spacing between lines and decides what is one paragraph. That is
    /// what turns four hundred words into a dozen boxes somebody can actually edit.
    /// </remarks>
    private static List<Sheet> Read(string input)
    {
        var sheets = new List<Sheet>();

        using var pdf = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = true });

        foreach (var page in pdf.GetPages())
        {
            var items = new List<Placed>();

            // Pictures first so that text sits over them, which is the order a page was drawn in
            // and the order a reader expects when the two overlap.
            foreach (var image in page.GetImages())
            {
                var bounds = image.BoundingBox;

                if (bounds.Width < 20 || bounds.Height < 20)
                {
                    continue;
                }

                var raw = image.RawMemory.ToArray();
                var jpeg = raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF;

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

                items.Add(new Placed(
                    "", bytes, jpeg,
                    bounds.Left, page.Height - bounds.Top,
                    bounds.Width, bounds.Height, 0));
            }

            foreach (var block in Blocks(page))
            {
                var text = Printable(block.Text);

                if (text.Length == 0)
                {
                    continue;
                }

                var box = block.BoundingBox;
                var size = SizeOf(block);

                items.Add(new Placed(
                    text, null, false,
                    box.Left, page.Height - box.Top,
                    box.Width,
                    box.Height,
                    size));
            }

            sheets.Add(new Sheet(page.Width, page.Height, items));
        }

        return sheets;
    }

    /// <summary>The page's text, grouped into the paragraphs it was printed as.</summary>
    private static IReadOnlyList<TextBlock> Blocks(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();

        return words.Count == 0 ? [] : DocstrumBoundingBoxes.Instance.GetBlocks(words);
    }

    /// <summary>
    /// The size the text was set in.
    /// </summary>
    /// <remarks>
    /// Taken from the letters themselves rather than guessed from the height of the box, which would
    /// be the block's line spacing and is a different number. A block mixing sizes — a heading run
    /// into its first line — gets the most common one rather than the average, because an average of
    /// twenty-four and ten is a size neither of them was.
    /// </remarks>
    private static double SizeOf(TextBlock block)
    {
        var sizes = block.TextLines
            .SelectMany(line => line.Words)
            .SelectMany(word => word.Letters)
            .Select(letter => Math.Round(letter.PointSize))
            .Where(size => size is > 0 and < 400)
            .ToList();

        if (sizes.Count == 0)
        {
            return 11;
        }

        return sizes.GroupBy(size => size)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First().Key;
    }

    /// <summary>Drops the characters the format cannot carry.</summary>
    private static string Printable(string text)
    {
        if (!text.Any(character => char.IsControl(character) && character is not ('\n' or '\t')))
        {
            return text.Trim();
        }

        return new string(text
            .Where(character => !char.IsControl(character) || character is ('\n' or '\t'))
            .ToArray()).Trim();
    }

    /// <summary>Writes the slides.</summary>
    private static void Write(string output, List<Sheet> sheets)
    {
        using var presentation = PresentationDocument.Create(output, PresentationDocumentType.Presentation);

        var part = presentation.AddPresentationPart();
        part.Presentation = new Presentation();

        var master = part.AddNewPart<SlideMasterPart>("rIdMaster");
        var layout = master.AddNewPart<SlideLayoutPart>("rIdLayout");
        var theme = part.AddNewPart<ThemePart>("rIdTheme");

        theme.Theme = Theme();
        layout.SlideLayout = Layout();
        master.SlideMaster = Master(master.GetIdOfPart(layout));

        // The page size of the first sheet decides the slide size. A PDF whose pages differ is rare
        // and a presentation cannot hold two sizes at once; the first page is the one somebody
        // looking at the file has in mind.
        var slideSize = new SlideSize
        {
            Cx = (int)Math.Min(51_206_400, sheets[0].Width * UnitsPerPoint),
            Cy = (int)Math.Min(51_206_400, sheets[0].Height * UnitsPerPoint),
        };

        var ids = new SlideIdList();
        var number = 256U;

        foreach (var sheet in sheets)
        {
            var slidePart = part.AddNewPart<SlidePart>($"rIdSlide{number}");

            slidePart.Slide = Slide(slidePart, sheet);
            slidePart.AddPart(layout, "rIdLayoutOfSlide");

            ids.Append(new SlideId { Id = number++, RelationshipId = part.GetIdOfPart(slidePart) });
        }

        part.Presentation.Append(
            new SlideMasterIdList(new SlideMasterId
            {
                Id = 2_147_483_648U,
                RelationshipId = part.GetIdOfPart(master),
            }),
            ids,
            slideSize,
            new NotesSize { Cx = slideSize.Cy!.Value, Cy = slideSize.Cx!.Value });

        part.Presentation.Save();
    }

    /// <summary>One slide, holding one page's pictures and blocks of text.</summary>
    private static Slide Slide(SlidePart part, Sheet sheet)
    {
        var tree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));

        var id = 2U;

        foreach (var item in sheet.Items)
        {
            tree.Append(item.IsPicture ? PictureOf(part, item, id) : TextOf(item, id));
            id++;
        }

        return new Slide(new CommonSlideData(tree), new ColorMapOverride(new A.MasterColorMapping()));
    }

    /// <summary>A block of text, in a box the user can click into and edit.</summary>
    private static Shape TextOf(Placed item, uint id)
    {
        var paragraphs = new List<OpenXmlElement>
        {
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Top },
            new A.ListStyle(),
        };

        foreach (var line in item.Text.Split('\n'))
        {
            paragraphs.Add(new A.Paragraph(
                new A.Run(
                    new A.RunProperties { Language = "fr-CH", FontSize = Points(item.PointSize), Dirty = false },
                    new A.Text(line))));
        }

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Texte {id}" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(
                Frame(item),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            new TextBody(paragraphs));
    }

    /// <summary>A picture, at the size and place the page drew it.</summary>
    private static Picture PictureOf(SlidePart part, Placed item, uint id)
    {
        var image = part.AddImagePart(item.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png);

        using (var bytes = new MemoryStream(item.Image!))
        {
            image.FeedData(bytes);
        }

        return new Picture(
            new NonVisualPictureProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Image {id}" },
                new NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new ApplicationNonVisualDrawingProperties()),
            new BlipFill(
                new A.Blip { Embed = part.GetIdOfPart(image) },
                new A.Stretch(new A.FillRectangle())),
            new ShapeProperties(
                Frame(item),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    /// <summary>
    /// Where a thing sits on the slide.
    /// </summary>
    /// <remarks>
    /// The one conversion that matters: a PDF measures upward from the foot of the page and a slide
    /// measures downward from its top, so every vertical coordinate is subtracted from the page
    /// height. Getting this backwards turns a document upside down, which is the sort of mistake
    /// that looks like a font problem for an hour.
    /// </remarks>
    private static A.Transform2D Frame(Placed item) => new(
        new A.Offset
        {
            X = (long)Math.Max(0, item.Left * UnitsPerPoint),
            Y = (long)Math.Max(0, item.Top * UnitsPerPoint),
        },
        new A.Extents
        {
            Cx = (long)Math.Max(UnitsPerPoint, item.Width * UnitsPerPoint),
            Cy = (long)Math.Max(UnitsPerPoint, item.Height * UnitsPerPoint),
        });

    private static int Points(double size) => (int)Math.Max(100, size * HundredthsPerPoint);

    /// <summary>The slide master, which every slide inherits from.</summary>
    private static SlideMaster Master(string layoutId) => new(
        new CommonSlideData(new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()))),
        new ColorMap
        {
            Background1 = A.ColorSchemeIndexValues.Light1,
            Text1 = A.ColorSchemeIndexValues.Dark1,
            Background2 = A.ColorSchemeIndexValues.Light2,
            Text2 = A.ColorSchemeIndexValues.Dark2,
            Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
            Accent1 = A.ColorSchemeIndexValues.Accent1,
            Accent2 = A.ColorSchemeIndexValues.Accent2,
            Accent3 = A.ColorSchemeIndexValues.Accent3,
            Accent4 = A.ColorSchemeIndexValues.Accent4,
            Accent5 = A.ColorSchemeIndexValues.Accent5,
            Accent6 = A.ColorSchemeIndexValues.Accent6,
        },
        new SlideLayoutIdList(new SlideLayoutId { Id = 2_147_483_649U, RelationshipId = layoutId }));

    /// <summary>An empty layout: the pages bring their own content, so there is nothing to lay out.</summary>
    private static SlideLayout Layout() => new(
        new CommonSlideData(new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()))),
        new ColorMapOverride(new A.MasterColorMapping()))
    {
        Type = SlideLayoutValues.Blank,
    };

    /// <summary>
    /// The theme the format insists on.
    /// </summary>
    /// <remarks>
    /// PowerPoint will not open a presentation whose master has no theme, even one that decides
    /// nothing. This is the smallest one that satisfies it: the standard Office colours, one font,
    /// and a fill and line style so that a shape drawn without its own has something to inherit.
    /// </remarks>
    private static A.Theme Theme() => new(
        new A.ThemeElements(
            new A.ColorScheme(
                new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText }),
                new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window }),
                new A.Dark2Color(new A.RgbColorModelHex { Val = "44546A" }),
                new A.Light2Color(new A.RgbColorModelHex { Val = "E7E6E6" }),
                new A.Accent1Color(new A.RgbColorModelHex { Val = "4472C4" }),
                new A.Accent2Color(new A.RgbColorModelHex { Val = "ED7D31" }),
                new A.Accent3Color(new A.RgbColorModelHex { Val = "A5A5A5" }),
                new A.Accent4Color(new A.RgbColorModelHex { Val = "FFC000" }),
                new A.Accent5Color(new A.RgbColorModelHex { Val = "5B9BD5" }),
                new A.Accent6Color(new A.RgbColorModelHex { Val = "70AD47" }),
                new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }),
                new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" }))
            {
                Name = "Office",
            },
            new A.FontScheme(
                new A.MajorFont(new A.LatinFont { Typeface = "Calibri Light" }, new A.EastAsianFont { Typeface = "" },
                    new A.ComplexScriptFont { Typeface = "" }),
                new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" },
                    new A.ComplexScriptFont { Typeface = "" }))
            {
                Name = "Office",
            },
            new A.FormatScheme(
                new A.FillStyleList(
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })),
                new A.LineStyleList(
                    new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 6350 },
                    new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 12700 },
                    new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 19050 }),
                new A.EffectStyleList(
                    new A.EffectStyle(new A.EffectList()),
                    new A.EffectStyle(new A.EffectList()),
                    new A.EffectStyle(new A.EffectList())),
                new A.BackgroundFillStyleList(
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                    new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })))
            {
                Name = "Office",
            }))
    {
        Name = "Office",
    };
}
