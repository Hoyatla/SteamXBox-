using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Core.Tests;

public sealed class SteamButtonModeSwitcherTests
{
    [Fact]
    public void SteamButtonRisingEdgeTogglesMode()
    {
        var switcher = new SteamButtonModeSwitcher(
            ControllerOutputMode.Xbox360,
            SteamControllerButtons.Steam,
            TimeSpan.FromMilliseconds(350));

        var changed = switcher.Update(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.Steam
        });

        Assert.True(changed);
        Assert.Equal(ControllerOutputMode.KeyboardMouse, switcher.CurrentMode);
    }

    [Fact]
    public void HeldSteamButtonDoesNotToggleRepeatedly()
    {
        var switcher = new SteamButtonModeSwitcher(
            ControllerOutputMode.Xbox360,
            SteamControllerButtons.Steam,
            TimeSpan.FromMilliseconds(350));

        switcher.Update(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.Steam
        });

        var changed = switcher.Update(SteamControllerState.Empty(TimeSpan.FromMilliseconds(500)) with
        {
            Buttons = SteamControllerButtons.Steam
        });

        Assert.False(changed);
        Assert.Equal(ControllerOutputMode.KeyboardMouse, switcher.CurrentMode);
    }

    [Fact]
    public void SecondPressAfterReleaseTogglesBack()
    {
        var switcher = new SteamButtonModeSwitcher(
            ControllerOutputMode.Xbox360,
            SteamControllerButtons.Steam,
            TimeSpan.FromMilliseconds(350));

        switcher.Update(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.Steam
        });
        switcher.Update(SteamControllerState.Empty(TimeSpan.FromMilliseconds(100)) with
        {
            Buttons = SteamControllerButtons.None
        });

        var changed = switcher.Update(SteamControllerState.Empty(TimeSpan.FromMilliseconds(500)) with
        {
            Buttons = SteamControllerButtons.Steam
        });

        Assert.True(changed);
        Assert.Equal(ControllerOutputMode.Xbox360, switcher.CurrentMode);
    }

    [Fact]
    public void AlternateSwitchButtonCanToggleMode()
    {
        var switcher = new SteamButtonModeSwitcher(
            ControllerOutputMode.Xbox360,
            SteamControllerButtons.QuickAccess,
            TimeSpan.FromMilliseconds(350));

        var changed = switcher.Update(SteamControllerState.Empty(TimeSpan.Zero) with
        {
            Buttons = SteamControllerButtons.QuickAccess
        });

        Assert.True(changed);
        Assert.Equal(ControllerOutputMode.KeyboardMouse, switcher.CurrentMode);
    }
}
