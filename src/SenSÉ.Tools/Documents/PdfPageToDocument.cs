using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SenSÉ.Tools.Documents;

/// <summary>
/// Convertit un PDF en Word en rendant chaque page telle qu'on la voit.
/// </summary>
/// <remarks>
/// La troisième route, à côté du flux éditable et de la mise en page reconstruite. Celle-ci ne
/// reconstruit rien : elle demande à <c>pdftocairo</c> l'image composée de chaque page — masques de
/// transparence appliqués, superpositions résolues — et la pose en pleine page.
///
/// <para>
/// <b>Pourquoi elle existe.</b> Mesuré sur le document de test le 18 août : 18 des 19 images portent
/// un masque de transparence, que ni PdfPig ni <c>pdfimages</c> n'appliquent — le second sort même
/// du gris sans canal alpha. Une illustration détourée devient alors un rectangle plein posé sur la
/// page. Recomposer chaque masque à la main serait long et fragile ; rendre la page l'est déjà, et
/// par un moteur dont c'est le métier.
/// </para>
///
/// <para>
/// <b>Ce qu'elle coûte, et il faut le savoir avant de la choisir.</b> Le document n'est plus
/// éditable : une page est une image, on ne déplace plus un logo ni ne corrige un mot. Elle est
/// fidèle à l'œil et muette au clavier. C'est l'inverse exact de la route en flux, et c'est pour ça
/// que les deux coexistent au lieu de se remplacer.
/// </para>
///
/// <para>
/// <b>Le texte n'est pas perdu pour autant.</b> Chaque page reçoit son texte en blanc, derrière
/// l'image : invisible à la lecture, mais trouvé par la recherche de Word et copiable. C'est la même
/// idée que la couche invisible d'un PDF cherchable, appliquée à un document Word.
/// </para>
///
/// <para>
/// Rien n'est embarqué : <c>pdftocairo</c> est détecté sur la machine, comme LibreOffice et
/// tesseract. Absent, la route s'éteint et le dit — elle ne se rabat pas en silence sur une autre,
/// qui donnerait un résultat différent de celui qui a été demandé.
/// </para>
/// </remarks>
public static class PdfPageToDocument
{
    /// <summary>Le format que cette route sert, distinct du <c>docx</c> éditable.</summary>
    public const string Format = "docx-image";

    /// <summary>
    /// Cent cinquante points par pouce.
    /// </summary>
    /// <remarks>
    /// Assez pour que le texte d'un scan reste net à l'écran et à l'impression courante, assez peu
    /// pour qu'une page A4 pèse quelques centaines de kilooctets. À 300, le même document quadruple
    /// sans que l'œil y gagne à l'écran.
    /// </remarks>
    private const int Resolution = 150;

    /// <summary>Une page qui ne se rend pas en une minute ne se rendra pas.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    /// <summary>Un point vaut 12 700 unités anglaises, la mesure de Word.</summary>
    private const long UnitsPerPoint = 12700;

    public static bool Handles(string input, string format)
        => format.Equals(Format, StringComparison.OrdinalIgnoreCase)
           && Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ce qui manque sur cette machine, ou une chaîne vide si la route est utilisable.</summary>
    public static string Missing()
        => Find("pdftocairo") is null ? "pdftocairo (paquet poppler)" : "";

    public static LibreOffice.Result Convert(string input, string outputDirectory)
    {
        if (Missing() is { Length: > 0 } manque)
        {
            return new LibreOffice.Result("", $"Rendu impossible : il manque {manque}.");
        }

        var pdftocairo = Find("pdftocairo")!;
        var travail = Path.Combine(Path.GetTempPath(), "SenSÉ.PageRender", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(travail);
            Directory.CreateDirectory(outputDirectory);

            // Tout le document en une passe : un processus par page coûterait une seconde de
            // démarrage chacune, ce qui se voit sur un document de cent pages.
            if (!Rendre(pdftocairo, input, Path.Combine(travail, "p")))
            {
                return new LibreOffice.Result("", "Le rendu des pages a échoué.");
            }

            var pages = Directory.EnumerateFiles(travail, "p*.png").OrderBy(p => p).ToList();

            if (pages.Count == 0)
            {
                return new LibreOffice.Result("", "Aucune page n'a été rendue.");
            }

            var textes = PdfToDocument.Read(input)
                .Where(piece => !piece.IsPicture && piece.Text.Length > 0)
                .Select(piece => piece.Text)
                .ToList();

            var output = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(input) + "." + Format + ".docx");

            Ecrire(output, pages, textes);

            return new LibreOffice.Result(output, "");
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
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
    }

    private static bool Rendre(string pdftocairo, string input, string prefixe)
    {
        var start = new ProcessStartInfo(pdftocairo)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("-png");
        start.ArgumentList.Add("-r");
        start.ArgumentList.Add(Resolution.ToString());
        start.ArgumentList.Add(input);
        start.ArgumentList.Add(prefixe);

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        // Les deux sorties se vident ensemble : voir Tuyaux.Vider.
        Serveurs.Tuyaux.Vider(process, Patience);

        return process.HasExited && process.ExitCode == 0;
    }

    /// <summary>
    /// Une page par page, l'image devant et son texte derrière.
    /// </summary>
    /// <remarks>
    /// Le texte est écrit en blanc et en corps minimal, dans le même paragraphe que l'image : Word le
    /// trouve et le copie, l'œil ne le voit pas. Sans lui, le document serait un album de captures —
    /// introuvable par ses propres mots, ce qui est exactement le défaut que la reconnaissance de
    /// caractères a servi à corriger ailleurs dans ce produit.
    /// </remarks>
    private static void Ecrire(string output, List<string> pages, List<string> textes)
    {
        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();

        var body = main.Document.AppendChild(new Body());
        var numero = 0U;

        foreach (var page in pages)
        {
            numero++;

            var part = main.AddImagePart(ImagePartType.Png);

            using (var octets = File.OpenRead(page))
            {
                part.FeedData(octets);
            }

            var (largeur, hauteur) = Taille(page);

            body.AppendChild(new Paragraph(new Run(new Drawing(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent
                    {
                        Cx = largeur,
                        Cy = hauteur,
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent
                    {
                        LeftEdge = 0,
                        TopEdge = 0,
                        RightEdge = 0,
                        BottomEdge = 0,
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties
                    {
                        Id = numero,
                        Name = $"Page {numero}",
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                        new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks { NoChangeAspect = true }),
                    Graphique(main.GetIdOfPart(part), numero, largeur, hauteur))))));

            if (numero <= textes.Count)
            {
                body.AppendChild(new Paragraph(
                    new Run(
                        new RunProperties(
                            new Color { Val = "FFFFFF" },
                            new FontSize { Val = "2" }),
                        new Text(textes[(int)numero - 1]) { Space = SpaceProcessingModeValues.Preserve })));
            }

            if (numero < (uint)pages.Count)
            {
                body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }
        }

        main.Document.Save();
    }

    private static DocumentFormat.OpenXml.Drawing.Graphic Graphique(
        string relation,
        uint numero,
        long largeur,
        long hauteur)
        => new(new DocumentFormat.OpenXml.Drawing.GraphicData(
            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties
                    {
                        Id = numero,
                        Name = $"Page {numero}",
                    },
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                    new DocumentFormat.OpenXml.Drawing.Blip { Embed = relation },
                    new DocumentFormat.OpenXml.Drawing.Stretch(
                        new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                    new DocumentFormat.OpenXml.Drawing.Transform2D(
                        new DocumentFormat.OpenXml.Drawing.Offset { X = 0L, Y = 0L },
                        new DocumentFormat.OpenXml.Drawing.Extents { Cx = largeur, Cy = hauteur }),
                    new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                        new DocumentFormat.OpenXml.Drawing.AdjustValueList())
                    {
                        Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle,
                    })))
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
        });

    /// <summary>
    /// La taille d'affichage, lue dans l'en-tête du PNG.
    /// </summary>
    /// <remarks>
    /// Les pixels rendus divisés par la résolution donnent des points, et les points donnent la
    /// mesure de Word. Sans cette conversion, une page rendue à 150 points par pouce s'afficherait à
    /// deux fois sa taille — la même confusion entre pixels et points qui a déjà vidé un document de
    /// ses images le 17 août.
    /// </remarks>
    private static (long Largeur, long Hauteur) Taille(string png)
    {
        try
        {
            var entete = new byte[24];

            using (var flux = File.OpenRead(png))
            {
                if (flux.Read(entete, 0, entete.Length) < 24)
                {
                    return (5_486_400L, 7_772_400L);
                }
            }

            var pixelsLarge = (entete[16] << 24) | (entete[17] << 16) | (entete[18] << 8) | entete[19];
            var pixelsHaut = (entete[20] << 24) | (entete[21] << 16) | (entete[22] << 8) | entete[23];

            return (
                (long)(pixelsLarge * 72.0 / Resolution * UnitsPerPoint),
                (long)(pixelsHaut * 72.0 / Resolution * UnitsPerPoint));
        }
        catch (Exception)
        {
            // A4 par défaut : un document mal dimensionné vaut mieux qu'une conversion perdue.
            return (5_486_400L, 7_772_400L);
        }
    }

    /// <summary>Cherche un exécutable sur le PATH, comme les autres outils détectés.</summary>
    private static string? Find(string nom)
    {
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
