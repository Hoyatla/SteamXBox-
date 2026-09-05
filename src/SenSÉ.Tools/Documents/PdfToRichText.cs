using System.Globalization;
using System.Text;

namespace SenSÉ.Tools.Documents;

/// <summary>
/// Writes the paragraphs and pictures as rich text.
/// </summary>
/// <remarks>
/// RTF is the format every word processor made since the eighties will open, which is the reason to
/// offer it at all: it is what somebody reaches for when they do not know what the other person has.
///
/// <para>
/// It is also plain text with braces, so there is no library and nothing to install — the same
/// property that made the OpenDocument writers worth doing by hand.
/// </para>
///
/// <para>
/// <b>Everything outside ASCII is escaped as a signed sixteen-bit code.</b> RTF predates Unicode and
/// carries it through <c>\uN?</c>, where the character after the escape is what a reader too old to
/// understand it shows instead. A French document written without this comes out as mojibake from
/// end to end, which is the whole of the format's reputation for mangling accents.
/// </para>
/// </remarks>
internal static class PdfToRichText
{
    /// <summary>A twip: a twentieth of a point, which is what RTF measures in.</summary>
    private const int TwipsPerPoint = 20;

    /// <summary>Writes the file.</summary>
    internal static void Write(string output, IReadOnlyList<PdfToDocument.Piece> pieces)
    {
        var rtf = new StringBuilder();

        rtf.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0")
            .Append("{\\fonttbl{\\f0\\froman\\fcharset0 Times New Roman;}}")
            .Append("\\viewkind4\\uc1\\fs22\n");

        foreach (var piece in pieces)
        {
            if (piece.IsPicture)
            {
                Picture(rtf, piece);
                continue;
            }

            rtf.Append("\\pard\\sa120 ").Append(Escape(piece.Text)).Append("\\par\n");
        }

        rtf.Append('}');

        // Written as Latin-1 with everything else already escaped, which is what \ansicpg1252 in the
        // header promises. Writing UTF-8 bytes under that header would contradict it.
        File.WriteAllText(output, rtf.ToString(), Encoding.Latin1);
    }

    /// <summary>
    /// One picture, as hexadecimal.
    /// </summary>
    /// <remarks>
    /// RTF carries an image as the file's own bytes written out in hex, so a picture costs twice its
    /// size. Both formats this route produces are declared by name — <c>\jpegblip</c> and
    /// <c>\pngblip</c> — and nothing is re-encoded.
    ///
    /// <para>
    /// The <c>picwgoal</c> and <c>pichgoal</c> pair is the size to draw at, in twips, as opposed to
    /// the size the image happens to be. Without them a photograph placed small on the page comes
    /// back filling several pages.
    /// </para>
    /// </remarks>
    private static void Picture(StringBuilder rtf, PdfToDocument.Piece piece)
    {
        var width = (int)(piece.WidthPt * TwipsPerPoint);
        var height = (int)(piece.HeightPt * TwipsPerPoint);

        rtf.Append("\\pard\\qc{\\pict")
            .Append(piece.Jpeg ? "\\jpegblip" : "\\pngblip")
            .Append("\\picwgoal").Append(width.ToString(CultureInfo.InvariantCulture))
            .Append("\\pichgoal").Append(height.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        var hex = Convert.ToHexString(piece.Image!).ToLowerInvariant();

        // Wrapped, because a single line of several million characters is legal RTF that some
        // readers refuse to load.
        for (var at = 0; at < hex.Length; at += 128)
        {
            rtf.Append(hex, at, Math.Min(128, hex.Length - at)).Append('\n');
        }

        rtf.Append("}\\par\n");
    }

    /// <summary>Text, made safe to put inside RTF.</summary>
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                case '{':
                case '}':
                    escaped.Append('\\').Append(character);
                    break;

                case '\n':
                    escaped.Append("\\line ");
                    break;

                case '\r':
                case '\t':
                    escaped.Append(' ');
                    break;

                default:
                    if (character < 128)
                    {
                        escaped.Append(character);
                        break;
                    }

                    // Signed, because RTF's code is a sixteen-bit integer and readers of the era
                    // expected it to wrap. The question mark is what an old reader shows instead.
                    escaped.Append("\\u")
                        .Append(((short)character).ToString(CultureInfo.InvariantCulture))
                        .Append('?');
                    break;
            }
        }

        return escaped.ToString();
    }
}
