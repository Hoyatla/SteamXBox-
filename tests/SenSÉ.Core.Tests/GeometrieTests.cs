using SenSÉ.Tools.Interface;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// La taille et la place que l'utilisateur donne à une fenêtre, d'une session à l'autre.
/// </summary>
/// <remarks>
/// Aucune fenêtre du produit ne retenait rien : chaque ouverture repartait des dimensions écrites
/// dans le XAML, centrée. Un magasin unique plutôt qu'une logique par fenêtre — six fenêtres et
/// autant de panneaux d'outils qui porteraient chacun leur sauvegarde feraient six occasions
/// d'oublier le même cas.
/// </remarks>
public class GeometrieTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("geometrie");

    public GeometrieTests() => Geometrie.Racine = _bac.FullName;

    public void Dispose()
    {
        Geometrie.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFrameIsWrittenAndReadBack()
    {
        Geometrie.Ecrire("assistant", new Cadre(100, 50, 800, 600, false));

        var relu = Geometrie.Lire("assistant");

        Assert.NotNull(relu);
        Assert.Equal(800, relu!.Largeur);
        Assert.Equal(600, relu.Hauteur);
        Assert.False(relu.Maximisee);
    }

    [Fact]
    public void AWindowNeverSeenHasNoFrame()
        => Assert.Null(Geometrie.Lire("jamais-ouverte"));

    /// <summary>Deux fenêtres ne se marchent pas dessus.</summary>
    /// <remarks>
    /// Le cas se produit à chaque sortie du produit, où plusieurs fenêtres se ferment ensemble :
    /// une écriture qui repartirait du fichier tel qu'il était à l'ouverture effacerait la place
    /// de sa voisine.
    /// </remarks>
    [Fact]
    public void OneWindowDoesNotEraseAnother()
    {
        Geometrie.Ecrire("assistant", new Cadre(0, 0, 800, 600, false));
        Geometrie.Ecrire("atelier", new Cadre(10, 10, 900, 700, false));

        Assert.Equal(800, Geometrie.Lire("assistant")!.Largeur);
        Assert.Equal(900, Geometrie.Lire("atelier")!.Largeur);
    }

    // Chaque outil garde la sienne : élargir le convertisseur n'élargit pas l'agrandisseur.
    [Fact]
    public void EachToolKeepsItsOwnSize()
    {
        Geometrie.Ecrire(Geometrie.Outil("convertir-document"), new Cadre(0, 0, 500, 400, false));
        Geometrie.Ecrire(Geometrie.Outil("agrandir-image"), new Cadre(0, 0, 700, 900, false));

        Assert.Equal(500, Geometrie.Lire(Geometrie.Outil("convertir-document"))!.Largeur);
        Assert.Equal(700, Geometrie.Lire(Geometrie.Outil("agrandir-image"))!.Largeur);
    }

    /// <summary>Une fenêtre sans surface n'est pas retenue.</summary>
    /// <remarks>
    /// Une fenêtre réduite rend des dimensions nulles. Les écrire la condamnerait à rouvrir
    /// invisible — et l'utilisateur ne peut pas en sortir, puisqu'il faudrait la voir pour la
    /// redimensionner.
    /// </remarks>
    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, -1)]
    public void AFrameWithoutSurfaceIsRefused(double largeur, double hauteur)
    {
        Geometrie.Ecrire("assistant", new Cadre(0, 0, largeur, hauteur, false));

        Assert.Null(Geometrie.Lire("assistant"));
    }

    [Fact]
    public void TheMaximisedStateSurvivesWithTheRestoredSize()
    {
        Geometrie.Ecrire("reglages", new Cadre(20, 30, 1000, 720, true));

        var relu = Geometrie.Lire("reglages")!;

        Assert.True(relu.Maximisee);

        // Ce qui est retenu est la taille qu'elle retrouvera, pas celle de l'écran entier.
        Assert.Equal(1000, relu.Largeur);
    }

    /// <summary>Un écran débranché ne doit pas emporter la fenêtre hors du bureau.</summary>
    [Fact]
    public void AFrameOnAVanishedScreenIsNotVisible()
        => Assert.False(Geometrie.Visible(
            new Cadre(3000, 100, 800, 600, false), 0, 0, 1920, 1080));

    [Fact]
    public void AFrameOnTheDesktopIsVisible()
        => Assert.True(Geometrie.Visible(
            new Cadre(100, 100, 800, 600, false), 0, 0, 1920, 1080));

    // À cheval sur un bord reste légitime : exiger qu'elle tienne entière la recentrerait sans
    // raison. Ce qu'il faut, c'est de quoi la reprendre à la souris.
    [Fact]
    public void AFrameStraddlingAnEdgeStaysVisible()
        => Assert.True(Geometrie.Visible(
            new Cadre(1700, 100, 800, 600, false), 0, 0, 1920, 1080));

    [Fact]
    public void AFrameBarelyTouchingIsNotEnoughToGrab()
        => Assert.False(Geometrie.Visible(
            new Cadre(1900, 100, 800, 600, false), 0, 0, 1920, 1080));

    [Fact]
    public void AnUnreadableStoreFallsBackToTheXamlSizes()
    {
        File.WriteAllText(Path.Combine(_bac.FullName, "fenetres.json"), "{ pas du json");

        Assert.Null(Geometrie.Lire("assistant"));
    }
}
