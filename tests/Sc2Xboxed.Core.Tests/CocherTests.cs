using SteamXBox.Tools.Assistant;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Cocher une étape : ce qui doit passer, et ce que l'échec doit dire.
/// </summary>
/// <remarks>
/// <b>Un modèle recopie mal.</b> Il reformule, tronque, numérote. Exiger le mot à mot ferait échouer
/// un cochage parfaitement clair — et l'échec, lui, coûte cher : le 23 août, un <c>travail_cocher</c>
/// manqué a laissé l'assistant sans rien à dire, et l'utilisateur a reçu la consigne interne
/// « Relis le carnet pour voir ce qu'il porte » comme réponse à sa demande.
/// </remarks>
[Collection(Carnets.Nom)]
public class CocherTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("cocher");

    public CocherTests()
    {
        FichierTravail.Racine = _bac.FullName;

        FichierTravail.Noter(
            "Faire des vidéos",
            ["Choisir le type de vidéo à créer (image fixe -> vidéo, montage photos, etc.)",
             "Identifier les fichiers sources ou images à utiliser"],
            null);
    }

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Le texte exact passe, évidemment.</summary>
    [Fact]
    public void TheExactTextWorks()
        => Assert.NotNull(FichierTravail.Cocher(
            "Faire des vidéos",
            "Identifier les fichiers sources ou images à utiliser",
            null));

    /// <summary>Le début de l'étape suffit : c'est ce qu'un modèle recopie en pratique.</summary>
    /// <remarks>
    /// Une étape longue avec une parenthèse ne sera jamais reproduite entière. Exiger l'égalité
    /// aurait rendu le cochage impossible sur exactement les carnets que le modèle écrit lui-même.
    /// </remarks>
    [Theory]
    [InlineData("Choisir le type de vidéo à créer")]
    [InlineData("Choisir le type")]
    [InlineData("choisir LE TYPE de vidéo")]
    public void ThePrefixIsEnough(string abrege)
        => Assert.NotNull(FichierTravail.Cocher("Faire des vidéos", abrege, null));

    /// <summary>Les deux formes que le modèle produit réellement sont reconnues.</summary>
    /// <remarks>
    /// Elles viennent d'une session du 23 août. Le modèle énonce le plan en numérotant ses étapes,
    /// puis coche avec sa propre énumération ; et il réécrit les parenthèses d'exemples en les
    /// reformulant. Ni l'une ni l'autre n'est un préfixe de l'étape enregistrée, et le cochage
    /// échouait sur une étape que n'importe quel lecteur aurait reconnue.
    /// </remarks>
    [Theory]
    [InlineData("1. Choisir le type de vidéo à créer")]
    [InlineData("2) Identifier les fichiers sources")]
    [InlineData("— Identifier les fichiers sources ou images à utiliser")]
    [InlineData("Choisir le type de vidéo à créer (animer une image fixe, assembler des photos)")]
    public void TheFormsTheModelActuallyWritesAreRecognised(string tel)
        => Assert.NotNull(FichierTravail.Cocher("Faire des vidéos", tel, null));

    /// <summary>La tolérance ne va pas jusqu'à cocher n'importe quoi.</summary>
    /// <remarks>
    /// C'est la contrepartie qui rend la tolérance acceptable. Un carnet qui déclare fait ce qui ne
    /// l'est pas vaut moins que pas de carnet du tout : c'est la seule trace sur laquelle
    /// l'utilisateur s'appuie pour savoir où en est le travail.
    /// </remarks>
    [Theory]
    [InlineData("3. Envoyer un courriel")]
    [InlineData("(image fixe -> vidéo)")]
    [InlineData("Vérifier le résultat")]
    public void ToleranceStopsShortOfCheckingAnything(string etranger)
        => Assert.Null(FichierTravail.Cocher("Faire des vidéos", etranger, null));

    /// <summary>Une étape qui n'existe pas ne coche rien, et ne coche surtout pas la voisine.</summary>
    /// <remarks>
    /// Cocher au hasard serait pire que refuser : le carnet dirait fait ce qui ne l'est pas, et
    /// c'est précisément la trace sur laquelle l'utilisateur s'appuie pour savoir où en est le
    /// travail.
    /// </remarks>
    [Fact]
    public void AnUnknownStepChecksNothing()
    {
        Assert.Null(FichierTravail.Cocher("Faire des vidéos", "Envoyer un courriel", null));

        var carnet = FichierTravail.Lire("Faire des vidéos", null)!;

        Assert.All(carnet.Taches, t => Assert.False(t.Faite));
    }

    /// <summary>Un carnet qui n'existe pas se distingue d'une étape qui n'existe pas.</summary>
    /// <remarks>
    /// Les deux rendaient <c>null</c>, donc le même message : « Carnet ou étape introuvable ». Le
    /// modèle ne pouvait pas savoir lequel des deux arguments corriger, et ne corrigeait ni l'un ni
    /// l'autre.
    /// </remarks>
    [Fact]
    public void AMissingNotebookIsNotAMissingStep()
    {
        Assert.Null(FichierTravail.Lire("Carnet imaginaire", null));
        Assert.NotNull(FichierTravail.Lire("Faire des vidéos", null));
    }
}
