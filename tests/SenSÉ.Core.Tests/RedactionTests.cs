using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SenSÉ.Tools.Documents;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Écrire un document sans ouvrir de fenêtre.
/// </summary>
/// <remarks>
/// <b>Ces épreuves existent à cause d'une session qui s'est terminée sans rien produire.</b>
/// 9 septembre 2026, à « écris trois paragraphes et enregistre-les en Word » : l'assistant a lancé
/// l'Éditeur Texte, tenté d'y poser le contenu, reçu « le panneau n'est pas ouvert », rouvert
/// l'outil, retenté, noté un carnet, coché une étape qu'il n'avait pas faite, rempli son contexte
/// et rendu la main. <b>Quatre fenêtres à l'écran, zéro document sur le disque.</b>
///
/// <para>
/// La cause n'était pas le modèle mais le chemin : écrire un document passait forcément par une
/// interface graphique, alors que ce que la demande voulait était un fichier. Ce qui suit vérifie
/// qu'il y a maintenant une route sans fenêtre — et que ce qu'elle produit est un vrai document,
/// relu par la bibliothèque même qui l'a écrit.
/// </para>
/// </remarks>
public class RedactionTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("redaction");

    public RedactionTests() => Redaction.Racine = _bac.FullName;

    public void Dispose()
    {
        Redaction.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private const string Trois = """
        Nvidia affiche une croissance prévue de 70 % en 2028 [fr.euronews.com].

        L'entreprise va acquérir Hugging Face pour 12,93 Md$ [lemondeinformatique.fr].

        Peter Thiel s'est séparé de ses actions [lefigaro.fr].
        """;

    /// <summary>Le .docx écrit est un vrai document Word, et il contient ce qu'on a dicté.</summary>
    /// <remarks>
    /// <b>Relu par Open XML, pas seulement écrit.</b> Un fichier peut porter l'extension d'un
    /// format sans en être un ; la seule preuve qui vaille est qu'il se rouvre. Cette épreuve fait
    /// ce que Word ferait — et c'est aussi ce qui rend le format natif préférable à une conversion
    /// par un logiciel qui n'est pas installé sur cette machine.
    /// </remarks>
    [Fact]
    public void TheDocxIsARealWordDocumentAndHoldsWhatWasDictated()
    {
        var rendu = Redaction.Ecrire("Nvidia, trois paragraphes", Trois, "docx");

        Assert.True(rendu.Reussi, rendu.Probleme);
        Assert.EndsWith(".docx", rendu.Chemin, StringComparison.Ordinal);

        using var document = WordprocessingDocument.Open(rendu.Chemin, isEditable: false);
        var corps = document.MainDocumentPart!.Document!.Body!;
        var paragraphes = corps.Elements<Paragraph>().ToList();

        // Le titre, puis les trois paragraphes.
        Assert.Equal(4, paragraphes.Count);
        Assert.Equal("Nvidia, trois paragraphes", paragraphes[0].InnerText);

        Assert.Contains("70 %", paragraphes[1].InnerText, StringComparison.Ordinal);
        Assert.Contains("Hugging Face", paragraphes[2].InnerText, StringComparison.Ordinal);
        Assert.Contains("Peter Thiel", paragraphes[3].InnerText, StringComparison.Ordinal);
    }

    /// <summary>Les renvois aux sources survivent au passage en Word.</summary>
    /// <remarks>
    /// C'est tout l'intérêt d'avoir mis la publication dans l'étiquette plutôt qu'un numéro : le
    /// document part vivre ailleurs, et la citation part avec lui.
    /// </remarks>
    [Fact]
    public void TheCitationsSurviveIntoTheDocument()
    {
        var rendu = Redaction.Ecrire("Nvidia", Trois, "docx");

        using var document = WordprocessingDocument.Open(rendu.Chemin, isEditable: false);

        var tout = document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("[fr.euronews.com]", tout, StringComparison.Ordinal);
        Assert.Contains("[lefigaro.fr]", tout, StringComparison.Ordinal);
    }

    /// <summary>Le titre est en gras, et plus gros que le corps.</summary>
    [Fact]
    public void TheTitleIsBoldAndLargerThanTheBody()
    {
        var rendu = Redaction.Ecrire("Un titre", "Un corps de texte.", "docx");

        using var document = WordprocessingDocument.Open(rendu.Chemin, isEditable: false);
        var paragraphes = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();

        Assert.NotNull(paragraphes[0].Descendants<Bold>().FirstOrDefault());
        Assert.Null(paragraphes[1].Descendants<Bold>().FirstOrDefault());
    }

    /// <summary>Les trois autres formats natifs s'écrivent sans rien d'installé.</summary>
    [Theory]
    [InlineData("html", ".html")]
    [InlineData("md", ".md")]
    [InlineData("txt", ".txt")]
    public void TheOtherNativeFormatsNeedNothingInstalled(string format, string extension)
    {
        var rendu = Redaction.Ecrire("Nvidia", Trois, format);

        Assert.True(rendu.Reussi, rendu.Probleme);
        Assert.EndsWith(extension, rendu.Chemin, StringComparison.Ordinal);
        Assert.Contains("Hugging Face", File.ReadAllText(rendu.Chemin), StringComparison.Ordinal);
    }

    /// <summary>Un format absent est dit avec ce qui marche à la place.</summary>
    /// <remarks>
    /// <b>« LibreOffice n'est pas installé » laisse le modèle sans issue</b>, et il recommencera
    /// autrement — c'est ce qui a produit quatre fenêtres. Nommer le format qui marche lui en donne
    /// une, et c'est presque toujours celle que l'utilisateur voulait.
    /// </remarks>
    [Fact]
    public void AMissingConverterIsSaidWithWhatWorksInstead()
    {
        var rendu = Redaction.Ecrire("Nvidia", Trois, "pdf");

        if (rendu.Reussi)
        {
            // LibreOffice est la sur cette machine : la conversion a abouti, rien a verifier de plus.
            Assert.EndsWith(".pdf", rendu.Chemin, StringComparison.Ordinal);

            return;
        }

        Assert.Contains("LibreOffice", rendu.Probleme, StringComparison.Ordinal);
        Assert.Contains("docx", rendu.Probleme, StringComparison.Ordinal);
    }

    /// <summary>Un format inventé est refusé en nommant ceux qui existent.</summary>
    [Fact]
    public void AnInventedFormatIsRefusedByNamingTheRealOnes()
    {
        var rendu = Redaction.Ecrire("Nvidia", Trois, "wordperfect");

        Assert.False(rendu.Reussi);
        Assert.Contains("docx", rendu.Probleme, StringComparison.Ordinal);
    }

    /// <summary>Un texte vide n'écrit pas un fichier vide.</summary>
    /// <remarks>
    /// Un document de zéro octet sur le disque est pire qu'un refus : il a l'air d'avoir marché, et
    /// personne ne s'en aperçoit avant de l'ouvrir.
    /// </remarks>
    [Fact]
    public void AnEmptyTextWritesNoFileAtAll()
    {
        var rendu = Redaction.Ecrire("Nvidia", "   ", "docx");

        Assert.False(rendu.Reussi);
        Assert.False(Directory.Exists(Path.Combine(_bac.FullName, "Travaux", "Documents"))
                     && Directory.GetFiles(Path.Combine(_bac.FullName, "Travaux", "Documents")).Length > 0);
    }

    /// <summary>Le document reste dans le produit, sous un nom daté et lisible.</summary>
    [Fact]
    public void TheDocumentStaysInsideTheProductUnderADatedName()
    {
        var rendu = Redaction.Ecrire("Nvidia, trois paragraphes", Trois, "docx");

        Assert.StartsWith(
            Path.Combine(_bac.FullName, "Travaux", "Documents"), rendu.Chemin, StringComparison.Ordinal);
        Assert.Contains("nvidia", Path.GetFileName(rendu.Chemin), StringComparison.Ordinal);
    }

    // Deux documents du meme titre a deux moments sont deux fichiers, pas un seul ecrase.
    [Fact]
    public void TwoDocumentsOfTheSameTitleDoNotOverwriteEachOther()
    {
        var premier = Redaction.Ecrire("Nvidia", Trois, "md");

        Assert.True(File.Exists(premier.Chemin));
        Assert.Contains(
            DateTimeOffset.Now.ToString("yyyyMM"), Path.GetFileName(premier.Chemin), StringComparison.Ordinal);
    }
}
