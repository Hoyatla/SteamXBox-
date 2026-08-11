using System.Globalization;
using System.Security;
using System.Text;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Writes the paragraphs and pictures as an OpenDocument text document.
/// </summary>
/// <remarks>
/// The free-format twin of the Word writer, and it makes the same trade: a flow of paragraphs with
/// the pictures in it, and no positioned boxes at all. Leaving this to LibreOffice would have sent
/// it back through the PDF import that produces ten thousand floating objects — so a customer
/// choosing OpenDocument would have got the unusable document and a customer choosing Word the good
/// one, which is the wrong way round for a product sold to schools.
///
/// <para>
/// A picture is anchored <c>as-char</c>: it sits in the text the way a letter does, so it moves when
/// the paragraphs above it are edited rather than staying behind at a coordinate.
/// </para>
/// </remarks>
internal static class OpenDocumentText
{
    /// <summary>Writes the file.</summary>
    internal static void Write(string output, IReadOnlyList<PdfToDocument.Piece> pieces)
    {
        var pictures = new List<PackedPicture>();

        OpenDocumentPackage.Write(
            output,
            OpenDocumentPackage.Text,
            Content(pieces, pictures),
            Styles(),
            pictures);
    }

    private static string Content(IReadOnlyList<PdfToDocument.Piece> pieces, List<PackedPicture> pictures)
    {
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
                       <style:style style:name="frImage" style:family="graphic">
                         <style:graphic-properties style:vertical-pos="middle"
                           style:vertical-rel="text" draw:stroke="none" draw:fill="none"/>
                       </style:style>
                     </office:automatic-styles>
                     <office:body>
                       <office:text>

                   """);

        foreach (var piece in pieces)
        {
            if (piece.IsPicture)
            {
                var path = $"Pictures/image{pictures.Count + 1}.{(piece.Jpeg ? "jpg" : "png")}";

                pictures.Add(new PackedPicture(path, piece.Image!, piece.Jpeg ? "image/jpeg" : "image/png"));

                xml.Append("      <text:p><draw:frame draw:style-name=\"frImage\" ")
                    .Append("text:anchor-type=\"as-char\" ")
                    .Append("svg:width=\"").Append(Length(piece.WidthPt)).Append("\" ")
                    .Append("svg:height=\"").Append(Length(piece.HeightPt)).Append("\">")
                    .Append("<draw:image xlink:href=\"").Append(path)
                    .Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>")
                    .Append("</draw:frame></text:p>\n");

                continue;
            }

            xml.Append("      <text:p>").Append(Escape(piece.Text)).Append("</text:p>\n");
        }

        xml.Append("""
                       </office:text>
                     </office:body>
                   </office:document-content>
                   """);

        return xml.ToString();
    }

    /// <summary>
    /// An A4 page, and the empty element that makes the rest of this file be read.
    /// </summary>
    /// <remarks>
    /// <b><c>office:styles</c> is load-bearing though it says nothing.</b> The schema makes it
    /// optional; without it LibreOffice ignores the whole <c>office:automatic-styles</c> block that
    /// follows, page layout included. Measured on the presentation writer, where its absence cost
    /// thirty-one per cent of the words and took four wrong diagnoses to find.
    /// </remarks>
    private static string Styles() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-styles
          xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
          xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
          xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
          office:version="1.2">
          <office:styles/>
          <office:automatic-styles>
            <style:page-layout style:name="PM1">
              <style:page-layout-properties fo:page-width="595pt" fo:page-height="842pt"
                fo:margin-top="57pt" fo:margin-bottom="57pt"
                fo:margin-left="57pt" fo:margin-right="57pt"
                style:print-orientation="portrait"/>
            </style:page-layout>
          </office:automatic-styles>
          <office:master-styles>
            <style:master-page style:name="Standard" style:page-layout-name="PM1"/>
          </office:master-styles>
        </office:document-styles>
        """;

    /// <summary>A length, in points, in the one culture that spells them the same everywhere.</summary>
    private static string Length(double points)
        => points.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";
}
