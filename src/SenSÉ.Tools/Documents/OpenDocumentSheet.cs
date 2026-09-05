using System.Security;
using System.Text;

namespace SenSÉ.Tools.Documents;

/// <summary>
/// Writes the rows as an OpenDocument spreadsheet.
/// </summary>
/// <remarks>
/// The free-format twin of the workbook writer, and it makes the same choice: every cell is text.
/// A column of dates written in one country's order becomes a column of different dates when a
/// spreadsheet in another country guesses at them, and a reference number with leading zeros loses
/// them the moment something decides it is a quantity.
/// </remarks>
internal static class OpenDocumentSheet
{
    private const string Spreadsheet = "application/vnd.oasis.opendocument.spreadsheet";

    /// <summary>Writes the file.</summary>
    internal static void Write(string output, List<List<string>> rows)
        => OpenDocumentPackage.Write(output, Spreadsheet, Content(rows), Styles(), []);

    private static string Content(List<List<string>> rows)
    {
        var xml = new StringBuilder();

        xml.Append("""
                   <?xml version="1.0" encoding="UTF-8"?>
                   <office:document-content
                     xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                     xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                     xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                     office:version="1.2">
                     <office:body>
                       <office:spreadsheet>
                         <table:table table:name="Extraction">

                   """);

        foreach (var row in rows)
        {
            xml.Append("        <table:table-row>\n");

            foreach (var cell in row)
            {
                if (cell.Length == 0)
                {
                    xml.Append("          <table:table-cell/>\n");
                    continue;
                }

                xml.Append("          <table:table-cell office:value-type=\"string\">")
                    .Append("<text:p>").Append(Escape(cell)).Append("</text:p>")
                    .Append("</table:table-cell>\n");
            }

            xml.Append("        </table:table-row>\n");
        }

        xml.Append("""
                         </table:table>
                       </office:spreadsheet>
                     </office:body>
                   </office:document-content>
                   """);

        return xml.ToString();
    }

    /// <summary>
    /// Nothing to say, said properly.
    /// </summary>
    /// <remarks>
    /// The empty <c>office:styles</c> element is here for the same reason it is in the other
    /// OpenDocument writers: without it LibreOffice ignores what follows. There is nothing after it
    /// in this file today, and the day something is added it will already be read.
    /// </remarks>
    private static string Styles() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-styles
          xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
          xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
          office:version="1.2">
          <office:styles/>
          <office:automatic-styles/>
          <office:master-styles/>
        </office:document-styles>
        """;

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";
}
