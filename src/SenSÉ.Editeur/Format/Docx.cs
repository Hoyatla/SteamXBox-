using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SenSÉ.Editeur.Format;

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

        foreach (var block in doc.Blocks)
        {
            var element = BlockVersWml(block);
            if (element is not null) body.AppendChild(element);
        }

        mainPart.Document.Save();
    }

    /// <summary>Charge un .docx dans un FlowDocument existant (vide prealablement).</summary>
    public static void DepuisDocx(FlowDocument cible, string chemin)
    {
        cible.Blocks.Clear();
        using var pkg = WordprocessingDocument.Open(chemin, false);
        var body = pkg.MainDocumentPart!.Document.Body!;
        foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            cible.Blocks.Add(ParagrapheWmlVersFlow(para));
        }
    }

    // ============== Writer ==============

    private static OpenXmlElement? BlockVersWml(System.Windows.Documents.Block block) => block switch
    {
        System.Windows.Documents.Paragraph p when EstTitre1(p) => TitreWml("Heading1", RunsVersWml(p.Inlines)),
        System.Windows.Documents.Paragraph p when EstTitre2(p) => TitreWml("Heading2", RunsVersWml(p.Inlines)),
        System.Windows.Documents.Paragraph p when EstTitre3(p) => TitreWml("Heading3", RunsVersWml(p.Inlines)),
        System.Windows.Documents.Paragraph p when EstListePucese(p) => ListeWml(p, numbered: false),
        System.Windows.Documents.Paragraph p when EstListeNum(p)   => ListeWml(p, numbered: true),
        System.Windows.Documents.Paragraph p => ParagrapheWml(RunsVersWml(p.Inlines)),
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

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph ListeWml(System.Windows.Documents.Paragraph src, bool numbered)
    {
        // Phase B-prime : prefixe texte (les vrais w:num demandent un
        // NumberingDefinitionsPart, qu'on ajoutera en Phase D si besoin).
        var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
        var prefixe = numbered ? "1. " : "* ";
        p.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
            new DocumentFormat.OpenXml.Wordprocessing.Text(prefixe) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        foreach (var r in RunsVersWml(src.Inlines)) p.AppendChild(r);
        return p;
    }

    private static System.Collections.Generic.IEnumerable<OpenXmlElement> RunsVersWml(InlineCollection inlines)
    {
        if (inlines.Count == 0) { yield return new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("")); yield break; }
        foreach (var inline in inlines)
        {
            if (inline is System.Windows.Documents.Run run) { yield return RunVersWml(run); }
            else if (inline is LineBreak) { yield return new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Break()); }
            // InlineUIContainer (images), Hyperlink, etc. : ignores en Phase B-prime.
        }
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

    private static System.Windows.Documents.Paragraph ParagrapheWmlVersFlow(DocumentFormat.OpenXml.Wordprocessing.Paragraph src)
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
