using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Writes the rows as a workbook.
/// </summary>
/// <remarks>
/// Text cells throughout, and deliberately: a column of dates written in one country's order becomes
/// a column of different dates when a spreadsheet in another country guesses at them, and a reference
/// number with leading zeros loses them the moment something decides it is a quantity. What was
/// extracted is what was printed, and that is what is stored.
///
/// <para>
/// Anyone who wants a column treated as numbers can say so in the spreadsheet, where they can see
/// what they are converting. This code cannot see it and would be guessing.
/// </para>
/// </remarks>
internal static class OfficeSheet
{
    /// <summary>Writes the file.</summary>
    internal static void Write(string output, List<List<string>> rows)
    {
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);

        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();

        var worksheet = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();

        var number = 1U;

        foreach (var row in rows)
        {
            var line = new Row { RowIndex = number };

            for (var column = 0; column < row.Count; column++)
            {
                if (row[column].Length == 0)
                {
                    continue;
                }

                // An inline string, not CellValues.String. That one means "the string a formula
                // produced" and puts the text where a computed value goes; Excel tolerates it,
                // everything else has to guess, and a cell read back as a formula result is not
                // what was extracted. There is no shared-string table because nothing here repeats
                // enough to pay for one.
                line.Append(new Cell
                {
                    CellReference = Reference(column, number),
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(Printable(row[column]))),
                });
            }

            data.Append(line);
            number++;
        }

        worksheet.Worksheet = new Worksheet(data);

        workbook.Workbook.Append(new Sheets(new Sheet
        {
            Id = workbook.GetIdOfPart(worksheet),
            SheetId = 1U,
            Name = "Extraction",
        }));

        workbook.Workbook.Save();
    }

    /// <summary>
    /// A cell's name, such as <c>A1</c> or <c>AB12</c>.
    /// </summary>
    /// <remarks>
    /// Base twenty-six with no zero, which is why this counts down rather than dividing cleanly: the
    /// column after Z is AA, not BA. A table wider than twenty-six columns gets this wrong if the
    /// subtraction is left out, and that is exactly the table nobody checks.
    /// </remarks>
    private static string Reference(int column, uint row)
    {
        var name = "";

        for (var remaining = column; ; remaining = remaining / 26 - 1)
        {
            name = (char)('A' + remaining % 26) + name;

            if (remaining < 26)
            {
                break;
            }
        }

        return name + row;
    }

    /// <summary>Drops the characters the format cannot carry.</summary>
    /// <remarks>
    /// A workbook is XML, and XML forbids almost every control character. PDFs carry them, and one
    /// of them anywhere would otherwise lose the whole file rather than one character.
    /// </remarks>
    private static string Printable(string text)
        => text.Any(character => char.IsControl(character) && character is not '\t')
            ? new string(text.Where(c => !char.IsControl(c) || c is '\t').ToArray())
            : text;
}
