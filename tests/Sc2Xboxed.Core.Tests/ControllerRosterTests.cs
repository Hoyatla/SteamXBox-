using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The list of controllers, every one of them an input.
/// </summary>
/// <remarks>
/// Nothing here ranks or filters. Picking one controller by a fixed rule and ignoring the rest is
/// what made split-screen impossible: two players need two physical pads feeding two distinct
/// virtual ones.
/// </remarks>
public class ControllerRosterTests
{
    // The three HID collections one Bluetooth Steam Controller exposes.
    private const string Col01 = @"\\?\hid#..._vid&0228de_pid&1303_rev&0100_d1af66b5aa8d&col01#9&1#{guid}";
    private const string Col02 = @"\\?\hid#..._vid&0228de_pid&1303_rev&0100_d1af66b5aa8d&col02#9&1#{guid}";
    private const string Col03 = @"\\?\hid#..._vid&0228de_pid&1303_rev&0100_d1af66b5aa8d&col03#9&1#{guid}";

    private const string OtherPad = @"\\?\hid#..._vid&0228de_pid&1303_rev&0100_aabbccddeeff&col03#9&1#{guid}";

    // Left as they come, one pad appears three times and is given three profiles.
    [Fact]
    public void TheCollectionsOfOneControllerCollapseIntoOneEntry()
    {
        var roster = ControllerRoster.Build([Col01, Col02, Col03], []);

        Assert.Single(roster);
        Assert.Equal(ControllerKind.SteamController, roster[0].Kind);
    }

    [Fact]
    public void TwoSteamControllersAreTwoEntries()
        => Assert.Equal(2, ControllerRoster.Build([Col03, OtherPad], []).Count);

    // The point of the whole change: both kinds are inputs at the same time, neither excludes the
    // other.
    [Fact]
    public void SteamAndXboxControllersCoexist()
    {
        var roster = ControllerRoster.Build([Col03], [0, 1]);

        Assert.Equal(3, roster.Count);
        Assert.Single(roster, c => c.Kind == ControllerKind.SteamController);
        Assert.Equal(2, roster.Count(c => c.Kind == ControllerKind.XInput));
    }

    [Fact]
    public void EachXInputSlotIsItsOwnEntry()
    {
        var roster = ControllerRoster.Build([], [0, 2, 3]);

        Assert.Equal([0, 2, 3], roster.Select(c => c.Slot));
    }

    [Fact]
    public void ARepeatedSlotIsCountedOnce()
        => Assert.Single(ControllerRoster.Build([], [1, 1, 1]));

    // The roster feeds a list the user clicks on. One that reshuffles between two refreshes is one
    // nobody can point at.
    [Fact]
    public void TheOrderIsStableAcrossRefreshes()
    {
        var first = ControllerRoster.Build([Col03, OtherPad], [1, 0]);
        var second = ControllerRoster.Build([OtherPad, Col03], [0, 1]);

        Assert.Equal(first.Select(c => c.Id), second.Select(c => c.Id));
    }

    [Fact]
    public void EveryEntryHasAnIdAndAName()
    {
        foreach (var controller in ControllerRoster.Build([Col03], [0]))
        {
            Assert.False(string.IsNullOrWhiteSpace(controller.Id));
            Assert.False(string.IsNullOrWhiteSpace(controller.DisplayName));
        }
    }

    [Fact]
    public void NoControllersGivesAnEmptyRoster()
        => Assert.Empty(ControllerRoster.Build([], []));

    // Rebuilding everything on every refresh would drop the virtual pads of players who never
    // touched anything — losing a player mid-match because someone else's controller slept.
    [Fact]
    public void TheDiffReportsOnlyWhatChanged()
    {
        var before = ControllerRoster.Build([Col03], [0]);
        var after = ControllerRoster.Build([Col03], [0, 1]);

        var (added, removed) = ControllerRoster.Diff(before, after);

        Assert.Single(added);
        Assert.Equal(1, added[0].Slot);
        Assert.Empty(removed);
    }

    [Fact]
    public void TheDiffReportsADeparture()
    {
        var before = ControllerRoster.Build([Col03], [0]);
        var after = ControllerRoster.Build([Col03], []);

        var (added, removed) = ControllerRoster.Diff(before, after);

        Assert.Empty(added);
        Assert.Single(removed);
        Assert.Equal(ControllerKind.XInput, removed[0].Kind);
    }

    [Fact]
    public void AnUnchangedRosterProducesNoChanges()
    {
        var roster = ControllerRoster.Build([Col03], [0]);
        var (added, removed) = ControllerRoster.Diff(roster, roster);

        Assert.Empty(added);
        Assert.Empty(removed);
    }
}
