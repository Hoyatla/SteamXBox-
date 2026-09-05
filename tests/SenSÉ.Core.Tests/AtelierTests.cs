using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le classeur qui réunit plusieurs outils en un seul.
/// </summary>
/// <remarks>
/// Créer une image, l'animer, l'agrandir : trois moments d'un même travail, et non trois outils
/// qu'on choisit indépendamment. Ce qui est éprouvé ici est la règle qui décide quels outils un
/// classeur prend, et dans quel ordre — le dessin, lui, ne se juge qu'à l'écran.
/// </remarks>
public class AtelierTests
{
    [Fact]
    public void AtelierIsPartOfTheVocabulary()
        => Assert.Contains(PluginActions.Atelier, PluginActions.Known);

    // Un classeur sans les outils qu'il réunit serait une fenêtre à onglets vide.
    [Fact]
    public void AWorkbenchNeedsToSayWhatItGathers()
        => Assert.True(PluginActions.NeedsTarget(PluginActions.Atelier));

    /// <summary>L'ordre de la cible est l'ordre des onglets.</summary>
    /// <remarks>
    /// C'est l'ordre dans lequel on travaille — créer, puis animer, puis agrandir — et le manifeste
    /// du classeur est le seul endroit où quelqu'un l'a décidé. Trier autrement, par nom ou par
    /// ordre de disque, perdrait cette intention sans que rien ne le signale.
    /// </remarks>
    [Fact]
    public void TheOrderOfTheTargetIsTheOrderOfTheTabs()
        => Assert.Equal(
            ["flux-texte-image", "flux-image-video", "agrandir-image"],
            PluginActions.Reunis("flux-texte-image|flux-image-video|agrandir-image"));

    [Theory]
    [InlineData("  a | b  ", new[] { "a", "b" })]
    [InlineData("a||b", new[] { "a", "b" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void ASloppyTargetIsStillRead(string? cible, string[] attendu)
        => Assert.Equal(attendu, PluginActions.Reunis(cible));

    /// <summary>Un classeur est accepté par le catalogue.</summary>
    [Fact]
    public void AWorkbenchManifestIsAccepted()
    {
        var manifeste = new PluginManifest
        {
            Id = "atelier", Name = "Atelier", Category = "tool", Version = "1.0.0",
            Licence = "Proprietary", Glyph = "EB9F", Surface = "tile",
            Does = PluginActions.Atelier, Target = "a|b",
        };

        Assert.Null(PluginCatalog.Validate(manifeste));
    }

    // Un classeur est une tuile : il n'a rien à désigner lui-même, ce sont les outils réunis qui
    // ont des panneaux.
    [Fact]
    public void AWorkbenchIsATileRatherThanAPanel()
        => Assert.False(PluginActions.NeedsPanel(PluginActions.Atelier));
}
