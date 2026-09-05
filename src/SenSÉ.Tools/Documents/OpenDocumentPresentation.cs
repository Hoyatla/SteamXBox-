using System.Globalization;
using System.Security;
using System.Text;

namespace SenSÉ.Tools.Documents;

/// <summary>
/// Writes the slides as an OpenDocument presentation.
/// </summary>
/// <remarks>
/// Written by hand rather than handed to LibreOffice, which sounds like the harder path and is not.
/// LibreOffice would have had to be given the PDF, and its PDF import goes through Draw — the route
/// that produced ten thousand floating objects for four hundred paragraphs. Asking it to convert a
/// presentation this code already built correctly would work, but only on a machine that has it.
///
/// <para>
/// An OpenDocument file is a ZIP holding a few XML documents, and a presentation is the simplest of
/// them: pages of frames at coordinates. There is no library here because none is needed, and the
/// result depends on nothing being installed — which matters for a product sold to schools and
/// non-profits, where the free format is the one that gets chosen.
/// </para>
///
/// <para>
/// <b>Every number is written in the invariant culture.</b> This is developed on a Swiss French
/// machine, where a decimal separator is a comma, and <c>svg:x="38,5pt"</c> is not a length. It is
/// the sort of defect that never appears on the machine that wrote it.
/// </para>
/// </remarks>
internal static class OpenDocumentPresentation
{

    /// <summary>Writes the file.</summary>
    internal static void Write(string output, List<PdfToPresentation.Sheet> sheets)
    {
        var pictures = new List<PackedPicture>();

        // The content is built first because building it is what discovers the pictures.
        var content = Content(sheets, pictures);

        OpenDocumentPackage.Write(
            output,
            OpenDocumentPackage.Presentation,
            content,
            Styles(sheets[0]),
            pictures);
    }

    /// <summary>
    /// The pages, their frames, and the styles those frames point at.
    /// </summary>
    /// <remarks>
    /// OpenDocument keeps formatting out of the content: a frame names a style and the style says
    /// what the text looks like. So one paragraph style is emitted per distinct size found in the
    /// document rather than one per block — a hundred-page report uses four or five sizes, and a
    /// style apiece would be a hundred times as many.
    /// </remarks>
    private static string Content(
        List<PdfToPresentation.Sheet> sheets,
        List<PackedPicture> pictures)
    {
        var sizes = sheets
            .SelectMany(sheet => sheet.Items)
            .Where(item => !item.IsPicture)
            .Select(item => item.PointSize)
            .Distinct()
            .OrderBy(size => size)
            .ToList();

        var styleOf = sizes
            .Select((size, index) => (size, name: $"P{index + 1}"))
            .ToDictionary(pair => pair.size, pair => pair.name);

        var xml = new StringBuilder();

        xml.Append("""
                   <?xml version="1.0" encoding="UTF-8"?>
                   <office:document-content
                     xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                     xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
                     xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                     xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
                     xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
                     xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
                     xmlns:xlink="http://www.w3.org/1999/xlink"
                     office:version="1.2">
                     <office:automatic-styles>

                   """);

        // One graphic style for text frames and one for pictures. A text frame draws no outline and
        // no fill, because the page it came from did not: a PDF's text sits on the page, not in a
        // box, and boxing it would add furniture the original never had.
        //
        // The frames are the blocks' own boxes, and they neither grow nor reflow. That is measured,
        // not assumed: growing frames and suppressed wrapping were both tried while a third of the
        // words were going missing, both were kept for a while, and once the real cause was found
        // — see Styles — each was measured to be very slightly worse than doing nothing. They are
        // gone rather than left in as insurance.
        xml.Append("""
                       <style:style style:name="grText" style:family="graphic">
                         <style:graphic-properties draw:fill="none" draw:stroke="none"
                           draw:textarea-vertical-align="top"
                           draw:textarea-horizontal-align="left" fo:padding="0pt"
                           fo:wrap-option="wrap" draw:auto-grow-height="false"
                           draw:auto-grow-width="false"/>
                       </style:style>
                       <style:style style:name="grImage" style:family="graphic">
                         <style:graphic-properties draw:fill="none" draw:stroke="none"/>
                       </style:style>

                   """);

        foreach (var (size, name) in styleOf)
        {
            xml.Append("    <style:style style:name=\"").Append(name)
                .Append("\" style:family=\"paragraph\">\n")
                .Append("      <style:text-properties fo:font-size=\"").Append(Length(size))
                .Append("\"/>\n    </style:style>\n");
        }

        xml.Append("""
                     </office:automatic-styles>
                     <office:body>
                       <office:presentation>

                   """);

        var page = 0;

        foreach (var sheet in sheets)
        {
            page++;

            xml.Append("      <draw:page draw:name=\"page").Append(page)
                .Append("\" draw:master-page-name=\"Default\">\n");

            foreach (var item in sheet.Items)
            {
                if (item.IsPicture)
                {
                    var path = $"Pictures/image{pictures.Count + 1}.{(item.Jpeg ? "jpg" : "png")}";

                    pictures.Add(new PackedPicture(path, item.Image!, item.Jpeg ? "image/jpeg" : "image/png"));

                    xml.Append("        <draw:frame draw:style-name=\"grImage\" ")
                        .Append(Frame(item)).Append(">\n")
                        .Append("          <draw:image xlink:href=\"").Append(path)
                        .Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>\n")
                        .Append("        </draw:frame>\n");

                    continue;
                }

                xml.Append("        <draw:frame draw:style-name=\"grText\" ")
                    .Append(Frame(item)).Append(">\n          <draw:text-box>\n");

                foreach (var line in item.Text.Split('\n'))
                {
                    xml.Append("            <text:p text:style-name=\"").Append(styleOf[item.PointSize])
                        .Append("\">").Append(Escape(line)).Append("</text:p>\n");
                }

                xml.Append("          </draw:text-box>\n        </draw:frame>\n");
            }

            xml.Append("      </draw:page>\n");
        }

        xml.Append("""
                       </office:presentation>
                     </office:body>
                   </office:document-content>
                   """);

        return xml.ToString();
    }

    /// <summary>Where a frame sits, in the units the page was measured in.</summary>
    /// <remarks>
    /// No flip here, unlike the presentation writer's own. Both formats measure downward from the
    /// top of the page, and the reading step already turned the PDF's upward coordinates round once.
    /// Doing it twice would put the document back upside down.
    /// </remarks>
    private static string Frame(PdfToPresentation.Placed item)
        => $"svg:x=\"{Length(item.Left)}\" svg:y=\"{Length(item.Top)}\" "
           + $"svg:width=\"{Length(Math.Max(1, item.Width))}\" "
           + $"svg:height=\"{Length(Math.Max(1, item.Height))}\"";

    /// <summary>A length, in points, spelled the way a length has to be spelled everywhere.</summary>
    private static string Length(double points)
        => points.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// The slide size and the master page every page inherits from.
    /// </summary>
    /// <remarks>
    /// The first page decides, as it does for the other format: a presentation cannot hold two sizes
    /// at once, and the first page is the one somebody opening the file has in mind.
    ///
    /// <para>
    /// <b>The empty <c>office:styles</c> element is load-bearing.</b> The schema makes it optional
    /// and it says nothing, but without it LibreOffice ignores the whole
    /// <c>office:automatic-styles</c> block that follows — page layout included — and falls back to
    /// its own sixteen-by-nine slide. An A4 page then came back seven hundred and ninety-four by
    /// four hundred and forty-six points, and everything below that line was off the slide:
    /// <b>thirty-one per cent of the words, gone without a message</b>.
    /// </para>
    ///
    /// <para>
    /// Established by removing it and putting it back, after three other suspects — the unit, the
    /// master page's name, the frame sizes — were each changed and each measured to make no
    /// difference at all. Every one of them had a plausible story attached.
    /// </para>
    /// </remarks>
    private static string Styles(PdfToPresentation.Sheet first) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <office:document-styles
           xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
           xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
           xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
           xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
           office:version="1.2">
           <office:styles/>
           <office:automatic-styles>
             <style:page-layout style:name="PM1">
               <style:page-layout-properties fo:page-width="{Length(first.Width)}"
                 fo:page-height="{Length(first.Height)}"
                 fo:margin-top="0pt" fo:margin-bottom="0pt"
                 fo:margin-left="0pt" fo:margin-right="0pt"
                 style:print-orientation="{(first.Width > first.Height ? "landscape" : "portrait")}"/>
             </style:page-layout>
             <style:style style:name="dp1" style:family="drawing-page"/>
           </office:automatic-styles>
           <office:master-styles>
             <style:master-page style:name="Default" style:page-layout-name="PM1" draw:style-name="dp1"/>
           </office:master-styles>
         </office:document-styles>
         """;

    /// <summary>Text, made safe to put inside XML.</summary>
    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";
}
