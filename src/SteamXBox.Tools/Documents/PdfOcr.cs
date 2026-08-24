using System.Diagnostics;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Lit le texte d'un PDF dont les pages sont des images.
/// </summary>
/// <remarks>
/// La seule route qui rend un document lisible quand il n'a aucune couche texte. Mesuré sur le PDF
/// de test du projet : l'import Writer de LibreOffice en tirait 14 octets, cette chaîne en tire
/// 5 301 caractères. Sans elle, un scan n'est trouvable que par son nom de fichier.
///
/// <para>
/// <b>Deux programmes détectés, jamais embarqués</b> — poppler pour dessiner la page, tesseract pour
/// la lire. C'est la règle que le projet applique déjà à LibreOffice, et pour poppler c'est une
/// obligation : sa licence est GPL, et le livrer avec un produit fermé exposerait le code de
/// SteamXBox. Absents, la fonction s'éteint et nomme ce qui manque.
/// </para>
/// </remarks>
public static class PdfOcr
{
    /// <summary>Assez fin pour un scan de texte courant, assez petit pour rester rapide.</summary>
    private const int Resolution = 200;

    /// <summary>Au-delà, un document tient la fenêtre trop longtemps pour un outil interactif.</summary>
    private const int MaxPages = 30;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static bool Handles(string input, string format)
        => format.Equals("ocr", StringComparison.OrdinalIgnoreCase)
           && Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ce qui manque sur cette machine, ou une chaîne vide si tout est là.</summary>
    public static string Missing()
    {
        var manques = new List<string>();

        if (Find("pdftoppm") is null) manques.Add("pdftoppm (paquet poppler)");
        if (Find("tesseract") is null) manques.Add("tesseract");

        return manques.Count == 0 ? "" : string.Join(" et ", manques);
    }

    /// <summary>
    /// Écrit le texte reconnu à côté du document, et dit où.
    /// </summary>
    /// <remarks>
    /// À côté de l'original, comme les autres routes : l'utilisateur a désigné ce dossier, c'est
    /// donc le seul qu'il a déjà accepté, et c'est là qu'il cherchera le résultat.
    /// </remarks>
    public static string Convert(string input, string folder)
    {
        if (Missing() is { Length: > 0 } manque)
        {
            return $"Reconnaissance impossible : il manque {manque}.";
        }

        var pdftoppm = Find("pdftoppm")!;
        var tesseract = Find("tesseract")!;
        var travail = Path.Combine(Path.GetTempPath(), "SteamXBox.Ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(travail);

        var texte = new System.Text.StringBuilder();
        var lues = 0;
        var cherchable = "";

        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var prefixe = Path.Combine(travail, $"p{page:D4}");

                if (!Lancer(pdftoppm, Patience,
                        "-png", "-r", Resolution.ToString(),
                        "-f", page.ToString(), "-l", page.ToString(),
                        input, prefixe))
                {
                    break;
                }

                var image = Directory.EnumerateFiles(travail, Path.GetFileName(prefixe) + "*.png").FirstOrDefault();

                // Plus d'image : la page demandée n'existe pas, donc le document est fini. C'est la
                // façon la moins coûteuse de compter les pages sans ouvrir le PDF nous-mêmes.
                if (image is null)
                {
                    break;
                }

                var lu = Lire(tesseract, image);

                if (lu.Trim().Length > 0)
                {
                    texte.Append(lu).Append('\n');
                    lues++;
                }

                // La meme page, rendue en PDF avec sa couche de texte invisible par-dessus l'image.
                // C'est tesseract qui la fabrique — un argument, pas un travail de notre part — et
                // c'est ce qui rend le document cherchable sans rien changer a ce qu'on voit.
                Lancer(tesseract, Patience, image, Path.Combine(travail, $"t{page:D4}"), "-l", "fra+eng", "pdf");

                File.Delete(image);
            }

            // Recollé ici, avant que le dossier de travail ne soit efface par le finally.
            cherchable = Recoller(folder, input, travail);
        }
        finally
        {
            try
            {
                Directory.Delete(travail, recursive: true);
            }
            catch (Exception)
            {
                // Le nettoyage du système reprendra ce qui résiste.
            }
        }

        if (texte.Length == 0)
        {
            return "Aucun texte reconnu : les pages sont peut-être vides ou illisibles.";
        }

        var sortie = Path.Combine(folder, Path.GetFileNameWithoutExtension(input) + ".txt");
        File.WriteAllText(sortie, texte.ToString(), new System.Text.UTF8Encoding(false));

        return cherchable.Length > 0
            ? $"{lues} page(s) lue(s), {texte.Length} caractères → {Path.GetFileName(sortie)} et {cherchable}"
            : $"{lues} page(s) lue(s), {texte.Length} caractères → {Path.GetFileName(sortie)}";
    }

    /// <summary>
    /// Recolle les pages en un PDF cherchable, posé à côté de l'original.
    /// </summary>
    /// <remarks>
    /// Chaque page a été écrite par tesseract avec sa couche de texte invisible par-dessus l'image :
    /// le document se lit exactement comme avant, et se cherche comme un vrai texte. C'est ce que
    /// demandait « du texte ajouté au PDF » plutôt qu'un fichier à côté.
    ///
    /// <para>
    /// L'original n'est jamais écrasé : le résultat prend le suffixe <c>-texte</c>. Une reconnaissance
    /// se trompe, et remplacer le document source par une version dont le texte est une supposition
    /// est le genre de perte qu'on ne remarque que trop tard.
    /// </para>
    /// </remarks>
    private static string Recoller(string folder, string input, string travail)
    {
        var pages = Directory.EnumerateFiles(travail, "t*.pdf").OrderBy(p => p).ToList();

        if (pages.Count == 0 || Find("pdfunite") is not { } pdfunite)
        {
            return "";
        }

        var sortie = Path.Combine(folder, Path.GetFileNameWithoutExtension(input) + "-texte.pdf");
        var arguments = new List<string>(pages) { sortie };

        return Lancer(pdfunite, Patience, [.. arguments]) && File.Exists(sortie)
            ? Path.GetFileName(sortie)
            : "";
    }

    private static string Lire(string tesseract, string image)
    {
        var start = Depart(tesseract);
        start.ArgumentList.Add(image);
        start.ArgumentList.Add("stdout");
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add("fra+eng");

        using var process = Process.Start(start);

        if (process is null)
        {
            return "";
        }

        // Les deux sorties se vident ensemble : voir Tuyaux.Vider.
        return Serveurs.Tuyaux.Vider(process, Patience).Sortie;
    }

    private static bool Lancer(string programme, TimeSpan patience, params string[] arguments)
    {
        var start = Depart(programme);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        // Les deux sorties se vident ensemble : voir Tuyaux.Vider.
        Serveurs.Tuyaux.Vider(process, patience);

        return process.HasExited && process.ExitCode == 0;
    }

    private static ProcessStartInfo Depart(string programme) => new(programme)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    /// <summary>
    /// Les emplacements d'installation habituels, essayés avant le PATH.
    /// </summary>
    /// <remarks>
    /// Comme <c>LibreOffice.Find()</c>, et pour la raison qu'un essai a démontrée : l'installeur de
    /// tesseract ne met rien sur le PATH, donc un utilisateur qui l'installe normalement obtenait
    /// « tesseract pas trouvé » avec le programme pourtant présent. Exiger une modification du PATH
    /// pour qu'une fonction marche, c'est demander à l'utilisateur de réparer notre détection.
    ///
    /// <para>
    /// Poppler n'a pas d'emplacement standard sous Windows — il s'y distribue en archive — donc lui
    /// reste au PATH, et c'est ce que dit le message quand il manque.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> EmplacementsConnus(string nom)
    {
        if (!nom.Equals("tesseract", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        yield return @"C:\Program Files\Tesseract-OCR\tesseract.exe";
        yield return @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Tesseract-OCR", "tesseract.exe");
    }

    /// <summary>Cherche un exécutable aux emplacements connus, puis sur le PATH.</summary>
    private static string? Find(string nom)
    {
        foreach (var candidat in EmplacementsConnus(nom))
        {
            try
            {
                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (Exception)
            {
                // Un chemin inaccessible ne doit pas arrêter la recherche des suivants.
            }
        }

        foreach (var dossier in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidat = Path.Combine(dossier.Trim(), nom + ".exe");

                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (Exception)
            {
                // Une entrée de PATH malformée ne doit pas arrêter la recherche des suivantes.
            }
        }

        return null;
    }
}
