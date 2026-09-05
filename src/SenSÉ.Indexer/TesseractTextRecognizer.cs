using System.Diagnostics;

namespace SenSÉ.Indexer;

/// <summary>
/// Reads an image with <c>tesseract</c>, when the machine has it.
/// </summary>
/// <remarks>
/// Detected on the PATH, never shipped. The rule is the one written beside LibreOffice in this
/// project: an engine that parses files coming from outside is an attack surface, and being the
/// party that delivers its security fixes to a school is not a role to take on. It also happens to
/// be the portable choice — tesseract exists on Linux, so the day the host is no longer Windows,
/// this class does not change.
///
/// <para>
/// The language matters more than the engine. Without the French data file, a French page comes back
/// as plausible English nonsense rather than as nothing, which is worse: it indexes, it looks like it
/// worked, and the document is unfindable by its own words. So the languages present are read once,
/// reported, and the ones asked for are the ones installed.
/// </para>
/// </remarks>
public sealed class TesseractTextRecognizer : ITextRecognizer
{
    /// <summary>A page that will not be read in this long is not one worth waiting for.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string? _tesseract;
    private readonly string _languages;

    /// <param name="wanted">
    /// The languages to try, in tesseract's own notation (<c>fra+eng</c>). Only those actually
    /// installed are kept: asking for a missing one makes tesseract fail the whole page.
    /// </param>
    public TesseractTextRecognizer(string wanted = "fra+eng")
    {
        _tesseract = PdfToPpmPageRenderer.Find("tesseract");
        _languages = _tesseract is null ? "" : Keep(wanted, Installed(_tesseract));
    }

    public bool IsAvailable => _tesseract is not null && _languages.Length > 0;

    public string Describe()
    {
        if (_tesseract is null)
        {
            return "aucune reconnaissance de caracteres : tesseract est introuvable";
        }

        return _languages.Length == 0
            ? $"{_tesseract} est installe, mais aucune des langues demandees n'est presente"
            : $"reconnaissance par {_tesseract}, langues {_languages}";
    }

    public string Read(string imagePath)
    {
        if (!IsAvailable)
        {
            return "";
        }

        var start = new ProcessStartInfo(_tesseract!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(imagePath);
        start.ArgumentList.Add("stdout");
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add(_languages);

        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                return "";
            }

            var text = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(Patience))
            {
                Kill(process);
                return "";
            }

            return text;
        }
        catch (Exception)
        {
            // Une page illisible n'arrete pas l'indexation du partage.
            return "";
        }
    }

    /// <summary>Les langues que cette installation possede reellement.</summary>
    private static HashSet<string> Installed(string tesseract)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var start = new ProcessStartInfo(tesseract)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            start.ArgumentList.Add("--list-langs");

            using var process = Process.Start(start);

            if (process is null)
            {
                return found;
            }

            // La premiere ligne est un en-tete ; les suivantes sont les codes, un par ligne.
            var sortie = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            foreach (var ligne in sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var code = ligne.Trim();

                if (code.Length is > 0 and <= 8)
                {
                    found.Add(code);
                }
            }
        }
        catch (Exception)
        {
            // Une installation qui ne repond pas est traitee comme une installation sans langue.
        }

        return found;
    }

    private static string Keep(string wanted, HashSet<string> installed)
        => string.Join(
            '+',
            wanted.Split('+', StringSplitOptions.RemoveEmptyEntries).Where(installed.Contains));

    private static void Kill(Process? process)
    {
        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Deja mort, ou hors d'atteinte.
        }
    }
}
