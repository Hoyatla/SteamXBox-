using System.Text.Json.Nodes;
using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Montrer une image à l'assistant, et non lui en donner le nom.
/// </summary>
/// <remarks>
/// <b>Le modèle voyait ; c'est nous qui ne lui montrions rien.</b> Le serveur charge le projecteur
/// — « loaded multimodal model » dans son journal — mais la conversation ne transportait que du
/// texte. Interrogé, l'assistant répondait « je ne peux pas lire une image » : il décrivait sa
/// situation, pas sa capacité, et l'utilisateur en concluait que le produit ne savait pas voir.
/// </remarks>
public class VueTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("vue");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Image(string nom)
    {
        var chemin = Path.Combine(_bac.FullName, nom);
        File.WriteAllBytes(chemin, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        return chemin;
    }

    /// <summary>Un chemin déposé seul sur sa ligne désigne l'image.</summary>
    /// <remarks>C'est ce que produit le glisser-déposer : le chemin, seul, sur une ligne.</remarks>
    [Fact]
    public void APathDroppedOnItsOwnLineIsFound()
    {
        var image = Image("photo.png");

        Assert.Equal([image], Vue.Designees($"anime ceci\n{image}\n"));
    }

    /// <summary>Un chemin entre guillemets au milieu d'une phrase est trouvé.</summary>
    /// <remarks>
    /// C'est la forme exacte que l'utilisateur a produite le 23 août :
    /// <c>"D:\…\image_00003_.png" refaire un essai avec cette image</c>. Le chemin porte des
    /// espaces — « Modspack perso » — donc aucune règle de découpage ne peut le retrouver ; seule
    /// l'existence du fichier tranche.
    /// </remarks>
    [Fact]
    public void AQuotedPathInTheMiddleOfASentenceIsFound()
    {
        var image = Image("un dossier avec espaces.png");

        Assert.Equal([image], Vue.Designees($"\"{image}\" refaire un essai avec cette image"));
    }

    /// <summary>Un fichier qui n'existe pas n'est pas joint.</summary>
    /// <remarks>
    /// Le modèle écrit des chemins plausibles quand il en invente ; les joindre reviendrait à
    /// confirmer son invention par une pièce jointe vide.
    /// </remarks>
    [Fact]
    public void AFileThatDoesNotExistIsNotAttached()
        => Assert.Empty(Vue.Designees(@"D:\photos\inventee.png"));

    /// <summary>Un fichier qui n'est pas une image n'est pas joint.</summary>
    [Fact]
    public void AFileThatIsNotAnImageIsNotAttached()
    {
        var texte = Path.Combine(_bac.FullName, "notes.txt");
        File.WriteAllText(texte, "rien");

        Assert.Empty(Vue.Designees(texte));
    }

    /// <summary>Au-delà de deux images, on n'en joint plus.</summary>
    /// <remarks>
    /// Une image coûte plus de mille jetons. L'utilisateur qui dépose un dossier entier ne s'attend
    /// pas à ce que sa conversation en meure.
    /// </remarks>
    [Fact]
    public void BeyondTwoImagesNoMoreAreAttached()
    {
        var demande = string.Join(
            "\n", Image("a.png"), Image("b.jpg"), Image("c.webp"), Image("d.bmp"));

        Assert.Equal(Vue.Maximum, Vue.Designees(demande).Count);
    }

    /// <summary>La même image citée deux fois n'est jointe qu'une.</summary>
    [Fact]
    public void TheSameImageTwiceIsAttachedOnce()
    {
        var image = Image("photo.png");

        Assert.Single(Vue.Designees($"{image}\n{image}"));
    }

    /// <summary>Sans image, le message reste du texte simple.</summary>
    /// <remarks>
    /// C'est le cas de l'écrasante majorité des tours. Les envelopper tous dans un tableau à une
    /// branche aurait gonflé chaque échange sans rien apporter.
    /// </remarks>
    [Fact]
    public void WithoutAnImageTheMessageStaysPlainText()
        => Assert.IsAssignableFrom<JsonValue>(Vue.Contenu("bonjour", null));

    /// <summary>Avec une image, le message porte le texte ET l'image.</summary>
    /// <remarks>
    /// Le texte est conservé, chemin compris : le modèle en a besoin pour nommer le fichier aux
    /// outils, qui ne voient rien et ne travaillent que sur des chemins.
    /// </remarks>
    [Fact]
    public void WithAnImageTheMessageCarriesBothTheTextAndTheImage()
    {
        var contenu = Vue.Contenu($"regarde\n{Image("photo.png")}", null);

        var morceaux = Assert.IsAssignableFrom<JsonArray>(contenu);

        Assert.Equal(2, morceaux.Count);
        Assert.Equal("text", morceaux[0]!["type"]!.GetValue<string>());
        Assert.Equal("image_url", morceaux[1]!["type"]!.GetValue<string>());
        Assert.StartsWith(
            "data:image/png;base64,",
            morceaux[1]!["image_url"]!["url"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    /// <summary>La consigne interdit au modèle de nier qu'il voit.</summary>
    [Fact]
    public void TheInstructionForbidsDenyingItCanSee()
        => Assert.Contains("TU VOIS LES IMAGES", AssistantLocal.Regles, StringComparison.Ordinal);

    /// <summary>La consigne interdit les listes d'options à répétition.</summary>
    /// <remarks>
    /// Trois échanges de suite, l'assistant a répondu par une liste numérotée sans jamais rien
    /// faire — renvoyant à l'utilisateur le travail de choisir à sa place, puis reposant la même
    /// question sous une autre forme.
    /// </remarks>
    [Fact]
    public void TheInstructionForbidsRepeatedOptionLists()
        => Assert.Contains("AGIS, N'ÉNUMÈRE PAS", AssistantLocal.Regles, StringComparison.Ordinal);
}
