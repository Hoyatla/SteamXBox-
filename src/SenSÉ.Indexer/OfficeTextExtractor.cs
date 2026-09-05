using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SenSÉ.Tools.Indexing;

namespace SenSÉ.Indexer;

/// <summary>
/// Reads the text of a Word, Excel or PowerPoint file.
/// </summary>
/// <remarks>
/// The Open XML SDK, MIT, published by Microsoft. It reads the file format directly — a modern
/// Office file is a ZIP of XML — so <b>nothing from Microsoft Office needs to be installed</b>. That
/// is the whole reason to use it rather than Office Interop, which would require a licensed Office
/// on the machine doing the indexing. A server has none, and asking an institution to license Office
/// on a file server to make search work would end the conversation.
///
/// <para>
/// Only the current formats. The old <c>.doc</c>, <c>.xls</c> and <c>.ppt</c> are a different,
/// binary format that this SDK does not read at all; they are left to the reporting rather than
/// quietly skipped, because a share of a school or an association is full of them.
/// </para>
/// </remarks>
public sealed class OfficeTextExtractor : ITextExtractor
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx", ".pptx" };

    public bool Handles(string extension) => Extensions.Contains(extension);

    public string Extract(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length > IndexableFiles.MaxBytes)
            {
                return "";
            }

            var text = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".docx" => FromWord(path),
                ".xlsx" => FromSpreadsheet(path),
                ".pptx" => FromPresentation(path),
                _ => "",
            };

            return IndexableFiles.Trim(text);
        }
        catch (Exception exception)
        {
            // Password-protected, corrupt, or a file whose extension lies about what it is.
            Console.Error.WriteLine($"Document illisible : {path} ({exception.GetType().Name})");
            return "";
        }
    }

    private static string FromWord(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);

        return document.MainDocumentPart?.Document?.Body?.InnerText ?? "";
    }

    /// <remarks>
    /// The shared string table has to be resolved by hand, and forgetting it is the classic mistake
    /// with this SDK: a spreadsheet stores repeated text once and puts an <i>index</i> in the cell,
    /// so reading cells raw gives a column of numbers where the words were.
    /// </remarks>
    private static string FromSpreadsheet(string path)
    {
        using var document = SpreadsheetDocument.Open(path, isEditable: false);

        var book = document.WorkbookPart;

        if (book is null)
        {
            return "";
        }

        var shared = book.SharedStringTablePart?.SharedStringTable?
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToArray() ?? [];

        var text = new StringBuilder();

        foreach (var sheet in book.WorksheetParts)
        {
            if (sheet.Worksheet is not { } worksheet)
            {
                continue;
            }

            foreach (var cell in worksheet.Descendants<Cell>())
            {
                if (text.Length >= IndexableFiles.MaxCharacters)
                {
                    return text.ToString();
                }

                var value = cell.CellValue?.InnerText ?? "";

                if (value.Length == 0)
                {
                    continue;
                }

                if (cell.DataType?.Value == CellValues.SharedString
                    && int.TryParse(value, out var at)
                    && at >= 0 && at < shared.Length)
                {
                    value = shared[at];
                }

                text.Append(value).Append(' ');
            }
        }

        return text.ToString();
    }

    private static string FromPresentation(string path)
    {
        using var document = PresentationDocument.Open(path, isEditable: false);

        var slides = document.PresentationPart?.SlideParts;

        if (slides is null)
        {
            return "";
        }

        var text = new StringBuilder();

        foreach (var slide in slides)
        {
            if (text.Length >= IndexableFiles.MaxCharacters)
            {
                break;
            }

            if (slide.Slide is { } content)
            {
                text.Append(content.InnerText).Append('\n');
            }
        }

        return text.ToString();
    }
}
