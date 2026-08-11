using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Pulls the rows and columns out of a PDF.
/// </summary>
/// <remarks>
/// <b>This is an extraction, not a conversion, and the difference is not pedantry.</b> A PDF does
/// not contain a table. It contains words at coordinates, and the table a reader sees is something
/// their eye assembles from alignment and spacing. Every other route in this folder rearranges
/// information the file actually holds; this one infers information the file never held.
///
/// <para>
/// So it will be right on a printed table with aligned columns, and wrong on a page laid out to look
/// like one. It is offered because the alternative on this machine was LibreOffice's PDF import,
/// which rebuilds the page as a drawing and gives a spreadsheet holding one enormous picture — worse
/// in every way, including that it looks like it worked.
/// </para>
///
/// <para>
/// The rule is stated so it can be argued with: words are cut into rows by their vertical position,
/// and a column boundary is a stripe of the page that nothing anywhere on it is printed on, wide
/// enough not to be word spacing.
/// </para>
/// </remarks>
public static class PdfToTable
{
    /// <summary>Which targets this route can write.</summary>
    public static IReadOnlySet<string> Formats { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "csv", "xlsx", "ods" };

    /// <summary>Whether a conversion should take this route rather than LibreOffice.</summary>
    public static bool Handles(string input, string format)
        => Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           && Formats.Contains(format);

    /// <summary>How much of a line's height two words may differ by and still share a row.</summary>
    /// <remarks>
    /// Words on one printed line rarely share a baseline exactly — a superscript, a different font,
    /// a hair of rounding. Half a line's height groups them without merging two real lines, which
    /// are a whole line's height apart by definition.
    /// </remarks>
    private const double RowTolerance = 0.5;


    /// <summary>Reads the PDF and writes the sheet beside it.</summary>
    public static LibreOffice.Result Convert(string input, string format, string outputDirectory)
    {
        try
        {
            var rows = Read(input);

            if (rows.Count == 0)
            {
                return new LibreOffice.Result(
                    "", "Ce PDF ne contient pas de texte : rien à mettre en colonnes.");
            }

            Directory.CreateDirectory(outputDirectory);

            var output = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(input) + "." + format.ToLowerInvariant());

            switch (format.ToLowerInvariant())
            {
                case "csv":
                    File.WriteAllLines(output, rows.Select(Csv), System.Text.Encoding.UTF8);
                    break;

                case "ods":
                    OpenDocumentSheet.Write(output, rows);
                    break;

                default:
                    OfficeSheet.Write(output, rows);
                    break;
            }

            return new LibreOffice.Result(output, "");
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
        }
    }

    /// <summary>Every page's rows, one list of cells apiece.</summary>
    internal static List<List<string>> Read(string input)
    {
        var rows = new List<List<string>>();

        using var pdf = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = true });

        foreach (var page in pdf.GetPages())
        {
            rows.AddRange(Rows(page));
        }

        // Squared off, because the columns are found one page at a time and pages disagree. A
        // spreadsheet whose rows are different lengths cannot be sorted, filtered or summed — the
        // three things somebody wanting a spreadsheet is about to do.
        var widest = rows.Count == 0 ? 0 : rows.Max(row => row.Count);

        foreach (var row in rows)
        {
            while (row.Count < widest)
            {
                row.Add("");
            }
        }

        return rows;
    }

    /// <summary>One page, cut into rows and then into columns.</summary>
    private static List<List<string>> Rows(Page page)
    {
        var words = page.GetWords()
            .Where(word => word.Text.Trim().Length > 0)
            .ToList();

        if (words.Count == 0)
        {
            return [];
        }

        var height = words.Average(word => word.BoundingBox.Height);
        var lines = Lines(words, height * RowTolerance);
        var boundaries = Columns(lines, height, page.Width);

        return lines.Select(line => Cells(line, boundaries)).ToList();
    }

    /// <summary>The words grouped into printed lines, top of the page first.</summary>
    private static List<List<Word>> Lines(List<Word> words, double tolerance)
    {
        var lines = new List<List<Word>>();

        // A PDF measures upward, so the top of the page is the largest coordinate.
        foreach (var word in words.OrderByDescending(word => word.BoundingBox.Bottom))
        {
            var line = lines.FirstOrDefault(
                existing => Math.Abs(existing[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= tolerance);

            if (line is null)
            {
                lines.Add([word]);
                continue;
            }

            line.Add(word);
        }

        foreach (var line in lines)
        {
            line.Sort((left, right) => left.BoundingBox.Left.CompareTo(right.BoundingBox.Left));
        }

        return lines;
    }

    /// <summary>
    /// Where the columns start.
    /// </summary>
    /// <remarks>
    /// <b>A column boundary is a stripe of the page nothing is printed on.</b> Not a gap that most
    /// rows happen to share — that was the first rule here and it was wrong for the reason tables
    /// exist: a row of a real table routinely leaves a cell empty, so the gap that defines the column
    /// is missing from exactly the rows that needed it most. Measured on a two-column list of sixty
    /// pages, it found no columns at all on most of them.
    ///
    /// <para>
    /// Asking the opposite question is robust to that. Every word on the page inks a band of
    /// horizontal positions; a position no word anywhere on the page inks, across a stripe wide
    /// enough not to be word spacing, is where a column ends. An empty cell contributes nothing
    /// either way instead of voting against.
    /// </para>
    /// </remarks>
    private static List<double> Columns(List<List<Word>> lines, double height, double pageWidth)
    {
        var width = (int)Math.Ceiling(pageWidth) + 1;
        var inked = new bool[width];

        foreach (var word in lines.SelectMany(line => line))
        {
            var from = Math.Max(0, (int)Math.Floor(word.BoundingBox.Left));
            var to = Math.Min(width - 1, (int)Math.Ceiling(word.BoundingBox.Right));

            for (var at = from; at <= to; at++)
            {
                inked[at] = true;
            }
        }

        // Wider than the space between words, or every gap in justified prose becomes a column. Two
        // thirds of the line height is about three spaces in most fonts.
        var minimum = Math.Max(2, (int)(height * 0.66));
        var boundaries = new List<double>();

        var start = -1;
        var seenInk = false;

        for (var at = 0; at < width; at++)
        {
            if (!inked[at])
            {
                if (start < 0)
                {
                    start = at;
                }

                continue;
            }

            // A blank stripe only separates columns if something was printed before it. The margin
            // down the left of every page is not a column boundary.
            if (seenInk && start >= 0 && at - start >= minimum)
            {
                boundaries.Add(at);
            }

            seenInk = true;
            start = -1;
        }

        return boundaries;
    }

    /// <summary>One row's words, distributed into its cells.</summary>
    private static List<string> Cells(List<Word> line, List<double> boundaries)
    {
        var cells = new List<string>(new string[boundaries.Count + 1]);

        for (var index = 0; index < cells.Count; index++)
        {
            cells[index] = "";
        }

        foreach (var word in line)
        {
            // A word belongs to the last column that starts at or before it. Half a point of slack,
            // because the boundary was rounded and the word was not.
            var column = 0;

            while (column < boundaries.Count && word.BoundingBox.Left >= boundaries[column] - 0.5)
            {
                column++;
            }

            cells[column] = cells[column].Length == 0 ? word.Text : cells[column] + " " + word.Text;
        }

        return cells;
    }

    /// <summary>
    /// One row, as a line of comma-separated values.
    /// </summary>
    /// <remarks>
    /// Quoted whenever the value holds a comma, a quotation mark or a line break, with quotation
    /// marks doubled inside. A cell containing a comma written plain silently becomes two cells, and
    /// nothing downstream can tell that it happened.
    /// </remarks>
    public static string Csv(IReadOnlyList<string> cells)
        => string.Join(',', cells.Select(cell =>
            cell.Contains(',') || cell.Contains('"') || cell.Contains('\n')
                ? '"' + cell.Replace("\"", "\"\"") + '"'
                : cell));
}
