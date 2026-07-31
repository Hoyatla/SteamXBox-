using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Core.Tests;

public sealed class SteamPresenceWatcherTests
{
    private static readonly TimeSpan ReclaimGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(25);
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Poll interval zero disables throttling, which is driven by <see cref="Environment.TickCount"/>
    /// and therefore not controllable from a test.
    /// </summary>
    private static SteamPresenceWatcher CreateWatcher(Func<bool> isSteamRunning) =>
        new(pollInterval: TimeSpan.Zero,
            reclaimGrace: ReclaimGrace,
            launchGrace: LaunchGrace,
            isSteamRunning: isSteamRunning);

    [Fact]
    public void SteamXBoxOwnsTheControllerWhileSteamIsAbsent()
    {
        var watcher = CreateWatcher(() => false);

        var changed = watcher.Poll(Origin);

        Assert.False(changed);
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }

    [Fact]
    public void SteamTakesOwnershipAsSoonAsItsProcessAppears()
    {
        var running = false;
        var watcher = CreateWatcher(() => running);

        watcher.Poll(Origin);
        running = true;
        var changed = watcher.Poll(Origin + TimeSpan.FromSeconds(1));

        Assert.True(changed);
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);
    }

    [Fact]
    public void OwnershipIsNotReclaimedBeforeTheGraceElapses()
    {
        var running = true;
        var watcher = CreateWatcher(() => running);

        watcher.Poll(Origin);
        running = false;
        watcher.Poll(Origin + TimeSpan.FromSeconds(1));
        var changed = watcher.Poll(Origin + TimeSpan.FromSeconds(3));

        Assert.False(changed);
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);
    }

    [Fact]
    public void OwnershipIsReclaimedOnceTheGraceElapses()
    {
        var running = true;
        var watcher = CreateWatcher(() => running);

        watcher.Poll(Origin);
        running = false;
        watcher.Poll(Origin + TimeSpan.FromSeconds(1));
        var changed = watcher.Poll(Origin + TimeSpan.FromSeconds(5));

        Assert.True(changed);
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }

    [Fact]
    public void SteamRestartingWithinTheGraceKeepsOwnership()
    {
        var running = true;
        var watcher = CreateWatcher(() => running);

        watcher.Poll(Origin);

        // Steam disappears and comes back, as it does while updating itself.
        running = false;
        watcher.Poll(Origin + TimeSpan.FromSeconds(1));
        running = true;
        watcher.Poll(Origin + TimeSpan.FromSeconds(2));

        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        // The absence timer must have been cleared, so a later exit gets a full grace period.
        running = false;
        watcher.Poll(Origin + TimeSpan.FromSeconds(10));
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        var changed = watcher.Poll(Origin + TimeSpan.FromSeconds(14));
        Assert.True(changed);
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }

    [Fact]
    public void ARequestedLaunchGetsTheLongerGraceBeforeReclaiming()
    {
        var watcher = CreateWatcher(() => false);

        watcher.HandOverToSteam(Origin);
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        // Steam is slow to appear: the short reclaim grace must not apply yet.
        watcher.Poll(Origin + TimeSpan.FromSeconds(5));
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        var changed = watcher.Poll(Origin + TimeSpan.FromSeconds(30));
        Assert.True(changed);
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }

    [Fact]
    public void ASlowSteamLaunchKeepsOwnershipOnceObserved()
    {
        var running = false;
        var watcher = CreateWatcher(() => running);

        watcher.HandOverToSteam(Origin);
        running = true;
        watcher.Poll(Origin + TimeSpan.FromSeconds(20));

        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        // Now that Steam has been observed, the short grace applies again.
        running = false;
        watcher.Poll(Origin + TimeSpan.FromSeconds(21));
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        watcher.Poll(Origin + TimeSpan.FromSeconds(25));
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }

    [Fact]
    public void TakingOwnershipSkipsTheGraceEntirely()
    {
        var running = true;
        var watcher = CreateWatcher(() => running);

        watcher.Poll(Origin);
        Assert.Equal(ControllerOwner.Steam, watcher.Owner);

        // SteamXBox killed Steam itself, so there is nothing to wait for.
        running = false;
        watcher.TakeOwnership();

        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
        Assert.False(watcher.Poll(Origin + TimeSpan.FromSeconds(1)));
        Assert.Equal(ControllerOwner.SteamXBox, watcher.Owner);
    }
}
