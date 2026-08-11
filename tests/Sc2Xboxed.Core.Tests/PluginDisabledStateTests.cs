using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// What the user decided about each tool, which is the one part of the uninstall screen that
/// outlives a session.
/// </summary>
/// <remarks>
/// Kept in the host's own storage rather than inside a tool's folder, because a folder has to stay
/// exactly what was dropped in — otherwise throwing it away no longer leaves nothing behind.
///
/// <para>
/// Every test uses an identifier of its own. They share one file on this machine and xUnit runs
/// them at the same time, so a shared name made them fail each other intermittently — which is
/// exactly the kind of test that gets ignored instead of fixed.
/// </para>
/// </remarks>
public class PluginDisabledStateTests
{
    private static string Fresh(string what) => $"test-steamxbox-{what}-{Guid.NewGuid():N}";

    // Three states, not two: on, off, and never decided. Only the third takes the manifest's word.
    [Fact]
    public void AToolNobodyTouchedTakesItsOwnDefault()
    {
        var id = Fresh("untouched");

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: true));
        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: false));
    }

    // The reason the third state exists: a tool shipped idle has to be switchable on, and stay on.
    [Fact]
    public void AToolShippedIdleStaysOnOnceSwitchedOn()
    {
        var id = Fresh("idle");

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: false));

        PluginLifecycle.SetEnabled(id, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: false));
    }

    [Fact]
    public void SwitchingOffIsRemembered()
    {
        var id = Fresh("off");

        PluginLifecycle.SetEnabled(id, enabled: false);

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: true));
    }

    [Fact]
    public void SwitchingBackOnIsRememberedToo()
    {
        var id = Fresh("back-on");

        PluginLifecycle.SetEnabled(id, enabled: false);
        PluginLifecycle.SetEnabled(id, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: true));
    }

    // Switching one must not switch the others.
    [Fact]
    public void EachToolIsSwitchedIndependently()
    {
        var one = Fresh("one");
        var two = Fresh("two");

        PluginLifecycle.SetEnabled(one, enabled: false);
        PluginLifecycle.SetEnabled(two, enabled: false);
        PluginLifecycle.SetEnabled(one, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(one, byDefault: true));
        Assert.False(PluginLifecycle.IsEnabled(two, byDefault: true));
    }

    [Fact]
    public void TheNameIsMatchedWithoutRegardToCase()
    {
        var id = Fresh("case");

        PluginLifecycle.SetEnabled(id.ToUpperInvariant(), enabled: false);

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: true));
    }
}
