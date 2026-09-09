using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SenSÉ.Tools.Documents;

/// <summary>Ce qu'une rédaction a produit, ou pourquoi elle n'a rien produit.</summary>
/// <param name="Chemin">Le fichier écrit, ou vide.</param>
/// <param name="Probleme">Vide si tout va bien ; sinon ce qui a manqué.</param>
public readonly record struct Redige(string Chemin, string Probleme)
{
    public bool Reussi => Chemin.Length > 0;
}

/// <summary>
/// Écrit un document sans ouvrir de fenêtre.
/// </summary>
/// <remarks>
/// <b>Écrit après avoir vu l'assistant ouvrir quatre fenêtres et abandonner.</b> Session du
/// 9 septembre 2026 : à « écris trois paragraphes et enregistre-les en Word », il a lancé
/// l'Éditeur Texte, tenté <c>regler_option</c>, reçu « le panneau n'est pas ouvert », rouvert
/// l'outil, retenté, noté un carnet, coché une étape qu'il n'avait pas faite, rempli son contexte
/// et rendu la main. Quatre fenêtres à l'écran, zéro document sur le disque.
///
/// <para>
/// <b>La cause n'est pas le modèle, c'est le chemin.</b> Écrire un document passait forcément par
/// une interface graphique : un éditeur WPF, un panneau, un champ. Or ce que la demande voulait
/// n'était pas une fenêtre — c'était un fichier. Ici, il n'y a pas de fenêtre du tout.
/// </para>
///
/// <para>
/// <b>Le .docx est écrit nativement</b>, par la bibliothèque Open XML que le produit référence
/// déjà, et non par une conversion. LibreOffice n'est pas installé sur cette machine : une route
/// qui en dépendrait ne rendrait rien du tout, et c'est exactement le genre de capacité qu'il vaut
/// mieux ne pas déclarer. Il reste employé pour le PDF, qui demande un moteur de rendu — et son
/// absence est alors dite, avec ce qui marche à la place.
/// </para>
/// </remarks>
public static class Redaction
{
    /// <summary>Où les documents sont écrits. Null pour l'emplacement du produit.</summary>
    public static string? Racine { get; set; }

    private static string Dossier
        => Path.Combine(Racine ?? AppContext.BaseDirectory, "Travaux", "Documents");

    /// <summary>Ce qui s'écrit sans rien d'autre que le produit.</summary>
    public static readonly IReadOnlyList<string> Natifs = ["docx", "html", "md", "txt"];

    /// <summary>Ce qui demande LibreOffice.</summary>
    public static readonly IReadOnlyList<string> Convertis = ["pdf", "odt", "rtf"];

    /// <summary>
    /// Écrit le texte dans un document du format demandé, et rend son chemin.
    /// </summary>
    /// <param name="titre">Le titre du document. Il ouvre le fichier et le nomme.</param>
    /// <param name="texte">Le corps. Une ligne vide sépare deux paragraphes.</param>
    /// <param name="format">docx, html, md, txt — ou pdf, odt, rtf si LibreOffice est là.</param>
    public static Redige Ecrire(
        string titre, string texte, string format, Action<string>? journal = null)
    {
        var quoi = (format ?? "").Trim().ToLowerInvariant();
        var intitule = (titre ?? "").Trim();
        var corps = (texte ?? "").Trim();

        if (corps.Length == 0)
        {
            return new Redige("", "Rien à écrire : le texte est vide.");
        }

        if (intitule.Length == 0)
        {
            intitule = "Document";
        }

        if (quoi.Length == 0)
        {
            quoi = "docx";
        }

        if (!Natifs.Contains(quoi) && !Convertis.Contains(quoi))
        {
            return new Redige("", $"Format inconnu : {quoi}. Connus : "
                + string.Join(", ", Natifs.Concat(Convertis)) + ".");
        }

        Directory.CreateDirectory(Dossier);

        var socle = Path.Combine(
            Dossier,
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)
            + "-" + Limace(intitule));

        try
        {
            switch (quoi)
            {
                case "docx":
                    return Fini(Docx(socle + ".docx", intitule, corps), journal);

                case "html":
                    return Fini(Poser(socle + ".html", Html(intitule, corps)), journal);

                case "md":
                    return Fini(Poser(socle + ".md", "# " + intitule + "\n\n" + corps + "\n"), journal);

                case "txt":
                    return Fini(Poser(socle + ".txt", intitule + "\n\n" + corps + "\n"), journal);

                default:
                    return Fini(Convertir(socle, intitule, corps, quoi, journal), journal);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Redige("", "Le document n'a pas pu être écrit : " + exception.Message);
        }
    }

    private static Redige Fini(Redige rendu, Action<string>? journal)
    {
        journal?.Invoke(rendu.Reussi
            ? $"document écrit : {rendu.Chemin}"
            : $"document non écrit : {rendu.Probleme}");

        return rendu;
    }

    private static Redige Poser(string chemin, string contenu)
    {
        File.WriteAllText(chemin, contenu, Encoding.UTF8);

        return new Redige(chemin, "");
    }

    /// <summary>
    /// Un .docx véritable, écrit par la bibliothèque Open XML.
    /// </summary>
    /// <remarks>
    /// <b>Nativement, et non par conversion.</b> LibreOffice n'est pas installé sur cette machine ;
    /// une route qui en dépendrait pour le format le plus demandé ne rendrait rien. La
    /// bibliothèque, elle, est déjà référencée par le produit — le document est donc écrit par le
    /// même format que Word emploie, et non par un intermédiaire qui pourrait manquer.
    /// </remarks>
    private static Redige Docx(string chemin, string titre, string corps)
    {
        using (var document = WordprocessingDocument.Create(chemin, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            var texte = new Body();

            texte.AppendChild(Paragraphe(titre, gras: true, taille: 32));

            foreach (var bloc in Paragraphes(corps))
            {
                texte.AppendChild(Paragraphe(bloc, gras: false, taille: 22));
            }

            partie.Document = new Document(texte);
            partie.Document.Save();
        }

        return new Redige(chemin, "");
    }

    /// <summary>Un paragraphe Word : ses lignes, sa graisse, sa taille.</summary>
    /// <remarks>
    /// Les tailles sont en demi-points — la convention d'Open XML, pas la nôtre : 32 fait 16 points
    /// pour le titre, 22 en fait 11 pour le corps.
    /// </remarks>
    private static Paragraph Paragraphe(string contenu, bool gras, int taille)
    {
        var courir = new Run();
        var mise = new RunProperties(new FontSize { Val = taille.ToString(CultureInfo.InvariantCulture) });

        if (gras)
        {
            mise.PrependChild(new Bold());
        }

        courir.AppendChild(mise);

        var lignes = contenu.Split('\n');

        for (var i = 0; i < lignes.Length; i++)
        {
            if (i > 0)
            {
                courir.AppendChild(new Break());
            }

            // Space.Preserve : sans lui, Word mange les espaces de tete et de fin, et une citation
            // indentee perd son retrait sans que rien ne le signale.
            courir.AppendChild(new Text(lignes[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return new Paragraph(courir);
    }

    /// <summary>Une ligne vide sépare deux paragraphes ; le reste tient ensemble.</summary>
    private static IEnumerable<string> Paragraphes(string corps)
        => corps.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim('\n', ' '))
            .Where(b => b.Length > 0);

    private static string Html(string titre, string corps)
    {
        var texte = new StringBuilder()
            .AppendLine("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\">")
            .Append("<title>").Append(Echapper(titre)).AppendLine("</title></head><body>")
            .Append("<h1>").Append(Echapper(titre)).AppendLine("</h1>");

        foreach (var bloc in Paragraphes(corps))
        {
            texte.Append("<p>").Append(Echapper(bloc).Replace("\n", "<br>")).AppendLine("</p>");
        }

        return texte.AppendLine("</body></html>").ToString();
    }

    /// <summary>Les formats qui demandent un moteur de rendu, quand il est là.</summary>
    /// <remarks>
    /// L'absence est dite avec ce qui marche à la place. « LibreOffice n'est pas installé » laisse
    /// le modèle sans issue ; « écris-le en docx » lui en donne une, et c'est presque toujours ce
    /// que l'utilisateur voulait.
    /// </remarks>
    private static Redige Convertir(
        string socle, string titre, string corps, string format, Action<string>? journal)
    {
        if (!LibreOffice.IsInstalled)
        {
            return new Redige("", $"Le format {format} demande LibreOffice, qui n'est pas installé "
                + "sur cette machine. Écris plutôt en docx, html, md ou txt — le docx est écrit "
                + "nativement et s'ouvre dans Word.");
        }

        var intermediaire = socle + ".html";

        File.WriteAllText(intermediaire, Html(titre, corps), Encoding.UTF8);

        var rendu = LibreOffice.Convert(
            intermediaire, format, Path.GetDirectoryName(socle)!, journal);

        return rendu.Worked
            ? new Redige(rendu.Produced, "")
            : new Redige("", rendu.Problem);
    }

    /// <summary>Un nom de fichier tenable, tiré d'un titre qui ne l'est pas.</summary>
    private static string Limace(string texte)
    {
        var limace = new StringBuilder();

        foreach (var lettre in texte.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(lettre) && lettre < 128)
            {
                limace.Append(lettre);
            }
            else if (limace.Length > 0 && limace[^1] != '-')
            {
                limace.Append('-');
            }

            if (limace.Length >= 40)
            {
                break;
            }
        }

        return limace.ToString().Trim('-') is { Length: > 0 } propre ? propre : "document";
    }

    private static string Echapper(string texte)
        => texte.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
