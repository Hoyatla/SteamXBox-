using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Output;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The one place the default mapping differs by controller, and the one that kept being reported as
/// buttons swapping between pads.
/// </summary>
public class XboxButtonMapKindTests
{
    // Measured on the hardware: a Steam Controller's two centre buttons produce Back and Start the
    // other way round from what their labels suggest.
    [Fact]
    public void ASteamControllerKeepsItsMeasuredCentreButtons()
    {
        var map = XboxButtonMap.DefaultFor(ControllerKind.SteamController);

        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.Menu]);
        Assert.Equal(Xbox360Buttons.Start, map[SteamControllerButtons.View]);
    }

    // Every other pad already labels them the Xbox way, so the quirk must not follow them.
    [Theory]
    [InlineData(ControllerKind.DualSense)]
    [InlineData(ControllerKind.XInput)]
    public void EveryOtherControllerGetsTheStraightMapping(ControllerKind kind)
    {
        var map = XboxButtonMap.DefaultFor(kind);

        Assert.Equal(Xbox360Buttons.Start, map[SteamControllerButtons.Menu]);
        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.View]);
    }

    [Theory]
    [InlineData(ControllerKind.SteamController)]
    [InlineData(ControllerKind.DualSense)]
    [InlineData(ControllerKind.XInput)]
    public void NothingElseDiffersByController(ControllerKind kind)
    {
        var map = XboxButtonMap.DefaultFor(kind);

        Assert.Equal(Xbox360Buttons.A, map[SteamControllerButtons.A]);
        Assert.Equal(Xbox360Buttons.B, map[SteamControllerButtons.B]);
        Assert.Equal(Xbox360Buttons.X, map[SteamControllerButtons.X]);
        Assert.Equal(Xbox360Buttons.Y, map[SteamControllerButtons.Y]);
        Assert.Equal(Xbox360Buttons.LeftShoulder, map[SteamControllerButtons.LeftBumper]);
        Assert.Equal(Xbox360Buttons.DPadUp, map[SteamControllerButtons.DPadUp]);
    }

    // A stored profile still wins: the default is only what a controller starts with.
    [Fact]
    public void AStoredValueOverridesTheDefaultForItsKind()
    {
        var stored = new Dictionary<string, string>
        {
            [nameof(SteamControllerButtons.Menu)] = nameof(Xbox360Buttons.Back),
        };

        var map = XboxButtonMap.FromDictionary(stored, ControllerKind.DualSense);

        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.Menu]);

        // And what the profile does not mention comes from that kind's default, not another's.
        Assert.Equal(Xbox360Buttons.Back, map[SteamControllerButtons.View]);
    }
}
