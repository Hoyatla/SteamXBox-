using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SenSÉ.EditeurTexte.Format;

/// <summary>
/// Writer/reader natif de fichiers .docx (Office Open XML) via
/// <see cref="DocumentFormat.OpenXml"/>. Pas de wrapper sur LibreOffice :
/// on parle directement le format docx (ZIP + XML selon ECMA-376).
/// </summary>
/// <remarks>
/// <b>Note sur les collisions de noms.</b> <c>DocumentFormat.OpenXml.Wordprocessing</c>
/// contient un <c>Paragraph</c> et un <c>Run</c>, et <c>System.Windows.Documents</c>
/// aussi. On importe OpenXml.Wordprocessing sans lier ses Paragraph/Run, et on
/// les qualifie a la main. Les types WPF sont reconnus par leur namespace
/// usuel <c>System.Windows.Documents.Paragraph</c> / <c>Run</c>.
/// </remarks>
public static class Docx
{
    /// <summary>Sauvegarde un FlowDocument en .docx (Office Open XML).</summary>
    public static void VersDocx(FlowDocument doc, string chemin)
    {
        if (File.Exists(chemin)) File.Delete(chemin);

        using var pkg = WordprocessingDocument.Create(chemin, WordprocessingDocumentType.Document);
        var mainPart = pkg.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Phase F : compteur d'images pour les Drawing inline.
        var imageCounter = 0;

        foreach (var block in doc.Blocks)
        {
            var element = BlockVersWml(block, mainPart, ref imageCounter);
            if (element is not null) body.AppendChild(element);
        }

        mainPart.Document.Save();
    }

    /// <summary>Charge un .docx dans un FlowDocument existant (vide prealablement).</summary>
    public static void DepuisDocx(FlowDocument cible, string chemin)
    {
        cible.Blocks.Clear();
        using var pkg = WordprocessingDocument.Open(chemin, false);
        var mainPart = pkg.MainDocumentPart!;
        var body = mainPart.Document.Body!;
        foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            cible.Blocks.Add(ParagrapheWmlVersFlow(para, mainPart));
        }
    }

    // ============== Writer ==============

    private static OpenXmlElement? BlockVersWml(System.Windows.Documents.Block block, MainDocumentPart mainPart, ref int imageCounter) => block switch
    {
        System.Windows.Documents.Paragraph p when EstTitre1(p) => TitreWml("Heading1", RunsVersWml(p.Inlines, mainPart, ref imageCounter)),
        System.Windows.Documents.Paragraph p when EstTitre2(p) => TitreWml("Heading2", RunsVersWml(p.Inlines, mainPart, ref imageCounter)),
        System.Windows.Documents.Paragraph p when EstTitre3(p) => TitreWml("Heading3", RunsVersWml(p.Inlines, mainPart, ref imageCounter)),
        System.Windows.Documents.Paragraph p when EstListePucese(p) => ListeWml(p, numbered: false, mainPart, ref imageCounter),
        System.Windows.Documents.Paragraph p when EstListeNum(p)   => ListeWml(p, numbered: true, mainPart, ref imageCounter),
        System.Windows.Documents.Paragraph p => ParagrapheWml(RunsVersWml(p.Inlines, mainPart, ref imageCounter)),
        _ => null,
    };

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph ParagrapheWml(System.Collections.Generic.IEnumerable<OpenXmlElement> runs)
    {
        var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
        foreach (var r in runs) p.AppendChild(r);
        return p;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph TitreWml(string styleId, System.Collections.Generic.IEnumerable<OpenXmlElement> runs)
    {
        var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
        var pPr = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
            new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = styleId });
        p.AppendChild(pPr);
        foreach (var r in runs) p.AppendChild(r);
        return p;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph ListeWml(System.Windows.Documents.Paragraph src, bool numbered, MainDocumentPart mainPart, ref int imageCounter)
    {
        // Phase B-prime : prefixe texte (les vrais w:num demandent un
        // NumberingDefinitionsPart, qu'on ajoutera en Phase D si besoin).
        var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
        var prefixe = numbered ? "1. " : "* ";
        p.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
            new DocumentFormat.OpenXml.Wordprocessing.Text(prefixe) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        foreach (var r in RunsVersWml(src.Inlines, mainPart, ref imageCounter)) p.AppendChild(r);
        return p;
    }

    private static System.Collections.Generic.List<OpenXmlElement> RunsVersWml(InlineCollection inlines, MainDocumentPart mainPart, ref int imageCounter)
    {
        var liste = new System.Collections.Generic.List<OpenXmlElement>();
        if (inlines.Count == 0)
        {
            liste.Add(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("")));
            return liste;
        }
        foreach (var inline in inlines)
        {
            if (inline is System.Windows.Documents.Run run) liste.Add(RunVersWml(run));
            else if (inline is LineBreak) liste.Add(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Break()));
            else if (inline is InlineUIContainer iuc && iuc.Child is System.Windows.Controls.Image)
                liste.Add(ImageRunWml(mainPart, (System.Windows.Controls.Image)iuc.Child, ref imageCounter));
            // Hyperlink, etc. : ignores.
        }
        return liste;
    }

    /// <summary>Construit le Run w:drawing/wp:inline pour une image, en utilisant
    /// le schema DrawingML Pictures (pic:pic / a:blip) requis par Word.</summary>
    private static DocumentFormat.OpenXml.Wordprocessing.Run ImageRunWml(MainDocumentPart main, System.Windows.Controls.Image img, ref int imageCounter)
    {
        // 1) Charger l'image (1600px max) et l'ajouter au package comme ImagePart PNG.
        // Le chemin source est recupere depuis le BitmapImage.UriSource. Si l'image
        // n'a pas de source fichier (image programme), on saute et on met un Run vide.
        BitmapImage? bi = img.Source as BitmapImage;
        string? chemin = null;
        if (bi is not null && bi.UriSource is not null)
        {
            try { chemin = bi.UriSource.LocalPath; } catch { chemin = null; }
        }
        if (string.IsNullOrEmpty(chemin) || !System.IO.File.Exists(chemin))
            return new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(""));

        byte[]? bytes = null;
        try { bytes = System.IO.File.ReadAllBytes(chemin); } catch { }
        if (bytes is null || bytes.Length == 0)
            return new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(""));

        var part = main.AddImagePart(ImagePartType.Png);
        using (var partStream = part.GetStream(System.IO.FileMode.Create))
        {
            partStream.Write(bytes, 0, bytes.Length);
        }
        var relId = main.GetIdOfPart(part);
        imageCounter++;

        // 2) Dimensions en EMU. 1 pixel @ 96 DPI = 9525 EMU.
        BitmapImage biLocal = bi!;
        int pixelW = (int)(biLocal.PixelWidth > 0 ? biLocal.PixelWidth : img.ActualWidth);
        int pixelH = (int)(biLocal.PixelHeight > 0 ? biLocal.PixelHeight : img.ActualHeight);
        if (pixelW <= 0) pixelW = 800;
        if (pixelH <= 0) pixelH = 600;
        long emuW = (long)pixelW * 9525L;
        long emuH = (long)pixelH * 9525L;

        // 3) XML inline complet. On utilise InnerXml pour eviter de naviguer
        // dans la hierarchie OpenXml verbeuse (Drawing/Inline/Extent/DocProperties/...).
        var drawingXml = "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
            + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
            + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
            + "<wp:extent cx=\"" + emuW + "\" cy=\"" + emuH + "\"/>"
            + "<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>"
            + "<wp:docPr id=\"" + imageCounter + "\" name=\"Image " + imageCounter + "\"/>"
            + "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>"
            + "<a:graphic>"
            + "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + "<pic:pic>"
            + "<pic:nvPicPr>"
            + "<pic:cNvPr id=\"0\" name=\"img.png\"/>"
            + "<pic:cNvPicPr/>"
            + "</pic:nvPicPr>"
            + "<pic:blipFill>"
            + "<a:blip r:embed=\"" + relId + "\"/>"
            + "<a:stretch><a:fillRect/></a:stretch>"
            + "</pic:blipFill>"
            + "<pic:spPr>"
            + "<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + emuW + "\" cy=\"" + emuH + "\"/></a:xfrm>"
            + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>"
            + "</pic:spPr>"
            + "</pic:pic>"
            + "</a:graphicData>"
            + "</a:graphic>"
            + "</wp:inline>"
            + "</w:drawing>";

        var drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing();
        drawing.InnerXml = drawingXml;
        return new DocumentFormat.OpenXml.Wordprocessing.Run(drawing);
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Run RunVersWml(System.Windows.Documents.Run src)
    {
        var rPr = new DocumentFormat.OpenXml.Wordprocessing.RunProperties();
        if (src.FontWeight == FontWeights.Bold) rPr.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Bold());
        if (src.FontStyle == FontStyles.Italic) rPr.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Italic());
        if (src.TextDecorations == TextDecorations.Underline)
            rPr.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Underline
            {
                Val = DocumentFormat.OpenXml.Wordprocessing.UnderlineValues.Single
            });

        var text = src.Text ?? "";
        var r = new DocumentFormat.OpenXml.Wordprocessing.Run();
        if (rPr.HasChildren) r.AppendChild(rPr);
        r.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(text)
        {
            Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
        });
        return r;
    }

    private static bool EstTitre1(System.Windows.Documents.Paragraph p) => p.FontSize > 20 || (p.FontWeight == FontWeights.Bold && p.FontSize >= 16);
    private static bool EstTitre2(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && p.FontSize > 16;
    private static bool EstTitre3(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && !EstTitre2(p) && p.FontSize > 13;
    private static bool EstListePucese(System.Windows.Documents.Paragraph p) => ACommePrefixe(p, "* ") || ACommePrefixe(p, "- ");
    private static bool EstListeNum(System.Windows.Documents.Paragraph p) => System.Text.RegularExpressions.Regex.IsMatch(Texte(p), @"^\d+\.\s");

    private static bool ACommePrefixe(System.Windows.Documents.Paragraph p, string prefixe)
        => Texte(p).StartsWith(prefixe, StringComparison.Ordinal);

    private static string Texte(System.Windows.Documents.Paragraph p)
        => new TextRange(p.ContentStart, p.ContentEnd).Text;

    // ============== Reader ==============

    private static System.Windows.Documents.Paragraph ParagrapheWmlVersFlow(DocumentFormat.OpenXml.Wordprocessing.Paragraph src, MainDocumentPart mainPart)
    {
        var flowPara = new System.Windows.Documents.Paragraph();
        var pPr = src.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties>();
        var style = pPr?.ParagraphStyleId?.Val?.Value;

        if (style is "Heading1" or "Heading2" or "Heading3")
        {
            flowPara.FontSize = style switch { "Heading1" => 24.0, "Heading2" => 18.0, _ => 14.0 };
            if (style == "Heading1") flowPara.FontWeight = FontWeights.Bold;
        }

        foreach (var child in src.ChildElements)
        {
            switch (child)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Run r:
                    // Phase F : un Run peut contenir un Drawing (image inline).
                    var drawing = r.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Drawing>();
                    if (drawing is not null && mainPart is not null)
                    {
                        var img = TryExtraireImage(drawing, mainPart);
                        if (img is not null)
                        {
                            flowPara.Inlines.Add(img);
                            break;
                        }
                    }
                    flowPara.Inlines.Add(RunWmlVersFlow(r));
                    break;
                case DocumentFormat.OpenXml.Wordprocessing.Hyperlink link:
                    foreach (var innerRun in link.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>())
                        flowPara.Inlines.Add(RunWmlVersFlow(innerRun));
                    break;
                // BookmarkStart/End, ProofErr, etc. : ignores.
            }
        }
        return flowPara;
    }

    /// <summary>Essaie d'extraire l'image d'un w:drawing/wp:inline et de l'inserer
    /// dans le FlowDocument. Retourne null si pas une image (texte brut, etc.).</summary>
    private static Inline? TryExtraireImage(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing, MainDocumentPart mainPart)
    {
        try
        {
            // Le schema DrawingML stocke le rId du Blip dans <a:blip r:embed="rIdN"/>.
            // On cherche l'element Blip n'importe ou dans le dessin.
            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip is null) return null;
            var relId = blip.Embed?.Value;
            if (string.IsNullOrEmpty(relId)) return null;

            // Resoudre l'ImagePart via le relationship ID du mainPart.
            var part = (ImagePart)mainPart.GetPartById(relId);
            if (part is null) return null;

            // Lire les bytes et les sauver dans le cache LocalAppData (evite de garder le .docx ouvert).
            byte[] bytes;
            using (var s = part.GetStream())
            {
                using var ms = new System.IO.MemoryStream();
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var cachedPath = ImageHelper.SauverDansCache(bytes);
            if (string.IsNullOrEmpty(cachedPath)) return null;

            // Creer l'Image WPF et l'InlineUIContainer.
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new System.Uri(cachedPath, System.UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            var img = new System.Windows.Controls.Image { Source = bmp, MaxWidth = ImageHelper.DefaultMaxDim };
            return new InlineUIContainer(img);
        }
        catch { return null; }
    }

    private static System.Windows.Documents.Run RunWmlVersFlow(DocumentFormat.OpenXml.Wordprocessing.Run src)
    {
        var rPr = src.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.RunProperties>();
        var run = new System.Windows.Documents.Run();

        var textElement = src.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Text>();
        if (textElement is not null) run.Text = textElement.Text;

        if (rPr is not null)
        {
            if (rPr.Bold is not null) run.FontWeight = FontWeights.Bold;
            if (rPr.Italic is not null) run.FontStyle = FontStyles.Italic;
            if (rPr.Underline is not null) run.TextDecorations = TextDecorations.Underline;
        }
        return run;
    }
}
