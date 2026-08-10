using System.Diagnostics;
using SteamXBox.Tools.Indexing;

namespace SteamXBox.Indexer;

/// <summary>
/// Reads the formats nothing else here can, by asking LibreOffice to convert them.
/// </summary>
/// <remarks>
/// The old binary Office formats — <c>.doc</c>, <c>.xls</c>, <c>.ppt</c> — and the OpenDocument ones
/// that schools and associations use heavily. No .NET library reads the binary ones under a licence
/// this product can accept: the one candidate, NPOI, charges a maintenance fee for its binary
/// release above a revenue threshold, and covers only <c>.xls</c> anyway.
///
/// <para>
/// <b>Detected, never bundled.</b> LibreOffice is three hundred and fifty megabytes and a security
/// surface that processes files from outside; shipping it would make this project its patch
/// maintainer towards schools and companies handling sensitive data. It is called as a separate
/// process — the same boundary as the search instances — so its MPL never reaches this product
/// either.
/// </para>
///
/// <para>
/// Absent, the formats are simply not handled and the run says what to install. That is a thinner
/// index, not a failure.
/// </para>
/// </remarks>
public sealed class LibreOfficeTextExtractor : ITextExtractor, IDisposable
{
    /// <summary>Long enough for a large document, short enough not to stall a whole share.</summary>
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromSeconds(60);

    /// <remarks>
    /// Templates as well as documents, and they were nearly missed. A spreadsheet saved from a model
    /// keeps the <c>.xlt</c> extension, and an institution's share is full of them — invoices,
    /// payment schedules, forms — all of them holding the text somebody would search for. They
    /// convert exactly like the document they are a model of.
    /// </remarks>
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Binary Office, and its templates.
            ".doc", ".xls", ".ppt",
            ".dot", ".xlt", ".pot",

            // OpenDocument, and its templates.
            ".odt", ".ods", ".odp",
            ".ott", ".ots", ".otp",
        };

    private readonly string? _soffice;
    private readonly string _workspace;

    /// <summary>Reads the PDFs that presentations are converted into.</summary>
    private readonly PdfTextExtractor _pdf = new();

    public LibreOfficeTextExtractor()
    {
        _soffice = Find();

        _workspace = Path.Combine(Path.GetTempPath(), "SteamXBox.Indexer", Guid.NewGuid().ToString("N"));

        Console.WriteLine(_soffice is null
            ? "LibreOffice absent : .doc, .xls, .ppt et les formats OpenDocument seront ignorés. "
              + "Installez LibreOffice sur cette machine pour les indexer."
            : $"LibreOffice trouvé : {_soffice}");
    }

    /// <summary>Whether LibreOffice was found. Nothing is handled without it.</summary>
    public bool IsAvailable => _soffice is not null;

    public bool Handles(string extension) => IsAvailable && Extensions.Contains(extension);

    public string Extract(string path)
    {
        if (_soffice is null)
        {
            return "";
        }

        var output = Path.Combine(_workspace, Guid.NewGuid().ToString("N"));

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists || info.Length > IndexableFiles.MaxBytes)
            {
                return "";
            }

            Directory.CreateDirectory(output);

            if (!Convert(path, output))
            {
                return "";
            }

            // LibreOffice names the result after the input, with the new extension. Taking whatever
            // landed in the folder avoids having to reproduce its naming, which differs by filter.
            var produced = Directory.GetFiles(output).FirstOrDefault();

            if (produced is null)
            {
                return "";
            }

            // A presentation came back as a PDF, because Impress has no text filter at all — asking
            // for one answers "no export filter" and produces nothing. Its text is then read by the
            // PDF extractor this project already has, which is why that route was chosen over
            // giving up on presentations.
            return produced.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? _pdf.Extract(produced)
                : IndexableFiles.Trim(File.ReadAllText(produced));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Conversion impossible : {path} ({exception.GetType().Name})");
            return "";
        }
        finally
        {
            try
            {
                if (Directory.Exists(output))
                {
                    Directory.Delete(output, recursive: true);
                }
            }
            catch (IOException)
            {
                // A file still held by the converter. The whole workspace goes at the end anyway.
            }
        }
    }

    /// <summary>Runs one conversion, and says whether it finished.</summary>
    /// <remarks>
    /// The private profile is not optional. LibreOffice refuses to start a second instance against a
    /// profile already in use, so a run would silently do nothing on any machine where somebody has
    /// LibreOffice open — which, on a file server, is not obvious until it is.
    /// </remarks>
    private bool Convert(string path, string output)
    {
        var profile = new Uri(Path.Combine(_workspace, "profile")).AbsoluteUri;

        var start = new ProcessStartInfo(_soffice!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add($"-env:UserInstallation={profile}");
        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--norestore");
        start.ArgumentList.Add("--convert-to");
        start.ArgumentList.Add(FilterFor(Path.GetExtension(path)));
        start.ArgumentList.Add("--outdir");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(path);

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        if (process.WaitForExit((int)ConversionTimeout.TotalMilliseconds))
        {
            return true;
        }

        // A converter that hangs on one file must not hold the whole share. It happens on documents
        // that ask for a missing font or a network resource.
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        Console.Error.WriteLine($"Conversion abandonnée après {ConversionTimeout.TotalSeconds:0} s : {path}");

        return false;
    }

    /// <summary>
    /// The conversion filter for each family, all three of them different.
    /// </summary>
    /// <remarks>
    /// Measured against a real LibreOffice rather than assumed, and each one is a trap:
    ///
    /// <list type="bullet">
    /// <item>
    /// <b>Spreadsheets</b> need the filter's full name. The short alias <c>csv</c> is accepted,
    /// exits without error, and produces no file whatsoever — the worst kind of failure, because
    /// nothing anywhere says it did not work.
    /// </item>
    /// <item>
    /// <b>Presentations</b> have no text filter at all: <c>--convert-to txt</c> answers "no export
    /// filter for … aborting". They go through PDF, which Impress does export, and the PDF extractor
    /// reads it.
    /// </item>
    /// <item><b>Documents</b> convert to text directly, which is the only simple case.</item>
    /// </list>
    /// </remarks>
    private static string FilterFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".xls" or ".ods" or ".xlt" or ".ots" => "csv:Text - txt - csv (StarCalc)",
        ".ppt" or ".odp" or ".pot" or ".otp" => "pdf",
        _ => "txt",
    };

    /// <summary>Where LibreOffice is, or null.</summary>
    /// <remarks>
    /// The <c>PATH</c> first, because an administrator who put it there meant that one. The standard
    /// install locations after, because the installer does not add it to the <c>PATH</c> by default —
    /// which would otherwise make detection fail on a perfectly ordinary installation.
    /// </remarks>
    private static string? Find()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, "soffice.exe");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry. There is always one.
            }
        }

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(folder), "LibreOffice", "program", "soffice.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temporary files in the temporary folder. Windows will see to them.
        }
    }
}
