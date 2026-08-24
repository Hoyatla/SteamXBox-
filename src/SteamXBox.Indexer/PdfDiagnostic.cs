using UglyToad.PdfPig;

namespace SteamXBox.Indexer;

/// <summary>
/// Décrit ce qu'un PDF contient réellement, tel que PdfPig le voit.
/// </summary>
/// <remarks>
/// Écrit après une erreur de mesure qui a coûté un aller-retour : le seuil d'images de la route Word
/// a été relevé de 20 à 64 points sur la foi de chiffres tirés de <c>pdfimages</c>, qui donne les
/// pixels de l'image <b>source</b> divisés par sa résolution. Le filtre du produit, lui, lit
/// <c>BoundingBox</c> — la taille à laquelle l'image est <b>posée sur la page</b>. Un document
/// réduit ses vignettes, donc les deux grandeurs n'ont aucun rapport, et les vingt-trois images
/// annoncées se sont révélées être zéro : le document est sorti vide.
///
/// <para>
/// D'où ce mode : mesurer avec l'extracteur que le code utilise, dans un projet qui le référence
/// déjà, plutôt qu'avec un outil voisin depuis une console. Il ne modifie rien et n'écrit rien — il
/// imprime ce qu'il voit.
/// </para>
/// </remarks>
public static class PdfDiagnostic
{
    /// <summary>Les seuils dont la route Word pourrait avoir besoin, mesurés d'un coup.</summary>
    private static readonly int[] Seuils = [10, 15, 20, 24, 28, 32, 40, 48, 64, 96];

    public static int Run(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Fichier introuvable : {path}");
            return 2;
        }

        try
        {
            using var document = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true });

            var cotes = new List<double>();
            var pages = 0;
            var caracteres = 0;
            var retenues = 0;
            var jpeg = 0;
            var pngs = 0;
            var perdues = 0;
            var douces = 0;
            var masquees = 0;

            foreach (var page in document.GetPages())
            {
                pages++;
                caracteres += page.Text.Length;

                foreach (var image in page.GetImages())
                {
                    var boite = image.BoundingBox;
                    cotes.Add(Math.Min(boite.Width, boite.Height));

                    // Au-dela de la taille, une image doit surtout etre RECUPERABLE : la route Word
                    // prend les octets bruts quand ils sont deja du JPEG, sinon demande un PNG, et
                    // abandonne l'image si ni l'un ni l'autre ne marche. Une image comptee ici mais
                    // absente du document final se perd la, pas dans le placement.
                    if (Math.Min(boite.Width, boite.Height) < 32)
                    {
                        continue;
                    }

                    retenues++;

                    // Les images a masque sont l'hypothese la plus probable derriere « une image
                    // n'est pas rendue correctement » : un masque de transparence ou un masque
                    // binaire est un objet separe dans le PDF, et une conversion en PNG qui ne
                    // l'applique pas rend un fond noir, une vignette pleine, ou rien du tout.
                    // Compte ici pour que la question se tranche sur un chiffre.
                    if (image.ImageDictionary is { } dictionnaire)
                    {
                        var cles = dictionnaire.Data.Keys.ToList();

                        if (cles.Contains("SMask"))
                        {
                            douces++;
                        }
                        else if (cles.Contains("Mask"))
                        {
                            masquees++;
                        }
                    }

                    var brut = image.RawMemory.ToArray();

                    if (brut.Length > 2 && brut[0] == 0xFF && brut[1] == 0xD8)
                    {
                        jpeg++;
                    }
                    else if (image.TryGetPng(out var png) && png is { Length: > 0 })
                    {
                        pngs++;
                    }
                    else
                    {
                        perdues++;
                    }
                }
            }

            Console.WriteLine($"{Path.GetFileName(path)}");
            Console.WriteLine($"  {pages} page(s), {caracteres} caractères de couche texte, {cotes.Count} image(s)");

            if (cotes.Count == 0)
            {
                Console.WriteLine("  Aucune image : rien à filtrer, et la route Word n'a que du texte à poser.");
                return 0;
            }

            cotes.Sort();

            Console.WriteLine($"  Côté le plus petit d'une image, en points de la page :");
            Console.WriteLine($"    minimum {cotes[0]:F1}   médiane {cotes[cotes.Count / 2]:F1}   maximum {cotes[^1]:F1}");
            Console.WriteLine();
            Console.WriteLine("  Ce que chaque seuil conserverait :");

            foreach (var seuil in Seuils)
            {
                var gardees = cotes.Count(c => c >= seuil);
                var part = 100.0 * gardees / cotes.Count;

                Console.WriteLine($"    >= {seuil,3} pt  ->  {gardees,4} image(s)  ({part,5:F1} %)");
            }

            Console.WriteLine();
            Console.WriteLine("  Le seuil se lit ici, et nulle part ailleurs : c'est la propriété que le");
            Console.WriteLine("  filtre de PdfToDocument compare, mesurée sur ce document.");
            Console.WriteLine();
            Console.WriteLine($"  Sur les {retenues} image(s) qui passent le seuil de 32 points :");
            Console.WriteLine($"    JPEG repris tel quel   : {jpeg}");
            Console.WriteLine($"    converties en PNG      : {pngs}");
            Console.WriteLine($"    ABANDONNEES            : {perdues}");
            Console.WriteLine($"    à masque doux (SMask)  : {douces}");
            Console.WriteLine($"    à masque binaire (Mask): {masquees}");
            Console.WriteLine();
            Console.WriteLine("  Une image abandonnée ne sort ni du placement ni du seuil : l'extracteur");
            Console.WriteLine("  n'a pas su en rendre les octets, et la route Word passe à la suivante.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lecture impossible : {exception.GetType().Name} — {exception.Message}");
            return 3;
        }
    }
}
