using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le découpage d'une cible, et la barre verticale que l'utilisateur a le droit de taper.
/// </summary>
/// <remarks>
/// Ce qui est éprouvé ici tient en une phrase : ce que le manifeste écrit sépare, ce que
/// l'utilisateur écrit ne sépare pas. Les deux arrivent dans la même chaîne, et rien d'autre ne
/// distingue leurs barres.
///
/// <para>
/// Le défaut mesuré avant cette règle : l'invite « un chat roux | style aquarelle », tapée dans le
/// panneau de création d'image, ajoutait un réglage que personne n'avait écrit et le flux refusait
/// de partir sur « Réglage illisible, « = » attendu ».
/// </para>
/// </remarks>
public class SegmentsTests
{
    [Fact]
    public void UneCibleOrdinaireSeDecoupeSurSesBarres()
        => Assert.Equal(
            ["image.png", "realesrgan", "4", "png"],
            Segments.Decouper("image.png|realesrgan|4|png"));

    // La barre tapée par l'utilisateur reste dans sa valeur : c'est tout l'objet de la règle.
    [Fact]
    public void UneBarreEchappeeResteDansSonSegment()
        => Assert.Equal(
            ["flux.json", "6.text=un chat roux | style aquarelle"],
            Segments.Decouper(@"flux.json|6.text=un chat roux \| style aquarelle"));

    [Fact]
    public void EchapperPuisDecouperRendLaValeurIntacte()
    {
        const string Tape = "un chat roux | style aquarelle | rien d'autre";

        var cible = "flux.json|6.text=" + Segments.Echapper(Tape);

        Assert.Equal(["flux.json", "6.text=" + Tape], Segments.Decouper(cible));
    }

    // Une cible porte des chemins Windows, et une barre oblique inverse y est un séparateur de
    // dossier. Traiter toutes les barres obliques inverses comme des échappements les détruirait.
    [Fact]
    public void UnCheminWindowsTraverseIntact()
        => Assert.Equal(
            [@"C:\Outils\Python\python.exe", @"D:\Films\a.mp4"],
            Segments.Decouper(@"C:\Outils\Python\python.exe|D:\Films\a.mp4"));

    [Fact]
    public void LeDernierSegmentAutoriseGardeToutLeReste()
        => Assert.Equal(
            ["document.pdf", "docx|autre chose"],
            Segments.Decouper("document.pdf|docx|autre chose", 2));

    [Fact]
    public void UneCibleVideRendUnSegmentVide()
        => Assert.Equal([""], Segments.Decouper(""));

    [Fact]
    public void UneCibleNulleRendUnSegmentVide()
        => Assert.Equal([""], Segments.Decouper(null));

    // Rien à échapper ne doit rien changer : la très grande majorité des valeurs passe par là.
    [Fact]
    public void EchapperNeTouchePasCeQuiNaPasDeBarre()
        => Assert.Equal("un chat roux", Segments.Echapper("un chat roux"));
}
