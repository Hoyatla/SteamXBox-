using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SteamXBox.Tools.Documents;

/// <summary>
/// Turns a PDF into an editable document, by taking its text rather than redrawing its pages.
/// </summary>
/// <remarks>
/// LibreOffice can convert a PDF, and the result is unusable. Measured on its own welcome guide:
/// a three-megabyte <c>.docx</c> whose <c>document.xml</c> is five megabytes and holds
/// <b>five thousand seven hundred shapes, three thousand nine hundred pictures and one thousand
/// four hundred drawings — for four hundred and forty-one paragraphs of text</b>. Every line of the
/// page had become a floating box, so Word laid out ten thousand objects for one turn of the mouse
/// wheel and took seconds to do it.
///
/// <para>
/// The reason is structural rather than a setting: LibreOffice opens a PDF in Draw, so its
/// conversion is a drawing of the page, not the page's text. No option changes that.
/// </para>
///
/// <para>
/// <b>The trade, stated plainly.</b> This keeps the words and loses the layout. That is what "pdf to
/// word" is usually wanted for — text somebody can edit — and a light document that can be worked
/// on beats a faithful one that cannot be scrolled. Anyone who needs the layout should keep the PDF.
/// </para>
/// </remarks>
public static class PdfToDocument
{
    /// <summary>Which targets this route can write.</summary>
    /// <remarks>
    /// Every prose format, from one reading, and none of them through LibreOffice. The proprietary
    /// one and the free one have to come out the same: a customer who chooses OpenDocument because
    /// it costs nothing should not be the one who gets the unusable document.
    /// </remarks>
    public static IReadOnlySet<string> Formats { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "docx", "odt", "rtf", "html", "txt",
        };

    /// <summary>Whether a conversion should take this route rather than LibreOffice.</summary>
    public static bool Handles(string input, string format)
        => Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           && Formats.Contains(format);

    /// <summary>What goes into the document, in reading order.</summary>
    /// <param name="Text">A paragraph, or empty when this is a picture.</param>
    /// <param name="Image">The picture's bytes, or null when this is text.</param>
    /// <param name="Jpeg">Whether those bytes are a JPEG rather than a PNG.</param>
    /// <param name="WidthPt">How wide the picture was on the page, in points.</param>
    /// <param name="HeightPt">How tall it was.</param>
    /// <remarks>
    /// Visible to the assembly because four writers consume it now — Word, OpenDocument, rich text
    /// and web. The reading is the hard part and is done once; what differs between those formats is
    /// only how the same paragraphs and pictures are spelled.
    /// </remarks>
    /// <param name="LeftPt">
    /// Distance du bord gauche de la page, en points. Zero quand la position n'est pas connue.
    /// </param>
    /// <param name="TopPt">
    /// Distance du HAUT de la page, en points — donc deja retournee. Un PDF compte du bas vers le
    /// haut, Word compte du haut vers le bas, et confondre les deux pose chaque image a la verticale
    /// opposee de la sienne. La conversion se fait une seule fois, dans <c>Pictures</c>, ou la
    /// hauteur de la page est connue.
    /// </param>
    internal readonly record struct Piece(
        string Text,
        byte[]? Image,
        bool Jpeg,
        double WidthPt,
        double HeightPt,
        double LeftPt = 0,
        double TopPt = 0,
        bool PageBreak = false,
        string FontName = "",
        double FontSize = 0)
    {
        public bool IsPicture => Image is not null;

        /// <summary>Une image dont on sait ou elle etait posee sur la page.</summary>
        public bool IsPlaced => IsPicture && (LeftPt > 0 || TopPt > 0);
    }

    /// <summary>Reads the PDF and writes the document beside it.</summary>
    public static LibreOffice.Result Convert(string input, string format, string outputDirectory)
    {
        try
        {
            // Le cache d'images appartient au document en cours. Garde d'une conversion a l'autre,
            // il ferait pointer le second .docx vers des relations du premier — un fichier que Word
            // refuse d'ouvrir, et dont la cause serait introuvable dans le document lui-meme.
            Deja.Clear();

            // Le compte aussi appartient au document en cours, et pour la meme raison. Il n'etait
            // remis a zero que par l'ecriture d'un .docx : une conversion vers txt, odt ou rtf
            // gardait donc celui du .docx precedent et le rendait comme le sien.
            Trace = "";
            Verdict = "";

            var pieces = Read(input);

            if (!pieces.Any(piece => piece.Text.Length > 0))
            {
                return new LibreOffice.Result(
                    "", "Ce PDF ne contient pas de texte : c'est une image, il faudrait le reconnaître d'abord.");
            }

            Directory.CreateDirectory(outputDirectory);

            var output = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(input) + "." + format.ToLowerInvariant());

            switch (format.ToLowerInvariant())
            {
                case "txt":
                    // A picture cannot go into a text file, and dropping it silently would leave a
                    // hole where the reader has no way of knowing something was removed.
                    File.WriteAllText(output, string.Join(
                        Environment.NewLine + Environment.NewLine,
                        pieces.Select(piece => piece.IsPicture ? "[image]" : piece.Text)));
                    break;

                case "odt":
                    OpenDocumentText.Write(output, pieces);
                    break;

                case "rtf":
                    PdfToRichText.Write(output, pieces);
                    break;

                case "html":
                    PdfToWeb.Write(output, Path.GetFileNameWithoutExtension(input), pieces);
                    break;

                default:
                    WriteDocx(output, pieces);
                    break;
            }

            // Le compte remonte avec le resultat : une conversion qui perd ses images doit le dire au
            // moment ou elle les perd, pas obliger a ouvrir le fichier produit pour s'en apercevoir.
            //
            // Dans le resultat, et non plus dans une propriete statique que l'appelant irait relire :
            // celle-ci survivait a la conversion qu'elle decrivait, et la suivante l'annoncait comme
            // la sienne. Le champ Problem reste vide — une conversion qui a produit un fichier n'a
            // pas de probleme, quoi qu'elle ait compte en chemin.
            return new LibreOffice.Result(output, "") { Trace = Bilan };
        }
        catch (Exception exception)
        {
            return new LibreOffice.Result("", exception.Message);
        }
    }

    /// <summary>
    /// The PDF's text, one entry per paragraph.
    /// </summary>
    /// <remarks>
    /// Read page by page with the layout-aware extractor, which puts the words back in reading
    /// order — a PDF stores them in the order they were drawn, which is not the order they are read
    /// in, and taking them raw gives a column of a two-column page interleaved with the other.
    ///
    /// <para>
    /// Lines are joined into paragraphs on the blank line, because a PDF has no paragraphs: it has
    /// lines at coordinates. Joining on the blank line is the one rule that holds across documents
    /// without guessing at indentation.
    /// </para>
    /// </remarks>
    internal static List<Piece> Read(string input)
    {
        var pieces = new List<Piece>();

        using var pdf = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = true });

        // The text first, for every page, before a single picture is touched. Measured on a
        // six-megabyte scan: thirty-two seconds spent re-encoding full-page images, then thrown away
        // when the document turned out to have no text at all. Asking the cheap question first
        // brought that to a third of a second.
        //
        // Asked of the whole document rather than of each page, which was the first attempt and was
        // wrong in the other direction: it dropped the cover of a book, and every full-page figure
        // with it. A page carrying only a picture is normal; a document carrying only pictures is
        // a scan, and that is the one this route cannot convert.
        var text = new List<List<string>>();

        foreach (var page in pdf.GetPages())
        {
            text.Add(Paragraphs(page));
        }

        if (text.All(paragraphs => paragraphs.Count == 0))
        {
            return pieces;
        }

        for (var number = 1; number <= text.Count; number++)
        {
            // Une page du PDF devient une page du document. Sans cette coupure, les sept pages
            // s'ecrivent en un seul flux continu — et comme les images sont ancrees « a la page »,
            // celles des pages suivantes se posent aux bonnes coordonnees de la mauvaise page.
            if (number > 1)
            {
                pieces.Add(new Piece("", null, false, 0, 0, 0, 0, PageBreak: true));
            }

            var page = pdf.GetPage(number);
            var paragraphs = text[number - 1];
            var pictures = Pictures(page);

            // Les blocs positionnes l'emportent quand le segmenteur en rend : le texte se pose alors
            // ou il etait sur la page, au lieu de couler sous les images ancrees. Sinon on garde le
            // flux, qui reste la seule lecture sure quand la segmentation ne donne rien.
            var blocs = Blocks(page);

            if (blocs.Count > 0)
            {
                pieces.AddRange(blocs);

                foreach (var picture in pictures)
                {
                    pieces.Add(new Piece(
                        "", picture.Bytes, picture.Jpeg, picture.Width, picture.Height,
                        picture.Left, picture.Top));
                }

                continue;
            }

            if (pictures.Count == 0)
            {
                pieces.AddRange(paragraphs.Select(paragraph => new Piece(paragraph, null, false, 0, 0)));
                continue;
            }

            Interleave(pieces, paragraphs, pictures);
        }

        return pieces;
    }

    /// <summary>One page's text, one entry per paragraph.</summary>
    /// <summary>
    /// Les blocs de texte de la page, avec leur place.
    /// </summary>
    /// <remarks>
    /// L'autre lecture du texte, a cote de <see cref="Paragraphs"/> qui rend l'ordre de lecture sans
    /// les coordonnees. Le segmenteur vient de PdfPig et n'a pas eu a etre ecrit : il regroupe les
    /// mots en blocs — un titre, un paragraphe, une legende — ce qui fait quelques dizaines d'objets
    /// par page la ou un cadre par mot en ferait des milliers.
    ///
    /// <para>
    /// Le retournement vertical se fait ici, comme pour les images : un PDF compte du bas vers le
    /// haut, Word du haut vers le bas.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Retire les caracteres qu'un fichier XML ne peut pas porter.
    /// </summary>
    /// <remarks>
    /// Un PDF de production en contient : celui de test porte un 0x11, et l'ecriture s'arretait
    /// dessus avec « hexadecimal value 0x11, is an invalid character ». Un document ne doit pas etre
    /// perdu pour un octet de controle invisible — il est retire, le reste du texte passe.
    ///
    /// <para>
    /// La liste autorisee est celle de la norme XML 1.0 : tabulation, saut de ligne, retour chariot,
    /// puis tout a partir de l'espace, aux zones interdites pres.
    /// </para>
    /// </remarks>
    private static string Propre(string texte)
    {
        var propre = new StringBuilder(texte.Length);

        foreach (var lettre in texte)
        {
            if (lettre is '\t' or '\n' or '\r'
                || (lettre >= ' ' && lettre <= '퟿')
                || (lettre >= '' && lettre <= '�'))
            {
                propre.Append(lettre);
            }
        }

        return propre.ToString();
    }

    private static List<Piece> Blocks(Page page)
    {
        var blocs = new List<Piece>();

        try
        {
            var mots = page.GetWords().ToList();

            if (mots.Count == 0)
            {
                return blocs;
            }

            foreach (var bloc in UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter
                         .DocstrumBoundingBoxes.Instance.GetBlocks(mots))
            {
                var texte = Propre(bloc.Text.Replace("\r\n", " ").Replace('\n', ' ')).Trim();

                if (texte.Length == 0)
                {
                    continue;
                }

                // La police et le corps dominants du bloc. Sans eux, tout sort en Calibri 11 : le
                // texte d'un bloc de huit points est rendu en onze, deborde de sa case et parait
                // decale vers le bas. Le mauvais placement vertical et la mauvaise police sont donc
                // le meme defaut, pas deux.
                //
                // Le nom d'une police de PDF porte souvent un prefixe de sous-ensemble — six lettres
                // et un plus, « ABCDEF+Arial » — que Word ne connait pas. Il est retire.
                var lettres = bloc.TextLines
                    .SelectMany(ligne => ligne.Words)
                    .SelectMany(mot => mot.Letters)
                    .ToList();

                var police = lettres
                    .GroupBy(l => l.FontName ?? "")
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "";

                var plus = police.IndexOf('+');

                if (plus is >= 0 and < 8)
                {
                    police = police[(plus + 1)..];
                }

                var corps = lettres.Count > 0
                    ? lettres.OrderByDescending(l => l.PointSize).ElementAt(lettres.Count / 2).PointSize
                    : 0;

                // Meme origine que pour les images : le cadre de la page, et non zero.
                var cadre = page.MediaBox.Bounds;

                blocs.Add(new Piece(
                    texte, null, false, bloc.BoundingBox.Width, bloc.BoundingBox.Height,
                    bloc.BoundingBox.Left - cadre.Left, cadre.Top - bloc.BoundingBox.Top,
                    FontName: police, FontSize: corps));
            }
        }
        catch (Exception)
        {
            // Un segmenteur qui echoue rend une page en flux, pas une conversion perdue.
            return [];
        }

        return blocs;
    }

    private static List<string> Paragraphs(Page page)
    {
        var paragraphs = new List<string>();
        var text = ContentOrderTextExtractor.GetText(page);

        if (string.IsNullOrWhiteSpace(text))
        {
            return paragraphs;
        }

        var current = new StringBuilder();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                Flush(paragraphs, current);
                continue;
            }

            if (current.Length > 0 && Continues(current, line))
            {
                current.Append(' ').Append(line);
                continue;
            }

            Flush(paragraphs, current);
            current.Append(line);
        }

        Flush(paragraphs, current);

        return paragraphs;
    }

    /// <summary>
    /// The pictures on a page, with what was drawn above each one.
    /// </summary>
    /// <remarks>
    /// Anything under twenty points on a side is left out. At that size a PDF image is a bullet, a
    /// rule, a logo in a header or a spacer — every document is full of them, and reproducing them
    /// would bring back the ten thousand objects this route exists to avoid.
    ///
    /// <para>
    /// <b>JPEG first, PNG second.</b> PdfPig's PNG conversion refuses a picture stored with
    /// <c>DCTDecode</c>, which is most photographs in most PDFs — the first version of this silently
    /// dropped the only picture in the test document for that reason. A <c>DCTDecode</c> stream is
    /// already a JPEG file, byte for byte, so it is taken as it stands and nothing is re-encoded.
    /// </para>
    /// </remarks>
    private static List<Picture> Pictures(Page page)
    {
        var pictures = new List<Picture>();
        var words = page.GetWords().ToList();

        foreach (var image in page.GetImages())
        {
            var bounds = image.BoundingBox;

            // Trente-deux points, mesures SUR CETTE PROPRIETE avec l'extracteur qui la rend —
            // « SteamXBox.Indexer --diag-pdf <fichier> » imprime la distribution complete. Sur le
            // PDF de test : 37 images posees, cote median 32,9 points, et la population tombe de 24
            // a 9 entre 28 et 40. C'est la que la decoration se separe de l'illustration ; a 20 il
            // en restait 27, soit quatre par page inserees en ligne, ce qui hachait le texte.
            //
            // Cette valeur ne doit pas etre relevee sur une mesure faite ailleurs. Portee a 64 le
            // 17 aout d'apres des chiffres de pdfimages — qui donne les pixels de l'image SOURCE
            // divises par sa resolution, et non la taille a laquelle la page la POSE — elle a vide
            // le document de toutes ses images. Les deux grandeurs n'ont aucun rapport : un PDF
            // reduit ses vignettes.
            if (bounds.Width < 32 || bounds.Height < 32)
            {
                continue;
            }

            var raw = image.RawMemory.ToArray();
            var jpeg = IsJpeg(raw);

            byte[] bytes;

            if (jpeg)
            {
                bytes = raw;
            }
            else if (image.TryGetPng(out var png) && png is { Length: > 0 })
            {
                bytes = png;
            }
            else
            {
                continue;
            }

            // A PDF measures upward from the foot of the page, so a word sitting higher than the
            // picture's top edge has the larger coordinate. Counting them is how the picture finds
            // its place in the text: it is the amount of reading that happens before it.
            var above = words.Count(word => word.BoundingBox.Bottom >= bounds.Top);

            // Retourne ici, une fois pour toutes : un PDF mesure du bas de la page vers le haut,
            // Word du haut vers le bas. Le calcul se fait la ou la hauteur de la page est connue,
            // pour qu'aucun ecrivain n'ait a le refaire ni a se tromper de sens.
            // Le repere de la page ne commence pas toujours a zero : une page rognee porte un cadre
            // dont le coin bas-gauche est ailleurs, et toutes les coordonnees s'en trouvent decalees
            // d'autant. L'origine est donc retiree avant conversion, sur les deux axes.
            var cadre = page.MediaBox.Bounds;
            var depuisLeHaut = cadre.Top - bounds.Top;

            pictures.Add(new Picture(
                bytes, jpeg, bounds.Width, bounds.Height, above, bounds.Left - cadre.Left, depuisLeHaut));
        }

        return pictures;
    }

    /// <summary>A picture on a page, and how much reading comes before it.</summary>
    private readonly record struct Picture(
        byte[] Bytes,
        bool Jpeg,
        double Width,
        double Height,
        int WordsAbove,
        double Left = 0,
        double Top = 0);

    /// <summary>Whether these bytes open a JPEG file.</summary>
    private static bool IsJpeg(byte[] bytes)
        => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    /// <summary>
    /// Puts a page's pictures back among its paragraphs, as near as the text can say.
    /// </summary>
    /// <remarks>
    /// <b>Why not exactly.</b> Placing a picture exactly means placing it at a coordinate, and a
    /// document made of positioned boxes is the one this route was written to replace. So the
    /// picture goes into the flow, between two paragraphs, and the question becomes which two.
    ///
    /// <para>
    /// It is answered by counting words. The extractor gives the page's words with their positions,
    /// so the number of words above the picture is known; the paragraphs are then counted off until
    /// that many words have gone by, and the picture is inserted there. A picture at the top of a
    /// page lands at the top, one at the foot lands at the foot, one beside the third paragraph
    /// lands by the third paragraph.
    /// </para>
    ///
    /// <para>
    /// It is an estimate and it will be wrong on a page laid out in columns, where reading order and
    /// height disagree. Wrong by a paragraph, in a document that opens and scrolls — which is the
    /// trade this whole route makes.
    /// </para>
    /// </remarks>
    private static void Interleave(
        List<Piece> pieces,
        List<string> paragraphs,
        List<Picture> pictures)
    {
        var counts = paragraphs.Select(CountWords).ToList();

        // Sorted so that two pictures landing on the same boundary keep their order down the page.
        var placed = pictures
            .OrderBy(picture => picture.WordsAbove)
            .Select(picture => (Index: PlaceAt(counts, picture.WordsAbove), Picture: picture))
            .ToList();

        for (var index = 0; index <= paragraphs.Count; index++)
        {
            foreach (var (_, picture) in placed.Where(entry => entry.Index == index))
            {
                pieces.Add(new Piece(
                    "", picture.Bytes, picture.Jpeg, picture.Width, picture.Height,
                    picture.Left, picture.Top));
            }

            if (index < paragraphs.Count)
            {
                pieces.Add(new Piece(paragraphs[index], null, false, 0, 0));
            }
        }
    }

    /// <summary>
    /// Which paragraph boundary a given number of words falls on.
    /// </summary>
    /// <remarks>
    /// The whole of the placement rule, and public because it is the one part of this route a user
    /// can see the result of and disagree with. Given how much of the page has been read before a
    /// picture, it says which gap in the text the picture belongs in.
    /// </remarks>
    public static int PlaceAt(IReadOnlyList<int> wordsPerParagraph, int wordsAbove)
    {
        var running = 0;

        for (var index = 0; index < wordsPerParagraph.Count; index++)
        {
            if (running >= wordsAbove)
            {
                return index;
            }

            running += wordsPerParagraph[index];
        }

        return wordsPerParagraph.Count;
    }

    private static int CountWords(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Whether a line carries on the one before it rather than starting something new.
    /// </summary>
    /// <remarks>
    /// A PDF has no paragraphs — it has lines at coordinates — so this has to be inferred, and the
    /// first attempt got it badly wrong. Joining until a blank line seemed obvious and produced two
    /// paragraphs for a six-hundred-kilobyte document: extracted PDF text has almost no blank lines,
    /// so every page became one block.
    ///
    /// <para>
    /// A sentence that was wrapped ends without punctuation and continues in lower case. Anything
    /// else — a line ending in a full stop, a new line starting with a capital, a bullet, a number —
    /// is treated as its own paragraph. It is a guess, and it errs towards too many paragraphs
    /// rather than too few: a split paragraph is a keystroke to repair, a page-long block is not.
    /// </para>
    /// </remarks>
    private static bool Continues(StringBuilder previous, string line)
    {
        var last = previous[^1];

        if (last is '.' or '!' or '?' or ':' or ';' or '•' or '-')
        {
            return false;
        }

        var first = line[0];

        return !char.IsUpper(first) && !char.IsDigit(first) && first is not ('•' or '-' or '–' or '*');
    }

    private static void Flush(List<string> paragraphs, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        var clean = Printable(current.ToString());

        if (clean.Length > 0)
        {
            paragraphs.Add(clean);
        }

        current.Clear();
    }

    /// <summary>
    /// Drops the characters a document cannot contain.
    /// </summary>
    /// <remarks>
    /// A real PDF stopped the whole conversion with "hexadecimal value 0x16 is an invalid
    /// character": a Word document is XML, and XML forbids almost every control character. PDFs
    /// carry them — from the fonts, from the producer, from whatever made the file — and one of them
    /// anywhere would otherwise lose the entire document rather than one character.
    /// </remarks>
    private static string Printable(string text)
    {
        if (!text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
        {
            return text;
        }

        var clean = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (!char.IsControl(character) || character is '\t')
            {
                clean.Append(character);
            }
        }

        return clean.ToString().Trim();
    }

    /// <summary>
    /// Ce que la derniere conversion a vu passer, etage par etage.
    /// </summary>
    /// <remarks>
    /// Pose apres avoir lu quatre fois une chaine dont chaque maillon est correct et dont le
    /// resultat ne l'est pas : 19 images extraites et mesurees, zero dans le .docx. Quand la lecture
    /// du code ne suffit plus, on compte a l'execution.
    /// </remarks>
    /// <remarks>
    /// Prive, et c'est le correctif : lu du dehors, il survivait a la conversion qu'il decrit. Ce
    /// qui sort d'ici sort par le resultat, avec le fichier qu'il commente.
    /// </remarks>
    private static string Trace { get; set; } = "";

    /// <summary>Ce que le validateur du SDK reproche a l'ancre, releve une fois par conversion.</summary>
    /// <inheritdoc cref="Trace" path="/remarks"/>
    private static string Verdict { get; set; } = "";

    /// <summary>Le compte et le verdict en une phrase, telle qu'elle part vers l'appelant.</summary>
    /// <remarks>
    /// Reunis ici plutot que chez l'appelant : les deux decrivent la meme conversion, et les laisser
    /// se rejoindre plus loin obligeait chaque lecteur a refaire le meme assemblage — et a savoir
    /// que le second peut etre vide quand le premier ne l'est pas.
    /// </remarks>
    private static string Bilan
        => (Trace.Length, Verdict.Length) switch
        {
            (0, 0) => "",
            (0, _) => Verdict,
            (_, 0) => Trace,
            _ => $"{Trace} — {Verdict}",
        };

    /// <summary>
    /// Vrai quand le document depasse le plafond d'objets positionnes et repasse au flux.
    /// </summary>
    /// <remarks>
    /// Porte ici plutot que passe en parametre parce que l'ecrivain d'images est appele depuis la
    /// meme boucle, et qu'un document se decide en entier : melanger des images ancrees et un texte
    /// en flux donnerait le pire des deux, des illustrations posees sur un texte qui n'est plus la
    /// ou elles l'attendent.
    /// </remarks>
    private static bool Deborde;

    /// <summary>
    /// La police, le corps et la graisse d'un bloc, tels que Word les attend.
    /// </summary>
    /// <remarks>
    /// Trois corrections que la mesure du 19 aout a rendues necessaires, sur le document de test :
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <b>Type3 n'est pas une police</b> : c'est une categorie du format PDF, des glyphes
    ///     dessines dans le fichier lui-meme. Ecrit tel quel, il demande a Word une police qui
    ///     n'existe nulle part — 223 blocs sur ce document. Mieux vaut ne rien demander et laisser
    ///     la police du document.
    ///   </item>
    ///   <item>
    ///     <b>Le corps etait deux fois trop petit</b> : 4,5 et 5 points la ou le document en porte
    ///     9 et 10. PointSize rend deja des demi-points sur ce lecteur, donc le doubler etait de
    ///     trop.
    ///   </item>
    ///   <item>
    ///     <b>La graisse vit dans le nom</b> — GraphikLCG-Bold, -Semibold. Word attend une famille
    ///     et un attribut ; le suffixe est retire du nom et devient du gras ou de l'italique.
    ///   </item>
    /// </list>
    /// </remarks>
    private static RunProperties Apparence(Piece piece)
    {
        var nom = piece.FontName;
        var gras = false;
        var italique = false;

        var tiret = nom.LastIndexOf('-');

        if (tiret > 0)
        {
            var variante = nom[(tiret + 1)..];
            gras = variante.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                   || variante.Contains("Semibold", StringComparison.OrdinalIgnoreCase)
                   || variante.Contains("Black", StringComparison.OrdinalIgnoreCase);
            italique = variante.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                       || variante.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            if (gras || italique || variante.Equals("Regular", StringComparison.OrdinalIgnoreCase))
            {
                nom = nom[..tiret];
            }
        }

        var proprietes = new RunProperties();

        // Type3 designe des glyphes dessines dans le PDF, pas une famille installable.
        if (nom.Length > 0 && !nom.Equals("Type3", StringComparison.OrdinalIgnoreCase))
        {
            proprietes.AppendChild(new RunFonts { Ascii = nom, HighAnsi = nom });
        }

        if (gras)
        {
            proprietes.AppendChild(new Bold());
        }

        if (italique)
        {
            proprietes.AppendChild(new Italic());
        }

        proprietes.AppendChild(new FontSize { Val = ((int)Math.Round(piece.FontSize * 2)).ToString() });

        return proprietes;
    }

    /// <summary>
    /// Writes the pieces as a Word document.
    /// </summary>
    /// <remarks>
    /// Deux ecritures dans une seule, et c'est le compte des objets positionnes qui tranche. Sous le
    /// plafond, un bloc dont on connait la place devient un cadre pose a ses coordonnees et une
    /// image ancree a la page ; au-dela, tout coule — paragraphes et images en ligne, une image
    /// posee dans le texte comme une lettre, qui suit ce qu'on edite au lieu de rester a une
    /// coordonnee.
    ///
    /// <para>
    /// La seconde forme est celle que ce convertisseur a portee seule pendant longtemps, et elle
    /// reste le repli : le document Word qui ramait en portait dix mille, d'objets positionnes.
    /// </para>
    /// </remarks>
    private static void WriteDocx(string output, IReadOnlyList<Piece> pieces)
    {
        // Compte avant d'ecrire, et repli au-dela du plafond.
        //
        // Un objet positionne coute a Word ce qu'un paragraphe en flux ne coute pas. La version
        // positionnee de ce convertisseur a deja ete abandonnee une fois pour cette raison : dix
        // mille objets pour quatre cents paragraphes, et le document ramait a chaque coup de molette.
        // Mesure du 18 aout sur le PDF de test : 314 cadres et 19 ancres pour sept pages, soit
        // quarante-cinq objets par page — tenable, mais la pente est la meme.
        //
        // Mille est le plafond de depart, a confirmer sur une machine modeste : c'est elle qui
        // decide, pas celle de developpement. Au-dela, le texte coule et les images reprennent le
        // fil, ce qui donne un document mal place plutot qu'un document qui fige Word.
        const int PlafondObjets = 1000;

        var positionnes = pieces.Count(p => p.LeftPt > 0 || p.TopPt > 0);
        var trop = positionnes > PlafondObjets;
        Deborde = trop;

        Trace = $"{pieces.Count} morceau(x) dont {pieces.Count(p => p.IsPicture)} image(s), "
                + $"{positionnes} positionne(s)"
                + (trop ? $" — au-dela de {PlafondObjets}, mise en page abandonnee pour le flux" : "");

        Verdict = "";

        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();

        var body = main.Document.AppendChild(new Body());
        var picture = 0U;

        foreach (var piece in pieces)
        {
            if (piece.PageBreak)
            {
                body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                continue;
            }

            if (piece.IsPicture)
            {
                body.AppendChild(PictureParagraph(main, piece, ++picture));
                continue;
            }

            // La police et le corps du bloc, quand ils ont ete releves. Un corps ecrit en demi-points
            // — la convention de Word — et une police designee par son nom sans le prefixe de
            // sous-ensemble du PDF.
            var run = piece.FontSize > 0
                ? new Run(Apparence(piece), new Text(piece.Text) { Space = SpaceProcessingModeValues.Preserve })
                : new Run(new Text(piece.Text) { Space = SpaceProcessingModeValues.Preserve });

            if (trop)
            {
                body.AppendChild(new Paragraph(run));
                continue;
            }


            // Un bloc dont on connait la place devient un cadre pose a ses coordonnees ; le reste
            // coule. Le cadre est le mecanisme de Word lui-meme, en twips — vingt par point — et il
            // evite les zones de texte DrawingML, bien plus lourdes a construire pour le meme effet.
            body.AppendChild(piece.LeftPt > 0 || piece.TopPt > 0
                ? new Paragraph(
                    new ParagraphProperties(new FrameProperties
                    {
                        X = ((int)(piece.LeftPt * 20)).ToString(),
                        Y = ((int)(piece.TopPt * 20)).ToString(),
                        Width = ((uint)Math.Max(1, piece.WidthPt * 20)).ToString(),
                        HorizontalPosition = HorizontalAnchorValues.Page,
                        VerticalPosition = VerticalAnchorValues.Page,
                        Wrap = TextWrappingValues.None,
                    }),
                    run)
                : new Paragraph(run));
        }

        main.Document.Save();
    }

    /// <summary>
    /// Les images déjà écrites, par empreinte de leur contenu.
    /// </summary>
    /// <remarks>
    /// Un PDF pose la même icône des dizaines de fois : mesuré sur le document de test, le même objet
    /// apparaît quatre fois sur une seule page, et 129 insertions couvrent bien moins d'images
    /// distinctes. Sans ce cache, chacune est réencodée et stockée à part dans le .docx.
    ///
    /// <para>
    /// La clé est l'empreinte des octets plutôt que le numéro d'objet du PDF, que l'extracteur
    /// n'expose pas : deux insertions du même objet portent les mêmes octets, donc le résultat est
    /// identique et ne dépend d'aucun détail de format.
    /// </para>
    ///
    /// <para>
    /// Vidé au début de chaque conversion : une relation d'image appartient au document qui la porte,
    /// et réutiliser l'identifiant d'un document précédent produirait un fichier illisible.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> Deja = [];

    /// <summary>
    /// One picture, at the size it had on the page — anchored where it stood, or in the flow.
    /// </summary>
    /// <remarks>
    /// Kept at its printed size rather than its pixel size. A PDF often stores a picture far larger
    /// than it is drawn — a photograph placed small still carries all its pixels — so taking the
    /// pixels as the size would blow a thumbnail up to fill several pages.
    ///
    /// <para>
    /// Word measures in English metric units: nine hundred and fourteen thousand four hundred to the
    /// inch, so twelve thousand seven hundred to the point, which is what a PDF measures in.
    /// </para>
    /// </remarks>
    private static Paragraph PictureParagraph(MainDocumentPart main, Piece piece, uint number)
    {
        const long UnitsPerPoint = 12700;

        var empreinte = System.Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(piece.Image!));

        if (!Deja.TryGetValue(empreinte, out var identifiant))
        {
            var part = main.AddImagePart(piece.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png);

            using (var bytes = new MemoryStream(piece.Image!))
            {
                part.FeedData(bytes);
            }

            identifiant = main.GetIdOfPart(part);
            Deja[empreinte] = identifiant;
        }

        var width = (long)Math.Max(1, piece.WidthPt * UnitsPerPoint);
        var height = (long)Math.Max(1, piece.HeightPt * UnitsPerPoint);
        var name = $"Image {number}";

        var graphic = new DocumentFormat.OpenXml.Drawing.Graphic(
            new DocumentFormat.OpenXml.Drawing.GraphicData(
                new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties
                        {
                            Id = number,
                            Name = name,
                        },
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                    new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                        new DocumentFormat.OpenXml.Drawing.Blip { Embed = identifiant },
                        new DocumentFormat.OpenXml.Drawing.Stretch(
                            new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                    new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                        new DocumentFormat.OpenXml.Drawing.Transform2D(
                            new DocumentFormat.OpenXml.Drawing.Offset { X = 0, Y = 0 },
                            new DocumentFormat.OpenXml.Drawing.Extents { Cx = width, Cy = height }),
                        new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                            new DocumentFormat.OpenXml.Drawing.AdjustValueList())
                        {
                            Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle,
                        })))
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
            });

        var inline = new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = width, Cy = height },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent
            {
                LeftEdge = 0,
                TopEdge = 0,
                RightEdge = 0,
                BottomEdge = 0,
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = number, Name = name },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };

        // Ancrage a la page quand la position est connue, flux sinon.
        //
        // Premiere tentative le 17 aout : le document est sorti a 23 Ko, texte seul, sans un seul
        // element de dessin. Lu comme « les images disparaissent », c'etait en fait une ecriture
        // interrompue — le paquet etait cree, Save() n'etait jamais atteint, et le catch de Convert
        // avalait le message. Une image qui manque et une exception silencieuse se ressemblent
        // beaucoup vues du fichier produit ; elles ne se ressemblent plus depuis que le compte et le
        // message remontent a l'ecran.
        //
        // Si l'ecriture echoue encore, la fenetre le dira, et le message nommera la cause.
        // L'ancre est CONSTRUITE et VALIDEE, mais pas ecrite : le document sort avec ses images en
        // flux, comme avant, et le validateur du SDK dit ce qu'il reproche a l'ancre dans le message
        // de retour de la conversion. Deviner l'element fautif a coute deux essais ; le demander a
        // celui qui connait le schema en coute zero, et ne peut rien casser puisque le resultat de
        // l'ancre n'est pas utilise.
        if (piece.IsPlaced && Verdict.Length == 0)
        {
            try
            {
                var essai = new Paragraph(new Run(new Drawing(
                    Ancre(piece, width, height, number, name, (DocumentFormat.OpenXml.Drawing.Graphic)graphic.CloneNode(true)))));

                var fautes = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
                    .Validate(essai)
                    .Take(2)
                    .Select(f => $"{f.Description} [{f.Path?.XPath}]")
                    .ToList();

                Verdict = fautes.Count == 0
                    ? "ancre valide selon le SDK"
                    : "ancre refusee : " + string.Join(" | ", fautes);
            }
            catch (Exception exception)
            {
                Verdict = $"ancre : {exception.GetType().Name} — {exception.Message}";
            }
        }

        // Ancrage a la page quand la position est connue, flux sinon.
        //
        // Deux tentatives ont echoue avant celle-ci, chacune en devinant l'element fautif : le .docx
        // sortait a 23 Ko, texte partiel, aucun dessin — l'ecriture s'interrompait a la premiere
        // image et Save() n'etait jamais atteint. C'est OpenXmlValidator qui a tranche en une passe
        // la ou deux relectures n'avaient rien donne : « unexpected child element positionH,
        // expected simplePos ». SimplePosition est obligatoire en premier enfant meme quand
        // l'attribut SimplePos vaut faux, et c'est exactement ce que la deuxieme tentative avait
        // retire.
        //
        // La validation reste en place au-dessus : elle ne coute rien, ne bloque pas l'ecriture, et
        // le verdict part dans le journal a chaque conversion.
        // Une COPIE du graphique, jamais l'original : il est deja rattache a l'Inline construit
        // au-dessus, et un element OpenXML n'appartient qu'a un seul parent. Le validateur ne
        // pouvait pas le voir — il n'a jamais examine qu'une copie —, ce qui explique une ancre
        // declaree valide et une ecriture qui casse quand meme au meme endroit.
        return piece.IsPlaced && !Deborde
            ? new Paragraph(new Run(new Drawing(Ancre(
                piece, width, height, number, name,
                (DocumentFormat.OpenXml.Drawing.Graphic)graphic.CloneNode(true)))))
            : new Paragraph(new Run(new Drawing(inline)));
    }

    /// <summary>
    /// L'image posee a ses coordonnees d'origine, sur la page.
    /// </summary>
    /// <remarks>
    /// Ancree a la page et non au paragraphe : c'est la page qui porte le reperage du PDF, et un
    /// ancrage au paragraphe suivrait le texte au lieu de rester ou l'image etait.
    ///
    /// <para>
    /// <c>BehindDoc</c> est faux et l'habillage nul : l'image se pose par-dessus, sans repousser le
    /// texte. Tant que le texte reste en flux, les deux se recouvrent forcement par endroits — c'est
    /// la limite assumee de cette premiere etape, et c'est ce que les cadres de texte regleront.
    /// </para>
    ///
    /// <para>
    /// <c>AllowOverlap</c> est vrai : sur une page dense, Word deplacerait sinon les images les unes
    /// pour les autres, ce qui defait exactement ce qu'on vient de calculer.
    /// </para>
    /// </remarks>
    private static DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor Ancre(
        Piece piece,
        long width,
        long height,
        uint number,
        string name,
        DocumentFormat.OpenXml.Drawing.Graphic graphic)
    {
        const long UnitsPerPoint = 12700;

        // SimplePosition est OBLIGATOIRE comme premier enfant, meme quand l'attribut SimplePos vaut
        // faux. Retire le 18 aout en croyant qu'il se contredisait avec l'attribut, il a fait
        // echouer l'ecriture ; c'est le validateur du SDK qui a tranche, et pas une relecture de
        // plus : « unexpected child element positionH, expected simplePos ».
        return new DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.SimplePosition { X = 0L, Y = 0L },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.HorizontalPosition(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset(
                    ((long)(piece.LeftPt * UnitsPerPoint)).ToString()))
            {
                RelativeFrom = DocumentFormat.OpenXml.Drawing.Wordprocessing
                    .HorizontalRelativePositionValues.Page,
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.VerticalPosition(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.PositionOffset(
                    ((long)(piece.TopPt * UnitsPerPoint)).ToString()))
            {
                RelativeFrom = DocumentFormat.OpenXml.Drawing.Wordprocessing
                    .VerticalRelativePositionValues.Page,
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = width, Cy = height },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent
            {
                LeftEdge = 0,
                TopEdge = 0,
                RightEdge = 0,
                BottomEdge = 0,
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.WrapNone(),
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = number, Name = name },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
            SimplePos = false,
            RelativeHeight = number * 10U,
            BehindDoc = false,
            Locked = false,
            LayoutInCell = true,
            AllowOverlap = true,
        };
    }
}
