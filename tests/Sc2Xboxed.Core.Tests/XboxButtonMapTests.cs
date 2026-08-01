using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Output;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The Xbox360 button mapping became configurable. These lock the behaviour that shipped, so making
/// it editable cannot quietly change what an existing installation does.
/// </summary>
public class XboxButtonMapTests
{
    [Theory]
    [InlineData(SteamControllerButtons.A, Xbox360Buttons.A)]
    [InlineData(SteamControllerButtons.B, Xbox360Buttons.B)]
    [InlineData(SteamControllerButtons.X, Xbox360Buttons.X)]
    [InlineData(SteamControllerButtons.Y, Xbox360Buttons.Y)]
    [InlineData(SteamControllerButtons.LeftBumper, Xbox360Buttons.LeftShoulder)]
    [InlineData(SteamControllerButtons.RightBumper, Xbox360Buttons.RightShoulder)]
    [InlineData(SteamControllerButtons.LeftStick, Xbox360Buttons.LeftThumb)]
    [InlineData(SteamControllerButtons.RightStick, Xbox360Buttons.RightThumb)]
    [InlineData(SteamControllerButtons.Menu, Xbox360Buttons.Start)]
    [InlineData(SteamControllerButtons.View, Xbox360Buttons.Back)]
    [InlineData(SteamControllerButtons.DPadUp, Xbox360Buttons.DPadUp)]
    [InlineData(SteamControllerButtons.DPadDown, Xbox360Buttons.DPadDown)]
    [InlineData(SteamControllerButtons.DPadLeft, Xbox360Buttons.DPadLeft)]
    [InlineData(SteamControllerButtons.DPadRight, Xbox360Buttons.DPadRight)]
    [InlineData(SteamControllerButtons.L4, Xbox360Buttons.X)]
    [InlineData(SteamControllerButtons.R4, Xbox360Buttons.Y)]
    [InlineData(SteamControllerButtons.L5, Xbox360Buttons.A)]
    [InlineData(SteamControllerButtons.R5, Xbox360Buttons.B)]
    public void DefaultMapReproducesTheShippedMapping(SteamControllerButtons physical, Xbox360Buttons expected)
    {
        Assert.Equal(expected, XboxButtonMap.Default.Apply(physical));
    }

    /// <summary>Steam still reaches the game as Guide even though no profile can rebind it.</summary>
    [Fact]
    public void SteamAlwaysProducesGuide()
    {
        Assert.Equal(Xbox360Buttons.Guide, XboxButtonMap.Default.Apply(SteamControllerButtons.Steam));
    }

    /// <summary>Quick Access drives mode switching; it must never reach the game.</summary>
    [Fact]
    public void QuickAccessProducesNothing()
    {
        Assert.Equal(Xbox360Buttons.None, XboxButtonMap.Default.Apply(SteamControllerButtons.QuickAccess));
    }

    [Fact]
    public void SimultaneousPressesCombine()
    {
        var pressed = SteamControllerButtons.A | SteamControllerButtons.LeftBumper | SteamControllerButtons.DPadLeft;

        Assert.Equal(
            Xbox360Buttons.A | Xbox360Buttons.LeftShoulder | Xbox360Buttons.DPadLeft,
            XboxButtonMap.Default.Apply(pressed));
    }

    /// <summary>Two physical buttons on the same output is the point of the paddles, not a clash.</summary>
    [Fact]
    public void PaddleAndFaceButtonProduceOneOutput()
    {
        var pressed = SteamControllerButtons.A | SteamControllerButtons.L5;

        Assert.Equal(Xbox360Buttons.A, XboxButtonMap.Default.Apply(pressed));
    }

    [Fact]
    public void RoundTripsThroughItsStoredForm()
    {
        var map = XboxButtonMap.Default;
        map[SteamControllerButtons.L4] = Xbox360Buttons.LeftShoulder;
        map[SteamControllerButtons.R5] = Xbox360Buttons.None;

        var restored = XboxButtonMap.FromDictionary(map.ToDictionary());

        Assert.Equal(Xbox360Buttons.LeftShoulder, restored[SteamControllerButtons.L4]);
        Assert.Equal(Xbox360Buttons.None, restored[SteamControllerButtons.R5]);
        Assert.Equal(Xbox360Buttons.A, restored[SteamControllerButtons.A]);
    }

    /// <summary>
    /// A file written by another build, or edited by hand, must not produce dead buttons: anything
    /// unreadable falls back to the default for that button alone.
    /// </summary>
    [Fact]
    public void UnknownEntriesFallBackToTheDefault()
    {
        var stored = new Dictionary<string, string>
        {
            ["A"] = "NotAButton",
            ["L4"] = "RightShoulder",
            ["SomeButtonFromTheFuture"] = "A",
        };

        var map = XboxButtonMap.FromDictionary(stored);

        Assert.Equal(Xbox360Buttons.A, map[SteamControllerButtons.A]);
        Assert.Equal(Xbox360Buttons.RightShoulder, map[SteamControllerButtons.L4]);
    }

    [Fact]
    public void NullStoredMapGivesTheDefault()
    {
        var map = XboxButtonMap.FromDictionary(null);

        Assert.Equal(Xbox360Buttons.Start, map[SteamControllerButtons.Menu]);
    }

    /// <summary>Steam and Quick Access must stay out of the editable set.</summary>
    [Fact]
    public void EditableButtonsExcludeSteamAndQuickAccess()
    {
        Assert.DoesNotContain(SteamControllerButtons.Steam, XboxButtonMap.All);
        Assert.DoesNotContain(SteamControllerButtons.QuickAccess, XboxButtonMap.All);
        Assert.Equal(18, XboxButtonMap.All.Count());
    }
}
