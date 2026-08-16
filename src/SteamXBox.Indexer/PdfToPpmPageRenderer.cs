using System.Diagnostics;

namespace SteamXBox.Indexer;

/// <summary>
/// Draws a PDF page with <c>pdftoppm</c>, when the machine has it.
/// </summary>
/// <remarks>
/// Poppler's renderer, found on the PATH. Chosen over the renderer built into Windows for one
/// reason: it exists on Linux too, and the day the host is no longer Windows this class keeps
/// working unchanged. Nothing is shipped with the product — the administrator installs poppler, or
/// the feature stays off and says so.
///
/// <para>
/// One process per page rather than one for the document. A scanned share holds files of six
/// hundred pages, and rendering all of them to read the first ten would cost minutes and a gigabyte
/// of temporary files per document.
/// </para>
/// </remarks>
public sealed class PdfToPpmPageRenderer : IPageRenderer, IDisposable
{
    /// <summary>Wide enough for a scan of ordinary body text, small enough to stay fast.</summary>
    private const int Resolution = 200;

    /// <summary>A page that will not draw in this long is not one worth waiting for.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string? _pdftoppm;
    private readonly string _workspace;

    public PdfToPpmPageRenderer()
    {
        _pdftoppm = Find("pdftoppm");
        _workspace = Path.Combine(Path.GetTempPath(), "SteamXBox.Indexer.Ocr", Guid.NewGuid().ToString("N"));
    }

    public bool IsAvailable => _pdftoppm is not null;

    public string Describe()
        => _pdftoppm is null
            ? "aucun moteur de rendu : pdftoppm est introuvable (paquet poppler)"
            : $"rendu par {_pdftoppm}, {Resolution} points par pouce";

    public string? RenderPage(string path, int page)
    {
        if (_pdftoppm is null)
        {
            return null;
        }

        Directory.CreateDirectory(_workspace);

        // pdftoppm ajoute lui-meme le numero de page et l'extension au prefixe qu'on lui donne.
        var prefix = Path.Combine(_workspace, Guid.NewGuid().ToString("N"));

        var start = new ProcessStartInfo(_pdftoppm)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        start.ArgumentList.Add("-png");
        start.ArgumentList.Add("-r");
        start.ArgumentList.Add(Resolution.ToString());
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(page.ToString());
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add(page.ToString());
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(prefix);

        try
        {
            using var process = Process.Start(start);

            if (process is null || !process.WaitForExit(Patience))
            {
                Kill(process);
                return null;
            }

            // Le suffixe depend de la version et du nombre de pages : on prend ce qui est apparu.
            return Directory
                .EnumerateFiles(_workspace, Path.GetFileName(prefix) + "*.png")
                .FirstOrDefault();
        }
        catch (Exception)
        {
            // Un document qui ne se rend pas n'arrete pas l'indexation du partage.
            return null;
        }
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
        catch (Exception)
        {
            // Un temporaire qui resiste sera repris par le nettoyage du systeme.
        }
    }

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

    /// <summary>Cherche un executable sur le PATH, avec l'extension qui convient au systeme.</summary>
    internal static string? Find(string name)
    {
        var noms = OperatingSystem.IsWindows() ? new[] { name + ".exe", name } : [name];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var nom in noms)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim(), nom);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // Une entree de PATH malformee ne doit pas arreter la recherche des suivantes.
                }
            }
        }

        return null;
    }
}
