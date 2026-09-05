using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Runtime;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Both stick clicks held together for two seconds switch between Profile and Xbox mode.
/// </summary>
/// <remarks>
/// Held, not pressed, and that is the whole point of this file. On a pad with no quick-access button
/// — a DualSense, an Xbox controller — this chord is the only way to change mode, so it has to fire
/// when it is meant to and never when it is not. L3 and R3 are ordinary game buttons and hitting
/// both at once is something a player does by accident constantly.
///
/// <para>
/// Measured on 14 August, with the hold removed and the chord firing on the rising edge: two
/// switches at 22:51:51.831 and 22:51:52.675, one second apart, while nothing was being done but
/// pushing the two sticks. The pad changed mode twice on its own.
/// </para>
/// </remarks>
public class InputModeHandlerChordTests
{
    private const SteamControllerButtons Chord =
        SteamControllerButtons.LeftStick | SteamControllerButtons.RightStick;

    /// <summary>The hold, in milliseconds, as <c>InputModeHandler</c> defines it.</summary>
    private const int HoldMs = 2000;

    private static InputModeHandler Handler(ControllerOutputMode mode = ControllerOutputMode.Profile)
        => new(mode, SteamControllerButtons.QuickAccess, TimeSpan.FromMilliseconds(250));

    private static ControllerState Frame(SteamControllerButtons buttons, int ms = 0)
        => new() { Buttons = buttons, Timestamp = TimeSpan.FromMilliseconds(ms) };

    /// <summary>Holds the chord from <paramref name="fromMs"/> to <paramref name="toMs"/> at 125 Hz.</summary>
    private static void Hold(InputModeHandler handler, int fromMs, int toMs)
    {
        for (var ms = fromMs; ms <= toMs; ms += 8)
        {
            handler.Update(Frame(Chord, ms));
        }
    }

    [Fact]
    public void HoldingBothClicksForTwoSecondsSwitchesToXbox()
    {
        var handler = Handler();
        Hold(handler, 0, HoldMs);

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }

    // The reason the hold exists. A brief simultaneous press is a game input, not a command.
    [Fact]
    public void AShortSimultaneousPressChangesNothing()
    {
        var handler = Handler();
        Hold(handler, 0, HoldMs - 100);

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // The exact case measured on 14 August: two collisions a second apart, neither long enough.
    [Fact]
    public void TwoCollisionsASecondApartChangeNothing()
    {
        var handler = Handler();

        Hold(handler, 0, 40);
        handler.Update(Frame(SteamControllerButtons.None, 100));
        Hold(handler, 900, 940);

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // Letting go restarts the clock. Two half-holds do not add up to one whole one.
    [Fact]
    public void ReleasingRestartsTheHold()
    {
        var handler = Handler();

        Hold(handler, 0, HoldMs - 200);
        handler.Update(Frame(SteamControllerButtons.None, HoldMs - 100));
        Hold(handler, HoldMs, HoldMs + 400);

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // It has to work in both directions: a controller stuck in Xbox mode with no way back would
    // need the keyboard to recover.
    [Fact]
    public void AndBackAgainFromXbox()
    {
        var handler = Handler(ControllerOutputMode.Xbox360);
        Hold(handler, 0, HoldMs);

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    [Theory]
    [InlineData(SteamControllerButtons.LeftStick)]
    [InlineData(SteamControllerButtons.RightStick)]
    public void OneClickAloneChangesNothingHoweverLong(SteamControllerButtons single)
    {
        var handler = Handler();

        for (var ms = 0; ms <= HoldMs * 2; ms += 8)
        {
            handler.Update(Frame(single, ms));
        }

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // Once per hold: keeping both clicks down must not switch again on every frame past the second
    // second.
    [Fact]
    public void HoldingPastTheSwitchSwitchesOnlyOnce()
    {
        var handler = Handler();
        Hold(handler, 0, HoldMs * 3);

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }

    [Fact]
    public void ReleasingAndHoldingAgainSwitchesBack()
    {
        var handler = Handler();

        Hold(handler, 0, HoldMs);
        handler.Update(Frame(SteamControllerButtons.None, HoldMs + 100));
        Hold(handler, HoldMs + 200, HoldMs * 2 + 200);

        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);
    }

    // Otherwise the switch also fires whatever the profile or the game binds to L3 and R3, at the
    // exact instant the mode changes.
    [Fact]
    public void BothClicksAreWithheldOnceTheModeHasChanged()
    {
        var handler = Handler();
        Hold(handler, 0, HoldMs);

        var frame = Frame(Chord, HoldMs);
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
        Hold(handler, 0, HoldMs);

        var later = Frame(Chord, HoldMs + 500);
        handler.Update(later);

        Assert.False(handler.ConsumeButton(later).Buttons.HasFlag(SteamControllerButtons.LeftStick));
    }

    // The price of a hold, and it is the right way round: for those two seconds the two clicks are
    // still an ordinary game input, so they have to reach the game. Withholding them from the first
    // frame would make every simultaneous press dead for two seconds whether or not it was meant as
    // a command.
    [Fact]
    public void TheClicksStillReachTheMappersDuringTheHold()
    {
        var handler = Handler();

        var early = Frame(Chord, 500);
        handler.Update(early);
        var consumed = handler.ConsumeButton(early);

        Assert.True(consumed.Buttons.HasFlag(SteamControllerButtons.LeftStick));
        Assert.True(consumed.Buttons.HasFlag(SteamControllerButtons.RightStick));
    }

    [Fact]
    public void ThePlainClicksReachTheMappersAgainAfterRelease()
    {
        var handler = Handler();
        Hold(handler, 0, HoldMs);
        handler.Update(Frame(SteamControllerButtons.None, HoldMs + 100));

        var single = Frame(SteamControllerButtons.LeftStick, HoldMs + 150);
        handler.Update(single);

        Assert.True(handler.ConsumeButton(single).Buttons.HasFlag(SteamControllerButtons.LeftStick));
    }

    // The quick-access button stays the everyday way on the pad that has one, and it is immediate:
    // it is a dedicated button pressed on purpose, so there is nothing to disambiguate.
    [Fact]
    public void TheQuickAccessButtonStillSwitchesImmediately()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.QuickAccess));

        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);
    }
}
