using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce qu'un carnet retient en plus de ses étapes, et qui rend une reprise possible.
/// </summary>
/// <remarks>
/// <b>Le défaut mesuré le 24 août.</b> L'utilisateur donne le sujet de sa vidéo — « un ours jaune
/// qui danse dans une discothèque » —, l'assistant compose pendant vingt appels d'outil, son
/// contexte se remplit, et à « reprends le travail » il redemande le sujet. Les étapes avaient
/// survécu dans le carnet ; la matière, non.
///
/// <para>
/// Un carnet porte donc deux choses : ce qu'il reste à faire, et ce avec quoi le faire. Ce sont les
/// secondes qui manquaient, et sans elles les premières ne se reprennent pas — savoir qu'il faut
/// « encoder le prompt » ne sert à rien quand on a oublié le prompt.
/// </para>
/// </remarks>
[Collection(Carnets.Nom)]
public class AcquisTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("acquis");

    public AcquisTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFactIsKeptAndReadBack()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux"], null);
        FichierTravail.Retenir("Vidéo par texte", ["le sujet est un ours qui danse"], null);

        var relu = FichierTravail.Lire("Vidéo par texte", null);

        Assert.NotNull(relu);
        Assert.Equal(["le sujet est un ours qui danse"], relu!.Acquis);
    }

    // Le modèle repropose volontiers la même phrase à chaque tour : sans garde, le carnet la
    // porterait dix fois et le rappel coûterait dix fois le contexte qu'il vaut.
    [Fact]
    public void TheSameFactIsNotKeptTwice()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux"], null);
        FichierTravail.Retenir("Vidéo par texte", ["le nœud s'appelle VAEDecode"], null);
        FichierTravail.Retenir("Vidéo par texte", ["Le nœud s'appelle VAEDecode"], null);

        Assert.Single(FichierTravail.Lire("Vidéo par texte", null)!.Acquis);
    }

    /// <summary>Réviser le plan ne rend pas faux ce qu'on a appris en l'exécutant.</summary>
    /// <remarks>
    /// C'est le cas qui compte le plus : on renote un carnet précisément quand le plan s'est révélé
    /// trop court, c'est-à-dire au moment où l'on a le plus appris.
    /// </remarks>
    [Fact]
    public void RewritingThePlanKeepsWhatWasLearned()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux"], null);
        FichierTravail.Retenir("Vidéo par texte", ["résolution 480x480"], null);

        FichierTravail.Noter("Vidéo par texte", ["composer le flux", "lancer", "vérifier"], null);

        var relu = FichierTravail.Lire("Vidéo par texte", null);

        Assert.Equal(3, relu!.Taches.Count);
        Assert.Equal(["résolution 480x480"], relu.Acquis);
    }

    [Fact]
    public void KeepingOnAMissingNotebookSaysSoRatherThanCreatingOne()
    {
        Assert.Null(FichierTravail.Retenir("jamais ouvert", ["un fait"], null));
        Assert.Empty(FichierTravail.Lister(null));
    }

    [Fact]
    public void EmptyFactsAreDropped()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer"], null);
        FichierTravail.Retenir("Vidéo par texte", ["", "   ", "un vrai fait"], null);

        Assert.Equal(["un vrai fait"], FichierTravail.Lire("Vidéo par texte", null)!.Acquis);
    }

    // Le rappel est ce que le modèle lit avant de répondre : un acquis qui n'y figure pas n'existe
    // pas pour lui, si bien retenu soit-il sur le disque.
    [Fact]
    public void FactsAppearInTheSummaryTheModelReads()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer"], null);
        FichierTravail.Retenir("Vidéo par texte", ["le sujet est un ours qui danse"], null);

        var rappel = AssistantLocal.RappelTravaux(null);

        Assert.Contains("ACQUIS", rappel, StringComparison.Ordinal);
        Assert.Contains("le sujet est un ours qui danse", rappel, StringComparison.Ordinal);
    }
}
