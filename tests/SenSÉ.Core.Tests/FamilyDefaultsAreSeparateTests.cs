using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Output;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Chaque famille tient ses valeurs de depart dans son propre fichier.
/// </summary>
/// <remarks>
/// Les familles PS5 et Xbox partageaient une branche — <c>DualSense or XInput</c> dans
/// <c>SenSÉProfileSettings.DefaultFor</c> — et les deux mappers d'une meme manette recevaient la
/// meme instance de <see cref="XboxButtonMap"/>. Deux materiels differents servis par un seul objet.
///
/// <para>
/// Les valeurs sont identiques aujourd'hui, ce qui rend la refusion tentante et rend ces tests
/// necessaires : ils verifient la separation, pas les valeurs.
/// </para>
/// </remarks>
public class FamilyDefaultsAreSeparateTests
{
    [Fact]
    public void ThePs5DefaultsComeFromThePs5File()
        => Assert.Equal(
            Ps5ControllerDefaults.Settings,
            SenSÉProfileSettings.DefaultFor(ControllerKind.DualSense));

    [Fact]
    public void TheXboxDefaultsComeFromTheXboxFile()
        => Assert.Equal(
            XboxControllerDefaults.Settings,
            SenSÉProfileSettings.DefaultFor(ControllerKind.XInput));

    // Chaque fichier ne decrit qu'une famille. Si l'un se met a repondre pour l'autre, la separation
    // est repartie.
    [Fact]
    public void NeitherFileAnswersForTheOtherFamily()
    {
        Assert.NotEqual(Ps5ControllerDefaults.Kind, XboxControllerDefaults.Kind);
        Assert.Equal(ControllerKind.DualSense, Ps5ControllerDefaults.Kind);
        Assert.Equal(ControllerKind.XInput, XboxControllerDefaults.Kind);
    }

    // Le Steam Controller n'est servi par aucun des deux : ses pads pilotent le curseur, donc ses
    // sticks sont libres pour les fleches. Lui appliquer un defaut PS5 ou Xbox lui retirerait ca.
    [Fact]
    public void TheSteamControllerIsServedByNeither()
    {
        var steam = SenSÉProfileSettings.DefaultFor(ControllerKind.SteamController);

        Assert.NotEqual(Ps5ControllerDefaults.Settings, steam);
        Assert.NotEqual(XboxControllerDefaults.Settings, steam);
        Assert.True(steam.HasTrackpads);
    }

    // XboxButtonMap est modifiable. Deux mappers tenant la meme instance sont deux mappers dont l'un
    // rebranche les boutons de l'autre — y compris ceux d'une manette d'une autre famille.
    [Fact]
    public void EachCallHandsOutItsOwnButtonMap()
    {
        var first = Ps5ControllerDefaults.ButtonMap;
        var second = Ps5ControllerDefaults.ButtonMap;

        Assert.NotSame(first, second);

        first[SteamControllerButtons.A] = Xbox360Buttons.Y;

        Assert.Equal(Xbox360Buttons.A, second.Apply(SteamControllerButtons.A));
    }

    [Fact]
    public void ThePs5AndXboxButtonMapsAreDistinctInstances()
        => Assert.NotSame(Ps5ControllerDefaults.ButtonMap, XboxControllerDefaults.ButtonMap);

    // Les palettes arriere sont des boutons de manette Steam. Une DualSense et une manette Xbox n'en
    // ont aucune, et une seule liste servait les trois familles : les onglets PS5 et Xbox montraient
    // quatre lignes reglables et sans effet.
    [Theory]
    [InlineData(ControllerKind.DualSense)]
    [InlineData(ControllerKind.XInput)]
    public void ThePaddlesBelongToTheSteamControllerAlone(ControllerKind kind)
    {
        foreach (var paddle in new[]
        {
            SteamControllerButtons.L4, SteamControllerButtons.R4,
            SteamControllerButtons.L5, SteamControllerButtons.R5,
        })
        {
            Assert.DoesNotContain(paddle, XboxButtonMap.AllFor(kind));
            Assert.Equal(Xbox360Buttons.None, XboxButtonMap.DefaultFor(kind)[paddle]);
        }
    }

    [Fact]
    public void TheSteamControllerKeepsItsPaddles()
    {
        Assert.Contains(SteamControllerButtons.L4, XboxButtonMap.AllFor(ControllerKind.SteamController));
        Assert.Equal(
            Xbox360Buttons.X,
            XboxButtonMap.DefaultFor(ControllerKind.SteamController)[SteamControllerButtons.L4]);
    }

    // L'inversion Menu/View a ete mesuree sur une manette Steam. Appliquee aux deux autres familles,
    // dont les etiquettes suivent deja la convention Xbox, elle croise Options et Start.
    [Theory]
    [InlineData(ControllerKind.DualSense)]
    [InlineData(ControllerKind.XInput)]
    public void OnlyTheSteamControllerCrossesMenuAndView(ControllerKind kind)
    {
        var map = XboxButtonMap.DefaultFor(kind);

        Assert.Equal(Xbox360Buttons.Start, map[SteamControllerButtons.Menu]);
        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.View]);
    }

    [Fact]
    public void TheSteamControllerKeepsItsMeasuredCrossing()
    {
        var map = XboxButtonMap.DefaultFor(ControllerKind.SteamController);

        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.Menu]);
        Assert.Equal(Xbox360Buttons.Start, map[SteamControllerButtons.View]);
    }

    // Un profil PS5 n'a rien a dire des palettes d'une manette Steam. Les ecrire quand meme mettait
    // quatre entrees mortes dans chaque fichier, que la relecture suivante prend pour des reglages.
    [Fact]
    public void APs5ProfileDoesNotStoreSteamControllerButtons()
    {
        var stored = Ps5ControllerDefaults.Settings.XboxButtons;

        Assert.DoesNotContain(nameof(SteamControllerButtons.L4), stored.Keys);
        Assert.Contains(nameof(SteamControllerButtons.A), stored.Keys);
    }
}
