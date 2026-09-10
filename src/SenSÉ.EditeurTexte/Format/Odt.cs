using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace SenSÉ.EditeurTexte.Format;

/// <summary>
/// Writer/reader natif de fichiers .odt (OpenDocument Text, OASIS) sans dependance
/// externe. .odt est un ZIP OPC-like : <c>mimetype</c> (non compresse),
/// <c>META-INF/manifest.xml</c>, <c>content.xml</c>, <c>Pictures/</c> pour les
/// images. On parle directement le format (ZIP+XML selon OASIS OpenDocument 1.2).
/// </summary>
/// <remarks>
/// Capacites : paragraphes, gras/italique/souligne (via text:span),
/// H1/H2/H3 (text:h avec text:outline-level), listes a puces/numerotees,
/// images (draw:image dans text:p, dedup par chemin source, extraction en
/// cache LocalAppData puis relecture UriSource).
/// </remarks>
public static class Odt
{
    private const string OfficeNs   = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs      = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string ManifestNs  = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string DrawNs      = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private const string XlinkNs     = "http://www.w3.org/1999/xlink";
    private const string SvgNs       = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    /// <summary>Largeur d'affichage par defaut d'une image (cm). Le ratio est preserve.</summary>
    private const double ImageLargeurCm = 12.0;
    /// <summary>Hauteur max d'affichage d'une image (cm).</summary>
    private const double ImageHauteurCmMax = 18.0;

    /// <summary>Sauvegarde un FlowDocument en .odt (OpenDocument Text).</summary>
    public static void VersOdt(FlowDocument doc, string chemin)
    {
        if (File.Exists(chemin)) File.Delete(chemin);

        using var zip = ZipFile.Open(chemin, ZipArchiveMode.Create);

        // 1) mimetype DOIT etre non compresse et premier fichier (contrainte ODF).
        var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimeEntry.Open(), new UTF8Encoding(false)))
            w.Write("application/vnd.oasis.opendocument.text");

        // 2) Extraire toutes les images du document, dedup par chemin source.
        // images : chemin source -> bytes PNG (apres compression 1600px).
        // nomsImages : chemin source -> nom de fichier dans Pictures/ (image1.png, image2.png, ...).
        var images = new Dictionary<string, byte[]>();
        var nomsImages = new Dictionary<string, string>();
        ExtraireImagesDuFlowDocument(doc, images, nomsImages);

        // 3) Ecrire les images dans le ZIP Pictures/.
        foreach (var kv in images)
        {
            var entry = zip.CreateEntry("Pictures/" + nomsImages[kv.Key], CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(kv.Value, 0, kv.Value.Length);
        }

        // 4) META-INF/manifest.xml (declare les images pour ODF).
        var manifestEntry = zip.CreateEntry("META-INF/manifest.xml", CompressionLevel.Optimal);
        using (var w = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
            w.Write(BuildManifestXml(nomsImages));

        // 5) content.xml : le document lui-meme (avec draw:image pour les images).
        var contentEntry = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
        using (var w = new StreamWriter(contentEntry.Open(), new UTF8Encoding(false)))
            w.Write(BuildContentXml(doc, nomsImages));
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
        ParseContentXml(doc, cible, zip);
    }

    private static string BuildManifestXml(Dictionary<string, string> nomsImages)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<manifest:manifest xmlns:manifest=\"").Append(ManifestNs).Append("\" manifest:version=\"1.2\">");
        sb.Append("<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>");
        sb.Append("<manifest:file-entry manifest:full-path=\"/content.xml\" manifest:media-type=\"text/xml\"/>");
        foreach (var kv in nomsImages)
        {
            sb.Append("<manifest:file-entry manifest:full-path=\"/Pictures/").Append(kv.Value)
              .Append("\" manifest:media-type=\"image/png\"/>");
        }
        sb.Append("</manifest:manifest>");
        return sb.ToString();
    }

    private static string BuildContentXml(FlowDocument doc, Dictionary<string, string> nomsImages)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<office:document-content xmlns:office=\"").Append(OfficeNs).Append("\"")
          .Append(" xmlns:text=\"").Append(TextNs).Append("\"")
          .Append(" xmlns:draw=\"").Append(DrawNs).Append("\"")
          .Append(" xmlns:xlink=\"").Append(XlinkNs).Append("\"")
          .Append(" xmlns:svg=\"").Append(SvgNs).Append("\"")
          .Append(" office:version=\"1.2\">");
        sb.Append("<office:body><office:text>");

        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case System.Windows.Documents.Paragraph p when EstTitre1(p):
                    AppendH(sb, 1, TexteRunsAvecImages(p.Inlines, nomsImages));
                    break;
                case System.Windows.Documents.Paragraph p when EstTitre2(p):
                    AppendH(sb, 2, TexteRunsAvecImages(p.Inlines, nomsImages));
                    break;
                case System.Windows.Documents.Paragraph p when EstTitre3(p):
                    AppendH(sb, 3, TexteRunsAvecImages(p.Inlines, nomsImages));
                    break;
                case System.Windows.Documents.Paragraph p when EstListePucese(p):
                    sb.Append("<text:p>\u2022 ").Append(TexteRunsAvecImages(p.Inlines, nomsImages)).Append("</text:p>");
                    break;
                case System.Windows.Documents.Paragraph p when EstListeNum(p):
                    sb.Append("<text:p>1. ").Append(TexteRunsAvecImages(p.Inlines, nomsImages)).Append("</text:p>");
                    break;
                case System.Windows.Documents.Paragraph p:
                    sb.Append("<text:p>").Append(TexteRunsAvecImages(p.Inlines, nomsImages)).Append("</text:p>");
                    break;
            }
        }

        sb.Append("</office:text></office:body></office:document-content>");
        return sb.ToString();
    }

    private static void AppendH(StringBuilder sb, int niveau, string contenu)
    {
        sb.Append("<text:h text:outline-level=\"").Append(niveau).Append("\">");
        sb.Append(contenu);
        sb.Append("</text:h>");
    }

    // ============== Extraction des images depuis le FlowDocument ==============

    /// <summary>
    /// Parcourt tous les paragraphes du FlowDocument, extrait les images (dedup par
    /// chemin source) et leur assigne un nom unique dans Pictures/.
    /// </summary>
    private static void ExtraireImagesDuFlowDocument(FlowDocument doc,
        Dictionary<string, byte[]> images, Dictionary<string, string> nomsImages)
    {
        var compteurs = new Dictionary<string, int>();
        foreach (var block in doc.Blocks)
        {
            if (block is System.Windows.Documents.Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    CollecterImage(inline, images, nomsImages, compteurs);
                }
            }
        }
    }

    private static void CollecterImage(Inline inline,
        Dictionary<string, byte[]> images, Dictionary<string, string> nomsImages,
        Dictionary<string, int> compteurs)
    {
        if (inline is not InlineUIContainer uic) return;
        var src = ImageHelper.ResoudreCheminSource(uic);
        if (src is null) return;
        if (images.ContainsKey(src)) return; // dedup
        var bytes = ImageHelper.ChargerImage(src, ImageHelper.DefaultMaxDim);
        if (bytes is null || bytes.Length == 0) return;
        images[src] = bytes;
        nomsImages[src] = NomImagePourInline(src, compteurs);
    }

    private static string NomImagePourInline(string cheminSource, Dictionary<string, int> compteurs)
    {
        compteurs.TryGetValue(cheminSource, out var n);
        n++;
        compteurs[cheminSource] = n;
        return "image" + n + ".png";
    }

    // ============== Writer : inlines -> XML ODT ==============

    /// <summary>
    /// Serialise les inlines d'un paragraphe en texte ODT. Gere les Run (avec spans
    /// gras/italique/souligne), les LineBreak, et les InlineUIContainer(Image) via
    /// &lt;draw:image&gt; avec xlink:href vers Pictures/imageN.png.
    /// </summary>
    private static string TexteRunsAvecImages(InlineCollection inlines, Dictionary<string, string> nomsImages)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is System.Windows.Documents.Run run)
            {
                sb.Append(TexteRunAvecSpans(run));
            }
            else if (inline is LineBreak)
            {
                sb.Append("<text:line-break/>");
            }
            else if (inline is InlineUIContainer uic2 && uic2.Child is System.Windows.Controls.Image img)
            {
                var src = ImageHelper.ResoudreCheminSource(uic2);
                if (src is null || !nomsImages.TryGetValue(src, out var nomImage)) continue;
                var (w, h) = DimensionsImage(img);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                sb.Append("<draw:image xlink:href=\"Pictures/").Append(nomImage)
                  .Append("\" xlink:type=\"simple\" draw:mime-type=\"image/png\"")
                  .Append(" svg:width=\"").Append(w.ToString("F2", ci)).Append("cm\"")
                  .Append(" svg:height=\"").Append(h.ToString("F2", ci)).Append("cm\"")
                  .Append(" svg:x=\"0cm\" svg:y=\"0cm\"/>");
            }
        }
        return sb.ToString();
    }

    private static string TexteRunAvecSpans(System.Windows.Documents.Run run)
    {
        var text = System.Net.WebUtility.HtmlEncode(run.Text ?? "");
        var bold = run.FontWeight == FontWeights.Bold;
        var italic = run.FontStyle == FontStyles.Italic;
        var underline = run.TextDecorations == TextDecorations.Underline;
        var sb = new StringBuilder();
        if (bold) sb.Append("<text:span text:style-name=\"T1\">");
        if (italic) sb.Append("<text:span text:style-name=\"T2\">");
        if (underline) sb.Append("<text:span text:style-name=\"T3\">");
        sb.Append(text);
        if (underline) sb.Append("</text:span>");
        if (italic) sb.Append("</text:span>");
        if (bold) sb.Append("</text:span>");
        return sb.ToString();
    }

    /// <summary>Calcule les dimensions d'affichage en cm, en preservant le ratio source.</summary>
    private static (double largeurCm, double hauteurCm) DimensionsImage(System.Windows.Controls.Image img)
    {
        double w = 800, h = 600;
        if (img.Source is BitmapSource bs && bs.PixelWidth > 0 && bs.PixelHeight > 0)
        {
            w = bs.PixelWidth;
            h = bs.PixelHeight;
        }
        double ratio = w / h;
        double largeurCm = ImageLargeurCm;
        double hauteurCm = largeurCm / ratio;
        if (hauteurCm > ImageHauteurCmMax)
        {
            hauteurCm = ImageHauteurCmMax;
            largeurCm = hauteurCm * ratio;
        }
        return (largeurCm, hauteurCm);
    }

    // ============== Detection titres / listes ==============

    private static bool EstTitre1(System.Windows.Documents.Paragraph p) => p.FontSize > 20 || (p.FontWeight == FontWeights.Bold && p.FontSize >= 16);
    private static bool EstTitre2(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && p.FontSize > 16;
    private static bool EstTitre3(System.Windows.Documents.Paragraph p) => !EstTitre1(p) && !EstTitre2(p) && p.FontSize > 13;
    private static bool EstListePucese(System.Windows.Documents.Paragraph p) => Texte(p).StartsWith("\u2022 ") || Texte(p).StartsWith("- ");
    private static bool EstListeNum(System.Windows.Documents.Paragraph p) => System.Text.RegularExpressions.Regex.IsMatch(Texte(p), @"^\d+\.\s");

    private static string Texte(System.Windows.Documents.Paragraph p)
        => new TextRange(p.ContentStart, p.ContentEnd).Text;

    // ============== Reader ==============

    private static void ParseContentXml(XDocument xdoc, FlowDocument cible, ZipArchive zip)
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
                    cible.Blocks.Add(HWmlVersFlow(elem, zip));
                    break;
                case "p":
                    cible.Blocks.Add(PWmlVersFlow(elem, zip));
                    break;
                case "list":
                    // Phase C-prime : on aplatit en paragraphes (vraies <text:list-item> en Phase D).
                    foreach (var li in elem.Descendants().Where(e => e.Name.LocalName == "li"))
                        cible.Blocks.Add(LiWmlVersFlow(li, zip));
                    break;
            }
        }
    }

    private static System.Windows.Documents.Paragraph HWmlVersFlow(XElement h, ZipArchive zip)
    {
        var p = new System.Windows.Documents.Paragraph();
        var niveau = (int?)h.Attribute(XName.Get("outline-level", TextNs)) ?? 1;
        p.FontSize = niveau switch { 1 => 24.0, 2 => 18.0, _ => 14.0 };
        if (niveau == 1) p.FontWeight = FontWeights.Bold;
        foreach (var node in h.Nodes())
        {
            if (node is XElement span && span.Name.LocalName == "span" && span.Name.NamespaceName == TextNs)
                p.Inlines.Add(SpanWmlVersFlow(span));
            else if (node is XElement img && img.Name.LocalName == "image" && img.Name.NamespaceName == DrawNs)
                InsererImageOdt(img, p, zip);
            else if (node is XText t)
                p.Inlines.Add(new System.Windows.Documents.Run(DecodeText(t.Value)));
        }
        if (!p.Inlines.Any())
            p.Inlines.Add(new System.Windows.Documents.Run(DecodeText(h.Value)));
        return p;
    }

    private static System.Windows.Documents.Paragraph PWmlVersFlow(XElement p, ZipArchive zip)
    {
        var flow = new System.Windows.Documents.Paragraph();
        foreach (var child in p.Nodes())
        {
            if (child is XElement span && span.Name.LocalName == "span" && span.Name.NamespaceName == TextNs)
                flow.Inlines.Add(SpanWmlVersFlow(span));
            else if (child is XElement img && img.Name.LocalName == "image" && img.Name.NamespaceName == DrawNs)
                InsererImageOdt(img, flow, zip);
            else if (child is XText t)
                flow.Inlines.Add(new System.Windows.Documents.Run(DecodeText(t.Value)));
        }
        return flow;
    }

    private static System.Windows.Documents.Paragraph LiWmlVersFlow(XElement li, ZipArchive zip)
    {
        var flow = new System.Windows.Documents.Paragraph();
        flow.Inlines.Add(new System.Windows.Documents.Run("\u2022 "));
        foreach (var child in li.Nodes())
        {
            if (child is XElement p && p.Name.LocalName == "p" && p.Name.NamespaceName == TextNs)
            {
                foreach (var sub in p.Nodes())
                {
                    if (sub is XElement span && span.Name.LocalName == "span" && span.Name.NamespaceName == TextNs)
                        flow.Inlines.Add(SpanWmlVersFlow(span));
                    else if (sub is XElement img && img.Name.LocalName == "image" && img.Name.NamespaceName == DrawNs)
                        InsererImageOdt(img, flow, zip);
                    else if (sub is XText t)
                        flow.Inlines.Add(new System.Windows.Documents.Run(DecodeText(t.Value)));
                }
            }
            else if (child is XElement img && img.Name.LocalName == "image" && img.Name.NamespaceName == DrawNs)
                InsererImageOdt(img, flow, zip);
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

    /// <summary>
    /// Extrait l'image d'un &lt;draw:image&gt; du ZIP, la sauve dans le cache
    /// LocalAppData, et insere un InlineUIContainer(Image) dans le paragraphe.
    /// </summary>
    private static void InsererImageOdt(XElement image, System.Windows.Documents.Paragraph flow, ZipArchive zip)
    {
        var href = (string?)image.Attribute(XName.Get("href", XlinkNs));
        if (string.IsNullOrEmpty(href)) return;
        // href est relatif au package, ex: "Pictures/image1.png".
        var entry = zip.GetEntry(href);
        if (entry is null) return;
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var s = entry.Open()) s.CopyTo(ms);
            bytes = ms.ToArray();
        }
        var cheminCache = ImageHelper.SauverDansCache(bytes);
        if (string.IsNullOrEmpty(cheminCache)) return;
        var img = new System.Windows.Controls.Image
        {
            Source = new BitmapImage(new Uri(cheminCache, UriKind.Absolute)),
        };
        var uic = new InlineUIContainer(img, flow.ContentEnd);
    }

    private static string DecodeText(string raw)
        => System.Net.WebUtility.HtmlDecode(raw ?? "");
}
