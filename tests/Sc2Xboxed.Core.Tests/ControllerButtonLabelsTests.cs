using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// What a button is called on the controller in the user's hands.
/// </summary>
/// <remarks>
/// The internal value never changes — a circle and a B are the same button in the same place. Only
/// the printed name differs, and editing a row labelled "B" while holding a pad marked with a circle
/// forces a translation the user will eventually get wrong.
/// </remarks>
public class ControllerButtonLabelsTests
{
    [Theory]
    [InlineData(SteamControllerButtons.A, "Croix")]
    [InlineData(SteamControllerButtons.B, "Rond")]
    [InlineData(SteamControllerButtons.X, "Carré")]
    [InlineData(SteamControllerButtons.Y, "Triangle")]
    [InlineData(SteamControllerButtons.LeftBumper, "L1")]
    [InlineData(SteamControllerButtons.RightBumper, "R1")]
    [InlineData(SteamControllerButtons.Menu, "Options")]
    [InlineData(SteamControllerButtons.View, "Create")]
    public void APlayStationPadUsesItsOwnNames(SteamControllerButtons button, string expected)
        => Assert.Equal(expected, ControllerButtonLabels.For(ControllerKind.DualSense, button));

    [Theory]
    [InlineData(SteamControllerButtons.A, "A")]
    [InlineData(SteamControllerButtons.B, "B")]
    [InlineData(SteamControllerButtons.RightBumper, "RB")]
    public void AnXboxPadKeepsTheXboxNames(SteamControllerButtons button, string expected)
        => Assert.Equal(expected, ControllerButtonLabels.For(ControllerKind.XInput, button));

    // The Xbox names are the fallback rather than a table of their own.
    [Fact]
    public void AnUnrecognisedFamilyFallsBackToXboxNames()
        => Assert.Equal(
            ControllerButtonLabels.For(ControllerKind.XInput, SteamControllerButtons.B),
            ControllerButtonLabels.For(ControllerKind.SteamController, SteamControllerButtons.B));

    // Both names, because the mapping's output is an Xbox button: showing only the PlayStation one
    // would trade one translation for another.
    [Fact]
    public void TheDescriptionCarriesBothNamesWhenTheyDiffer()
        => Assert.Equal("Rond (B)", ControllerButtonLabels.Describe(ControllerKind.DualSense, SteamControllerButtons.B));

    [Fact]
    public void TheDescriptionDoesNotRepeatItselfWhenTheyMatch()
    {
        Assert.Equal("L3", ControllerButtonLabels.Describe(ControllerKind.DualSense, SteamControllerButtons.LeftStick));
        Assert.Equal("B", ControllerButtonLabels.Describe(ControllerKind.XInput, SteamControllerButtons.B));
    }

    // The correspondence the author asked to be certain of, written down so it cannot drift.
    [Fact]
    public void TheShouldersLineUpWithTheirXboxEquivalents()
    {
        Assert.Equal("R1 (RB)", ControllerButtonLabels.Describe(ControllerKind.DualSense, SteamControllerButtons.RightBumper));
        Assert.Equal("L1 (LB)", ControllerButtonLabels.Describe(ControllerKind.DualSense, SteamControllerButtons.LeftBumper));
    }

    [Fact]
    public void EveryFaceButtonHasAName()
    {
        foreach (var button in new[]
                 {
                     SteamControllerButtons.A, SteamControllerButtons.B,
                     SteamControllerButtons.X, SteamControllerButtons.Y,
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(
                ControllerButtonLabels.For(ControllerKind.DualSense, button)));
        }
    }
}
