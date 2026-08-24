using System.Diagnostics;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// The document converter SteamXBox borrows rather than carries.
/// </summary>
/// <remarks>
/// Shared by the indexer, which reads formats no .NET library will open under an acceptable licence,
/// and by the conversion tool. One copy on purpose: the detection and the filter names were each
/// found by measurement rather than by reading documentation, and a second copy would drift from
/// them within a release.
///
/// <para>
/// <b>Detected, never bundled.</b> LibreOffice is three hundred and fifty megabytes and a security
/// surface that opens files from outside; shipping it would make this project its patch maintainer
/// towards schools and companies handling sensitive data. It runs as a separate process — so its
/// MPL never reaches this product either — and when it is absent the feature simply says so.
/// </para>
/// </remarks>
public static class LibreOffice
{
    /// <summary>Long enough for a large document, short enough not to hang a window.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    private static readonly string[] Candidates =
    [
        @"C:\Program Files\LibreOffice\program\soffice.exe",
        @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
    ];

    /// <summary>Whether LibreOffice is on this machine.</summary>
    public static bool IsInstalled => Find() is not null;

    /// <summary>
    /// Where LibreOffice is, or null.
    /// </summary>
    /// <remarks>
    /// The <c>PATH</c> first, because an administrator who put it there meant that one. The standard
    /// install locations after, because the installer does not add it to the <c>PATH</c> — which
    /// would otherwise make detection fail on a perfectly ordinary installation.
    /// </remarks>
    public static string? Find()
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

        return Candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>What a document can be turned into, and the filter that does it.</summary>
    /// <remarks>
    /// The filter names are measured, not guessed, and two of them are traps. A spreadsheet needs the
    /// filter's full name — the short alias <c>csv</c> is accepted, exits without error and produces
    /// no file at all, which is the worst kind of failure because nothing says it did not work. A
    /// presentation has no text filter whatsoever.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Formats { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["docx"] = "docx:MS Word 2007 XML",
            ["odt"] = "odt",
            ["xlsx"] = "xlsx:Calc MS Excel 2007 XML",
            ["ods"] = "ods",
            ["pptx"] = "pptx:Impress MS PowerPoint 2007 XML",
            ["odp"] = "odp",
            ["pdf"] = "pdf",
            ["txt"] = "txt",
            ["csv"] = "csv:Text - txt - csv (StarCalc)",
            ["html"] = "html",
            ["rtf"] = "rtf",
        };

    /// <summary>The formats a Writer filter can produce.</summary>
    private static readonly string[] WriterFormats = ["docx", "odt", "rtf", "txt", "html"];

    /// <summary>
    /// How the source has to be opened, when leaving it to LibreOffice gets it wrong.
    /// </summary>
    /// <remarks>
    /// A PDF is the case, and it fails in a way that gives no clue. LibreOffice opens a PDF in Draw,
    /// so asking for a Writer format finds nothing to write and it stops with "no export filter …
    /// aborting" — a message about the <i>output</i> for a problem with the <i>input</i>. Naming the
    /// import filter makes it a Writer document first, and the same command then produces two
    /// hundred and fifty kilobytes of .docx.
    ///
    /// <para>
    /// Measured on this machine against a real PDF, like the two other traps in this file: the
    /// spreadsheet alias that writes nothing, and Impress having no text filter at all.
    /// </para>
    /// </remarks>
    private static string? ImportFilterFor(string input, string format)
        => Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           && WriterFormats.Contains(format, StringComparer.OrdinalIgnoreCase)
            ? "writer_pdf_import"
            : null;

    /// <summary>What came out of a conversion.</summary>
    /// <param name="Produced">The file that was written, or empty when none was.</param>
    /// <param name="Problem">Why nothing was written, or empty on success.</param>
    public readonly record struct Result(string Produced, string Problem)
    {
        private readonly string? _trace;

        public bool Worked => Produced.Length > 0;

        /// <summary>
        /// Ce que la route a compté en chemin, quand elle a de quoi le dire.
        /// </summary>
        /// <remarks>
        /// <b>Porté par le résultat, et non par une propriété statique.</b> Le compte vivait sur la
        /// classe qui l'écrit, et n'y était remis à zéro que par la route qui l'écrit : toute autre
        /// conversion réussie relisait donc celui de la précédente et l'annonçait comme le sien. Une
        /// conversion .docx vers .odt affichait « 412 morceau(x) dont 19 image(s) », comptés sur un
        /// PDF converti dix minutes plus tôt.
        ///
        /// <para>
        /// Attaché au résultat, il ne peut plus survivre à ce qu'il décrit. Vide pour les routes qui
        /// ne comptent rien, ce qui est le cas de toutes sauf une.
        /// </para>
        ///
        /// <para>
        /// Lu à travers un champ qui accepte le nul : un <c>default(Result)</c> reste une valeur
        /// légitime pour une structure, et rendre nul depuis une propriété déclarée non nulle
        /// vaudrait à l'appelant une exception là où il attend une phrase vide.
        /// </para>
        /// </remarks>
        public string Trace
        {
            get => _trace ?? "";
            init => _trace = value;
        }
    }

    /// <summary>
    /// Converts one document, and says what happened.
    /// </summary>
    /// <remarks>
    /// The private profile is not optional. LibreOffice refuses to start a second instance against a
    /// profile already in use, so a conversion would silently do nothing on any machine where
    /// somebody has LibreOffice open — which is most machines that have it at all.
    /// </remarks>
    public static Result Convert(string input, string format, string outputDirectory, Action<string>? log = null)
    {
        if (Find() is not { } soffice)
        {
            return new Result("", "LibreOffice n'est pas installé sur cette machine.");
        }

        if (!Formats.TryGetValue(format, out var filter))
        {
            return new Result("", $"Format inconnu : {format}.");
        }

        if (!File.Exists(input))
        {
            return new Result("", "Le fichier à convertir est introuvable.");
        }

        // One profile, kept, and one conversion at a time.
        //
        // A fresh profile per conversion made LibreOffice redo its whole first-run initialisation
        // every time — seconds of work before it even looked at the document, on top of a
        // conversion that is already slow. It is still a profile of our own rather than the user's,
        // which is the point: LibreOffice refuses to start against a profile already in use, so
        // sharing theirs would silently do nothing whenever they have LibreOffice open.
        //
        // The lock is what makes reuse safe. Two conversions against one profile is the very clash
        // the private profile existed to avoid, so they queue instead.
        lock (Gate)
        {
        var workspace = Path.Combine(Path.GetTempPath(), "SteamXBox.Convert", "profile");

        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(outputDirectory);

            var start = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            start.ArgumentList.Add($"-env:UserInstallation={new Uri(workspace).AbsoluteUri}");
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--norestore");

            if (ImportFilterFor(input, format) is { } importFilter)
            {
                start.ArgumentList.Add($"--infilter={importFilter}");
            }

            start.ArgumentList.Add("--convert-to");
            start.ArgumentList.Add(filter);
            start.ArgumentList.Add("--outdir");
            start.ArgumentList.Add(outputDirectory);
            start.ArgumentList.Add(input);

            using var process = Process.Start(start);

            if (process is null)
            {
                return new Result("", "LibreOffice n'a pas pu être démarré.");
            }

            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                // A converter that hangs must not hold the window. It happens on documents asking
                // for a missing font or a network resource.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return new Result("", $"Abandonné après {Timeout.TotalSeconds:0} secondes.");
            }

            var expected = Path.Combine(
                outputDirectory, Path.GetFileNameWithoutExtension(input) + "." + format.ToLowerInvariant());

            if (File.Exists(expected))
            {
                log?.Invoke($"converted {Path.GetFileName(input)} to {format}.");
                return new Result(expected, "");
            }

            // LibreOffice names the result after the input with the new extension, but the filter
            // decides the extension and they do not always agree. Whatever landed is the answer.
            var produced = Directory.GetFiles(outputDirectory)
                .Where(file => !file.Equals(input, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return produced is not null
                ? new Result(produced, "")
                : new Result("", "LibreOffice n'a produit aucun fichier.");
        }
        catch (Exception exception)
        {
            log?.Invoke($"conversion failed: {exception.GetType().Name}: {exception.Message}");
            return new Result("", exception.Message);
        }
        }
    }

    /// <summary>One conversion at a time, so the reused profile is never opened twice.</summary>
    private static readonly Lock Gate = new();
}
