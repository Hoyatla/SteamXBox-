using System.Globalization;
using System.Net;
using System.Text;

namespace SenSÉ.Tools.Documents;

/// <summary>
/// Writes the paragraphs and pictures as a web page.
/// </summary>
/// <remarks>
/// One file, with the pictures inside it. A page whose images live in a folder beside it stops
/// working the moment somebody sends it to a colleague or moves it, and a conversion that survives
/// only where it was made is not much of a conversion.
///
/// <para>
/// The cost is stated: base64 is a third larger than the bytes it carries, so a document heavy with
/// photographs makes a heavy page. For a text document with a few figures — which is what takes this
/// route — the file stays small and self-contained.
/// </para>
/// </remarks>
internal static class PdfToWeb
{
    /// <summary>Writes the file.</summary>
    internal static void Write(string output, string title, IReadOnlyList<PdfToDocument.Piece> pieces)
    {
        var html = new StringBuilder();

        html.Append("<!doctype html>\n<html lang=\"fr\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(Escape(title)).Append("</title>\n")
            .Append("<style>\n")
            .Append("body { max-width: 46em; margin: 2em auto; padding: 0 1em;\n")
            .Append("  font-family: Georgia, 'Times New Roman', serif; line-height: 1.5; }\n")
            .Append("p { margin: 0 0 0.8em; }\n")
            .Append("img { max-width: 100%; height: auto; display: block; margin: 1.2em auto; }\n")
            .Append("@media (prefers-color-scheme: dark) {\n")
            .Append("  body { background: #16181c; color: #e8e6e3; } }\n")
            .Append("</style>\n</head>\n<body>\n");

        foreach (var piece in pieces)
        {
            if (piece.IsPicture)
            {
                // The width the page drew it at, so a picture placed small stays small. Without it a
                // thumbnail stored at full resolution fills the screen.
                html.Append("<img alt=\"\" style=\"width:")
                    .Append(piece.WidthPt.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append("pt\" src=\"data:").Append(piece.Jpeg ? "image/jpeg" : "image/png")
                    .Append(";base64,").Append(Convert.ToBase64String(piece.Image!)).Append("\">\n");

                continue;
            }

            html.Append("<p>").Append(Escape(piece.Text)).Append("</p>\n");
        }

        html.Append("</body>\n</html>\n");

        File.WriteAllText(output, html.ToString(), new UTF8Encoding(false));
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
