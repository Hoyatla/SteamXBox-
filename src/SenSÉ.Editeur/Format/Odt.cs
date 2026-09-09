using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Xml;
using System.Xml.Linq;

namespace SenSÉ.Editeur.Format;

/// <summary>
/// Writer/reader natif de fichiers .odt (OpenDocument Text, OASIS) sans dependance
/// externe. .odt est un ZIP OPC-like : <c>mimetype</c> (non compresse),
/// <c>META-INF/manifest.xml</c>, <c>content.xml</c>. On parle directement le
/// format (ZIP+XML selon ECMA-376 / OASIS OpenDocument 1.2).
/// </summary>
/// <remarks>
/// Capacites minimales : paragraphes, gras/italique/souligne (via text:span
/// + text:span avec style bold/italic/underline), H1/H2/H3 (text:h avec
/// text:outline-level), listes a puces/numerotees (prefixees en texte).
/// Pas d'images, pas de tableaux, pas de styles auto-generes. Pour aller
/// au-dela, etendre BuildContentXml et ParseContentXml.
/// </remarks>
public static class Odt
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs    = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string ManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>Sauvegarde un FlowDocument en .odt (OpenDocument Text).</summary>
    public static void VersOdt(FlowDocument doc, string chemin)
    {
        if (File.Exists(chemin)) File.Delete(chemin);

        using var zip = ZipFile.Open(chemin, ZipArchiveMode.Create);

        // 1) mimetype DOIT etre non compresse et premier fichier (contrainte ODF).
        var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimeEntry.Open(), new UTF8Encoding(false)))
            w.Write("application/vnd.oasis.opendocument.text");

        // 2) META-INF/manifest.xml
        var manifestEntry = zip.CreateEntry("META-INF/manifest.xml", CompressionLevel.Optimal);
        using (var w = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
            w.Write(BuildManifestXml());

        // 3) content.xml : le document lui-meme.
        var contentEntry = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
        using (var w = new StreamWriter(contentEntry.Open(), new UTF8Encoding(false)))
            w.Write(BuildContentXml(doc));
    }

    /// <summary>Charge un .odt dans un FlowDocument existant (vide prealablement).</summary>
    public static void DepuisOdt(FlowDocument cible, string chemin)
    {
        cible.Blocks.Clear();
        using var zip = ZipFile.OpenRead(chemin);
        var contentEntry = zip.GetEntry("content.xml")
            ?? throw new InvalidDataException("ODT invalide : content.xml absent.");
        XDocument doc;
        using (var s = contentEntry.Open())
        {
            doc = XDocument.Load(s);
        }
        ParseContentXml(doc, cible);
    }

    private static string BuildManifestXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<manifest:manifest xmlns:manifest=\"" + ManifestNs + "\" manifest:version=\"1.2\">" +
            "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
            "<manifest:file-entry manifest:full-path=\"/content.xml\" manifest:media-type=\"text/xml\"/>" +
            "</manifest:manifest>";
    }

    private static string BuildContentXml(FlowDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<office:document-content xmlns:office=\"").Append(OfficeNs).Append("\"")
          .Append(" xmlns:text=\"").Append(TextNs).Append("\"")
          .Append(" office:version=\"1.2\">");
        sb.Append("<office:body><office:text>");

        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case System.Windows.Documents.Paragraph p when EstTitre1(p):
                    AppendH(sb, 1, TexteRunsAvecSpans(p.Inlines));
                    break;
                case System.Windows.Documents.Paragraph p when EstTitre2(p):
                    AppendH(sb, 2, TexteRunsAvecSpans(p.Inlines));
                    break;
                case System.Windows.Documents.Paragraph p when EstTitre3(p):
                    AppendH(sb, 3, TexteRunsAvecSpans(p.Inlines));
                    break;
                case System.Windows.Documents.Paragraph p when EstListePucese(p):
                    sb.Append("<text:p>• ").Append(TexteRunsAvecSpans(p.Inlines)).Append("</text:p>");
                    break;
                case System.Windows.Documents.Paragraph p when EstListeNum(p):
                    sb.Append("<text:p>1. ").Append(TexteRunsAvecSpans(p.Inlines)).Append("</text:p>");
                    break;
                case System.Windows.Documents.Paragraph p:
                    sb.Append("<text:p>").Append(TexteRunsAvecSpans(p.Inlines)).Append("</text:p>");
                    break;
            }
        }

        sb.Append("</office:text></office:body></office:document-content>");
        return sb.ToString();
    }

    private static void AppendH(StringBuilder sb, int niveau, string contenu)
    {
        // s est juste un placeholder pour la signature ; non utilise.
        sb.Append("<text:h text:outline-level=\"").Append(niveau).Append("\">");
        sb.Append(contenu);
        sb.Append("</text:h>");
    }

    private static string TexteRunsAvecSpans(InlineCollection inlines)
    {
        var sb = new StringBuilder();
        if (inlines.Count == 0) return "";
        foreach (var inline in inlines)
        {
            if (inline is System.Windows.Documents.Run run)
            {
                var text = System.Net.WebUtility.HtmlEncode(run.Text ?? "");
                var bold = run.FontWeight == FontWeights.Bold;
                var italic = run.FontStyle == FontStyles.Italic;
                var underline = run.TextDecorations == TextDecorations.Underline;
                if (bold) sb.Append("<text:span text:style-name=\"T1\">");
                if (italic) sb.Append("<text:span text:style-name=\"T2\">");
                if (underline) sb.Append("<text:span text:style-name=\"T3\">");
                sb.Append(text);
                if (underline) sb.Append("</text:span>");
                if (italic) sb.Append("</text:span>");
                if (bold) sb.Append("</text:span>");
            }
            else if (inline is LineBreak)
            {
                sb.Append("<text:line-break/>");
            }
            // InlineUIContainer, Hyperlink : ignores en Phase C-prime.
        }
        return sb.ToString();
    }

    private static bool EstTitre1(System.Windows.Documents.Paragraph p) => p.FontSize > 20 || (p.FontWeight == FontWeights.Bold && p.FontSize >= 16);
    private static bool EstTitre2(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && p.FontSize > 16;
    private static bool EstTitre3(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && !EstTitre2(p) && p.FontSize > 13;
    private static bool EstListePucese(System.Windows.Documents.Paragraph p) => Texte(p).StartsWith("• ") || Texte(p).StartsWith("- ");
    private static bool EstListeNum(System.Windows.Documents.Paragraph p) => System.Text.RegularExpressions.Regex.IsMatch(Texte(p), @"^\d+\.\s");

    private static string Texte(System.Windows.Documents.Paragraph p)
        => new TextRange(p.ContentStart, p.ContentEnd).Text;

    // ============== Reader ==============

    private static void ParseContentXml(XDocument xdoc, FlowDocument cible)
    {
        var root = xdoc.Root;
        if (root is null) return;
        var body = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "body" && e.Name.NamespaceName == OfficeNs);
        if (body is null) return;
        var textContainer = body.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "text" && e.Name.NamespaceName == OfficeNs);
        if (textContainer is null) return;

        foreach (var elem in textContainer.Elements())
        {
            if (elem.Name.NamespaceName != TextNs) continue;
            switch (elem.Name.LocalName)
            {
                case "h":
                    cible.Blocks.Add(HWmlVersFlow(elem));
                    break;
                case "p":
                    cible.Blocks.Add(PWmlVersFlow(elem));
                    break;
                case "list":
                    // Phase C-prime : on aplatit en paragraphes (vraies <text:list-item> en Phase D).
                    foreach (var li in elem.Descendants().Where(e => e.Name.LocalName == "li"))
                        cible.Blocks.Add(LiWmlVersFlow(li));
                    break;
            }
        }
    }

    private static System.Windows.Documents.Paragraph HWmlVersFlow(XElement h)
    {
        var p = new System.Windows.Documents.Paragraph();
        var niveau = (int?)h.Attribute(XName.Get("outline-level", TextNs)) ?? 1;
        p.FontSize = niveau switch { 1 => 24.0, 2 => 18.0, _ => 14.0 };
        if (niveau == 1) p.FontWeight = FontWeights.Bold;
        foreach (var span in h.Descendants().Where(e => e.Name.LocalName == "span"))
            p.Inlines.Add(SpanWmlVersFlow(span));
        // Si pas de span, prendre le texte direct.
        if (!p.Inlines.Any())
            p.Inlines.Add(new System.Windows.Documents.Run(DecodeText(h.Value)));
        return p;
    }

    private static System.Windows.Documents.Paragraph PWmlVersFlow(XElement p)
    {
        var flow = new System.Windows.Documents.Paragraph();
        foreach (var child in p.Nodes())
        {
            if (child is XElement span && span.Name.LocalName == "span" && span.Name.NamespaceName == TextNs)
                flow.Inlines.Add(SpanWmlVersFlow(span));
            else if (child is XText t)
                flow.Inlines.Add(new System.Windows.Documents.Run(DecodeText(t.Value)));
        }
        return flow;
    }

    private static System.Windows.Documents.Paragraph LiWmlVersFlow(XElement li)
    {
        var flow = new System.Windows.Documents.Paragraph();
        flow.Inlines.Add(new System.Windows.Documents.Run("• "));
        foreach (var child in li.Nodes())
        {
            if (child is XElement span && span.Name.LocalName == "span" && span.Name.NamespaceName == TextNs)
                flow.Inlines.Add(SpanWmlVersFlow(span));
            else if (child is XElement p && p.Name.LocalName == "p" && p.Name.NamespaceName == TextNs)
                foreach (var sub in p.Elements().Where(e => e.Name.LocalName == "span"))
                    flow.Inlines.Add(SpanWmlVersFlow(sub));
            else if (child is XText t)
                flow.Inlines.Add(new System.Windows.Documents.Run(DecodeText(t.Value)));
        }
        return flow;
    }

    private static System.Windows.Documents.Run SpanWmlVersFlow(XElement span)
    {
        var run = new System.Windows.Documents.Run();
        var style = (string?)span.Attribute(XName.Get("style-name", TextNs)) ?? "";
        if (style == "T1") run.FontWeight = FontWeights.Bold;
        if (style == "T2") run.FontStyle = FontStyles.Italic;
        if (style == "T3") run.TextDecorations = TextDecorations.Underline;
        run.Text = DecodeText(span.Value);
        return run;
    }

    private static string DecodeText(string raw)
        => System.Net.WebUtility.HtmlDecode(raw ?? "");
}
