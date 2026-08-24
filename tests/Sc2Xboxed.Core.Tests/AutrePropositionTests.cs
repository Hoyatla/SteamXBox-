using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Le bouton qui retire au sort le réglage le plus incompréhensible du panneau.
/// </summary>
/// <remarks>
/// <b>Ce que cinq clips ont montré.</b> À longueur et mouvement égaux, deux graines différentes
/// donnent l'une une animation intacte, l'autre un personnage dissous. C'est le levier qui agit le
/// plus sur ce qui casse réellement — bien plus que le mouvement, dont faire varier la valeur de 60
/// à 180 ne changeait presque rien.
///
/// <para>
/// Or « graine : 42 » ne veut rien dire à personne. Le besoin derrière se dit en trois mots —
/// « refais-en un autre » — et c'est un bouton, pas un champ.
/// </para>
/// </remarks>
public class AutrePropositionTests
{
    private static PluginManifest Outil(string tire) => new()
    {
        Id = "animer",
        Name = "Animer",
        Category = "tool",
        Version = "1.0.0",
        Licence = "Proprietary",
        Glyph = "E786",
        Surface = "panel",
        Content =
        [
            new PluginContentItem { Kind = "file", Id = "image", Label = "Image" },
            new PluginContentItem
            {
                Kind = "number", Id = "graine", Label = "Graine", Value = "42", Min = 0, Max = 999999,
            },
            new PluginContentItem
            {
                Kind = "action", Label = "Animer", Does = PluginActions.Flux, Target = "x|!1.image={image}",
            },
            new PluginContentItem
            {
                Kind = "action", Label = "Autre proposition", Does = PluginActions.Flux,
                Hasard = tire, Target = "x|!1.image={image}",
            },
        ],
    };

    [Fact]
    public void AButtonMayReRollASetting()
        => Assert.Null(PluginCatalog.Validate(Outil("graine")));

    /// <summary>Un tirage qui ne désigne rien est refusé au chargement.</summary>
    /// <remarks>
    /// Sans cette garde, le bouton relancerait à l'identique : « Autre proposition » rendrait deux
    /// fois la même chose, et rien ne dirait pourquoi. Un défaut silencieux, donc le pire genre.
    /// </remarks>
    [Fact]
    public void ARollThatNamesNothingIsRefused()
    {
        var faute = PluginCatalog.Validate(Outil("inexistant"));

        Assert.NotNull(faute);
        Assert.Contains("inexistant", faute, StringComparison.Ordinal);
    }

    // Un outil sans tirage reste valide : la plupart des boutons ne tirent rien.
    [Fact]
    public void AButtonWithoutARollIsOrdinary()
    {
        var manifeste = Outil("graine");
        manifeste.Content[3].Hasard = "";

        Assert.Null(PluginCatalog.Validate(manifeste));
    }
}
