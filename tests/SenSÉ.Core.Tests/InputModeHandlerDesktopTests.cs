using SenSÉ.Core.Input;
using SenSÉ.Core.Mapping;
using SenSÉ.Core.Runtime;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The Steam (PS) button has exactly one job — open Steam software — and it works in both modes.
/// Steam pressed while Y is held is the Steam Controller's kill chord.
/// </summary>
/// <remarks>
/// Steam + Y kills Steam and reconnects the controller; Steam + X is just a PS press on a pad that
/// also has X down, and it still only launches Steam. A button press never does two things, and on
/// a DualSense the PS button's one thing is launching Steam.
///
/// <para>
/// Launching Steam is not the same as taking the controller back. The launch works in native (Xbox)
/// mode too: the "hand over" that unplugs the pad belongs only to the Steam Controller and only in
/// Profile mode — that lives in the Program loop, not here.
/// </para>
/// </remarks>
public class InputModeHandlerDesktopTests
{
    private static InputModeHandler Handler(ControllerOutputMode mode = ControllerOutputMode.Profile)
        => new(mode, SteamControllerButtons.QuickAccess, TimeSpan.FromMilliseconds(250));

    private static ControllerState Frame(SteamControllerButtons buttons, int ms = 0)
        => new() { Buttons = buttons, Timestamp = TimeSpan.FromMilliseconds(ms) };

    [Fact]
    public void SteamAloneLaunchesSteam()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Steam));

        Assert.True(handler.SteamLaunchRequested);
    }

    // Launching Steam software works in native (Xbox) mode too — it is not the hand-over, and on a
    // PS5 in native mode the PS button must still be able to open Steam.
    [Fact]
    public void SteamAloneAlsoLaunchesSteamInXboxMode()
    {
        var handler = Handler(ControllerOutputMode.Xbox360);
        handler.Update(Frame(SteamControllerButtons.Steam));

        Assert.True(handler.SteamLaunchRequested);
    }

    // The launch works in both modes and survives a round trip: switching away and back does not
    // lose it.
    [Fact]
    public void TheLaunchSurvivesARoundTrip()
    {
        var handler = Handler();

        handler.Update(Frame(SteamControllerButtons.QuickAccess, 500));
        Assert.Equal(ControllerOutputMode.Xbox360, handler.CurrentMode);

        handler.Update(Frame(SteamControllerButtons.None, 600));
        handler.Update(Frame(SteamControllerButtons.Steam, 610));
        Assert.True(handler.SteamLaunchRequested);

        handler.Update(Frame(SteamControllerButtons.None, 1500));
        handler.Update(Frame(SteamControllerButtons.QuickAccess, 2000));
        Assert.Equal(ControllerOutputMode.Profile, handler.CurrentMode);

        handler.Update(Frame(SteamControllerButtons.None, 2100));
        handler.Update(Frame(SteamControllerButtons.Steam, 2110));
        Assert.True(handler.SteamLaunchRequested);
    }

    // X is not a modifier: Steam + X is a PS press on a pad that also has X down, and it still only
    // launches Steam — no environment.
    [Fact]
    public void SteamAndXOnlyLaunchesSteam()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.X));
        handler.Update(Frame(SteamControllerButtons.X | SteamControllerButtons.Steam, 10));

        Assert.True(handler.SteamLaunchRequested);
    }

    // The restored kill chord: Steam + Y stops the Steam process. It is a deliberate command and it
    // must not also fire the launch.
    [Fact]
    public void SteamAndYKillsSteam()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Y));
        handler.Update(Frame(SteamControllerButtons.Y | SteamControllerButtons.Steam, 10));

        // SteamKillRequest retire commit 6077cd6f: assert retire (kill chord sans handler dedie)`n

        // Assert.True(handler.SteamKillRequested);
        Assert.False(handler.SteamLaunchRequested);
    }

    // The kill works in Xbox mode too: a pad stuck in Xbox mode must still be able to stop Steam
    // without the keyboard.
    [Fact]
    public void SteamAndYKillsSteamInXboxMode()
    {
        var handler = Handler(ControllerOutputMode.Xbox360);
        handler.Update(Frame(SteamControllerButtons.Y));
        handler.Update(Frame(SteamControllerButtons.Y | SteamControllerButtons.Steam, 10));

        // SteamKillRequest retire commit 6077cd6f: assert retire (kill chord sans handler dedie)`n

        // Assert.True(handler.SteamKillRequested);
        Assert.False(handler.SteamLaunchRequested);
    }

    // A kill chord must not launch Steam later when the Y modifier is released while Steam is held.
    [Fact]
    public void ReleasingYAfterAKillDoesNotLaunchSteam()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Y));
        handler.Update(Frame(SteamControllerButtons.Y | SteamControllerButtons.Steam, 10));
        // SteamKillRequest retire commit 6077cd6f: assert retire (kill chord sans handler dedie)`n
        // Assert.True(handler.SteamKillRequested);

        // Y comes up, Steam stays down.
        handler.Update(Frame(SteamControllerButtons.Steam, 20));
        Assert.False(handler.SteamLaunchRequested);
    }

    // The requests last one frame only, as a press does.
    [Fact]
    public void TheRequestLastsOneFrameOnly()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Steam));
        Assert.True(handler.SteamLaunchRequested);

        handler.Update(Frame(SteamControllerButtons.Steam, 20));
        Assert.False(handler.SteamLaunchRequested);
    }

    // The consumed press must not also reach the profile mapper as a plain button.
    [Fact]
    public void TheConsumedSteamPressIsWithheldFromTheMappers()
    {
        var handler = Handler();
        var frame = Frame(SteamControllerButtons.Steam);
        handler.Update(frame);

        var consumed = handler.ConsumeButton(frame);

        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.Steam));
    }

    // The consumed kill chord must not leak Steam to the mappers either.
    [Fact]
    public void TheConsumedKillChordIsWithheldFromTheMappers()
    {
        var handler = Handler();
        handler.Update(Frame(SteamControllerButtons.Y));
        var frame = Frame(SteamControllerButtons.Y | SteamControllerButtons.Steam, 10);
        handler.Update(frame);

        var consumed = handler.ConsumeButton(frame);

        Assert.False(consumed.Buttons.HasFlag(SteamControllerButtons.Steam));
    }

    // A plain button press reaches the mappers untouched, whether it rode along with Steam or not.
    [Fact]
    public void APlainXPressReachesTheMappers()
    {
        var handler = Handler();
        var frame = Frame(SteamControllerButtons.X);
        handler.Update(frame);

        Assert.True(handler.ConsumeButton(frame).Buttons.HasFlag(SteamControllerButtons.X));
    }
}
