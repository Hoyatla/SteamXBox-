using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The gate that stops the power-off chord from firing Menu's and View's own actions on the way.
/// Masking them once both were down was too late: the first button to land had already opened the
/// Start menu before the second arrived.
/// </summary>
public class ChordButtonGateTests
{
    private const SteamControllerButtons Chord =
        SteamControllerButtons.Menu | SteamControllerButtons.View;

    private static ChordButtonGate Gate() => new(Chord, TimeSpan.FromMilliseconds(250));

    private static DateTimeOffset At(double ms) => DateTimeOffset.UnixEpoch.AddMilliseconds(ms);

    /// <summary>The case that was broken: two fingers landing a few frames apart.</summary>
    [Fact]
    public void PartnerArrivingWithinTheGraceSuppressesBoth()
    {
        var gate = Gate();

        // Menu lands first and is withheld.
        Assert.Equal(SteamControllerButtons.None, gate.Filter(SteamControllerButtons.Menu, At(0), false));
        Assert.Equal(SteamControllerButtons.None, gate.Filter(SteamControllerButtons.Menu, At(30), false));

        // View joins; the chord is now engaged and neither is ever released.
        Assert.Equal(SteamControllerButtons.None, gate.Filter(Chord, At(60), true));
        Assert.Equal(SteamControllerButtons.None, gate.Filter(Chord, At(3000), true));
    }

    [Fact]
    public void SinglePressIsReleasedAfterTheGrace()
    {
        var gate = Gate();

        Assert.Equal(SteamControllerButtons.None, gate.Filter(SteamControllerButtons.Menu, At(0), false));
        Assert.Equal(SteamControllerButtons.None, gate.Filter(SteamControllerButtons.Menu, At(200), false));
        Assert.Equal(SteamControllerButtons.Menu, gate.Filter(SteamControllerButtons.Menu, At(250), false));
        Assert.Equal(SteamControllerButtons.Menu, gate.Filter(SteamControllerButtons.Menu, At(900), false));
    }

    /// <summary>
    /// Once a button has been handed over, a late partner must not snatch it back: the press is
    /// already in flight and retracting it would produce a stuck or stuttering key.
    /// </summary>
    [Fact]
    public void AlreadyReleasedButtonIsNotRetracted()
    {
        var gate = Gate();

        gate.Filter(SteamControllerButtons.Menu, At(0), false);
        Assert.Equal(SteamControllerButtons.Menu, gate.Filter(SteamControllerButtons.Menu, At(300), false));

        var withPartner = gate.Filter(Chord, At(400), false);
        Assert.True(withPartner.HasFlag(SteamControllerButtons.Menu));
        Assert.False(withPartner.HasFlag(SteamControllerButtons.View));
    }

    [Fact]
    public void ReleasingRearmsTheGate()
    {
        var gate = Gate();

        gate.Filter(SteamControllerButtons.Menu, At(0), false);
        Assert.Equal(SteamControllerButtons.Menu, gate.Filter(SteamControllerButtons.Menu, At(300), false));

        gate.Filter(SteamControllerButtons.None, At(400), false);

        // Withheld again on the next press.
        Assert.Equal(SteamControllerButtons.None, gate.Filter(SteamControllerButtons.Menu, At(500), false));
    }

    /// <summary>Buttons outside the chord are never touched.</summary>
    [Fact]
    public void OtherButtonsPassThroughImmediately()
    {
        var gate = Gate();
        var pressed = SteamControllerButtons.A | SteamControllerButtons.LeftBumper | SteamControllerButtons.Menu;

        var result = gate.Filter(pressed, At(0), false);

        Assert.True(result.HasFlag(SteamControllerButtons.A));
        Assert.True(result.HasFlag(SteamControllerButtons.LeftBumper));
        Assert.False(result.HasFlag(SteamControllerButtons.Menu));
    }

    /// <summary>While the chord is engaged nothing gets through, however long it is held.</summary>
    [Fact]
    public void EngagedChordSuppressesEvenPastTheGrace()
    {
        var gate = Gate();

        Assert.Equal(SteamControllerButtons.None, gate.Filter(Chord, At(0), true));
        Assert.Equal(SteamControllerButtons.None, gate.Filter(Chord, At(5000), true));
    }
}
