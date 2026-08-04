using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Runtime;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Both stick clicks held together switch between Profile and Xbox mode.
/// </summary>
public class InputModeHandlerChordTests
{
    private const SteamControllerButtons Chord =
        SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick;

    private static InputModeHandler Handler(ControllerOutputMode mode = ControllerOutputMode.Profile)
        => new(mode, SteamControllerButtons.QuickAccess, TimeSpan.FromMilliseconds(250));

    private static ControllerState Frame(SteamControllerButtons buttons, int ms = 0)
        => new() { Buttons = buttons, Timestamp = TimeSpan.FromMilliseconds(ms) };

    [Fact]
    public void BothClicksTogetherSwitchToXbox()
    {
        var handler = Handler();
        handler.Update(Frame(Chord));

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }

    // It has to work in both directions: a controller stuck in Xbox mode with no way back would
    // need the keyboard to recover.
    [Fact]
    public void AndBackAgainFromXbox()
    {
        var handler = Handler(ControllerOutputMode.Xbox360);
        handler.Update(Frame(Chord));

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    [Theory]
    [InlineData(SteamControllerButtons.LeftStick)]
    [InlineData(SteamControllerButtons.RightStick)]
    public void OneClickAloneChangesNothing(SteamControllerButtons single)
    {
        var handler = Handler();
        handler.Update(Frame(single));

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // Held, not tapped: the chord must not switch again on every frame it stays down.
    [Fact]
    public void HoldingTheChordSwitchesOnlyOnce()
    {
        var handler = Handler();

        for (var i = 0; i < 40; i++)
        {
            handler.Update(Frame(Chord, i * 8));
        }

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }

    [Fact]
    public void ReleasingAndPressingAgainSwitchesBack()
    {
        var handler = Handler();

        handler.Update(Frame(Chord, 0));
        handler.Update(Frame(SteamControllerButtons.None, 100));
        handler.Update(Frame(Chord, 400));

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // Otherwise the switch also fires whatever the profile or the game binds to L3 and R3, at the
    // exact instant the mode changes.
    [Fact]
    public void BothClicksAreWithheldFromTheMappers()
    {
        var handler = Handler();
        var frame = Frame(Chord);
        handler.Update(frame);

        var consumed = handler.ConsumeButton(frame);

        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.LeftStick));
        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.RightStick));
    }

    // A chord is held for a moment, not for eight milliseconds. Withholding only on the switching
    // frame would leak the clicks to the game for every frame after it.
    [Fact]
    public void TheyStayWithheldWhileTheChordIsStillHeld()
    {
        var handler = Handler();
        handler.Update(Frame(Chord, 0));

        var later = Frame(Chord, 200);
        handler.Update(later);

        Assert.False(handler.ConsumeButton(later).Buttons.HasFlag(SteamControllerButtons.LeftStick));
    }

    [Fact]
    public void ThePlainClicksReachTheMappersAgainAfterRelease()
    {
        var handler = Handler();
        handler.Update(Frame(Chord, 0));
        handler.Update(Frame(SteamControllerButtons.None, 100));

        var single = Frame(SteamControllerButtons.LeftStick, 150);
        handler.Update(single);

        Assert.True(handler.ConsumeButton(single).Buttons.HasFlag(SteamControllerButtons.LeftStick));
    }

    // The quick-access button stays the everyday way; the chord joins it rather than replacing it.
    [Fact]
    public void TheQuickAccessButtonStillSwitches()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.QuickAccess));

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }
}
