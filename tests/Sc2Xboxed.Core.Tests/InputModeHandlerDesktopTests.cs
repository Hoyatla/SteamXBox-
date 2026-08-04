using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Runtime;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The Steam + X chord that reaches the SteamXBox tools, and the rule that it exists only in
/// Profile mode.
/// </summary>
public class InputModeHandlerDesktopTests
{
    private static InputModeHandler Handler(ControllerOutputMode mode = ControllerOutputMode.Profile)
        => new(mode, SteamControllerButtons.QuickAccess, TimeSpan.FromMilliseconds(250));

    private static ControllerState Frame(SteamControllerButtons buttons, int ms = 0)
        => new() { Buttons = buttons, Timestamp = TimeSpan.FromMilliseconds(ms) };

    /// <summary>Presses the modifier first, then Steam — the chord is read on Steam's rising edge.</summary>
    private static void PressChord(InputModeHandler handler, SteamControllerButtons modifier)
    {
        handler.Update(Frame(modifier));
        handler.Update(Frame(modifier | SteamControllerButtons.Steam, 10));
    }

    [Fact]
    public void SteamAndXAsksForTheEnvironment()
    {
        var handler = Handler();
        PressChord(handler, SteamControllerButtons.X);

        Assert.True(handler.DesktopRequested);
    }

    // The whole point of the request: in Xbox mode the pad is a gamepad and X is a game button.
    // Opening a window mid-game would be a bug, not a feature.
    [Fact]
    public void TheChordDoesNothingInXboxMode()
    {
        var handler = Handler(ControllerOutputMode.Xbox360);
        PressChord(handler, SteamControllerButtons.X);

        Assert.False(handler.DesktopRequested);
    }

    // Switching to Xbox has to unbind the tools. It does so by never raising the request, so there
    // is nothing to undo — which is what makes the switch reliable in both directions.
    [Fact]
    public void SwitchingToXboxUnbindsTheChordAndComingBackRestoresIt()
    {
        var handler = Handler();

        handler.Update(Frame(SteamControllerButtons.QuickAccess, 500));
        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);

        handler.Update(Frame(SteamControllerButtons.None, 600));
        PressChord(handler, SteamControllerButtons.X);
        Assert.False(handler.DesktopRequested);

        handler.Update(Frame(SteamControllerButtons.None, 1500));
        handler.Update(Frame(SteamControllerButtons.QuickAccess, 2000));
        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);

        handler.Update(Frame(SteamControllerButtons.None, 2100));
        PressChord(handler, SteamControllerButtons.X);
        Assert.True(handler.DesktopRequested);
    }

    // Steam alone still launches Steam: the environment joined that vocabulary, it did not take it.
    [Fact]
    public void SteamAloneStillLaunchesSteam()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Steam));

        Assert.True(handler.SteamLaunchRequested);
        Assert.False(handler.DesktopRequested);
    }

    [Fact]
    public void SteamAndYStillKillsSteam()
    {
        var handler = Handler();
        PressChord(handler, SteamControllerButtons.Y);

        Assert.True(handler.SteamKillRequested);
        Assert.False(handler.DesktopRequested);
    }

    // Releasing the modifier while Steam is still down must not then read as "Steam alone".
    [Fact]
    public void ReleasingXWhileSteamIsHeldDoesNotLaunchSteam()
    {
        var handler = Handler();
        PressChord(handler, SteamControllerButtons.X);

        handler.Update(Frame(SteamControllerButtons.Steam, 20));

        Assert.False(handler.SteamLaunchRequested);
    }

    [Fact]
    public void TheRequestLastsOneFrameOnly()
    {
        var handler = Handler();
        PressChord(handler, SteamControllerButtons.X);
        Assert.True(handler.DesktopRequested);

        handler.Update(Frame(SteamControllerButtons.X | SteamControllerButtons.Steam, 20));
        Assert.False(handler.DesktopRequested);
    }

    // Without this the chord would also fire whatever the profile binds X to — a click, a key — at
    // the very moment the environment opens.
    [Fact]
    public void BothChordButtonsAreWithheldFromTheMappers()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.X));
        var frame = Frame(SteamControllerButtons.X | SteamControllerButtons.Steam, 10);
        handler.Update(frame);

        var consumed = handler.ConsumeButton(frame);

        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.X));
        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.Steam));
    }

    [Fact]
    public void APlainXPressReachesTheMappers()
    {
        var handler = Handler();
        var frame = Frame(SteamControllerButtons.X);
        handler.Update(frame);

        Assert.True(handler.ConsumeButton(frame).Buttons.HasFlag(SteamControllerButtons.X));
    }
}
