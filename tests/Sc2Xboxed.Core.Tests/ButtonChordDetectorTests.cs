using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The power-off chord. A false positive turns the controller off in the middle of a game, so these
/// lean hard on the cases that must NOT fire.
/// </summary>
public class ButtonChordDetectorTests
{
    private const SteamControllerButtons PowerOff =
        SteamControllerButtons.Menu | SteamControllerButtons.View;

    private static ButtonChordDetector Detector() => new(PowerOff, TimeSpan.FromSeconds(3));

    private static DateTimeOffset At(double seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    [Fact]
    public void FiresAfterTheFullHold()
    {
        var detector = Detector();

        Assert.False(detector.Update(PowerOff, At(0)));
        Assert.False(detector.Update(PowerOff, At(2.9)));
        Assert.True(detector.Update(PowerOff, At(3.0)));
    }

    [Fact]
    public void DoesNotFireBeforeTheHoldElapses()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));

        Assert.False(detector.Update(PowerOff, At(2.99)));
    }

    /// <summary>Releasing one button restarts the hold from zero.</summary>
    [Fact]
    public void ReleasingOneButtonRestartsTheHold()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));
        detector.Update(PowerOff, At(2.5));
        detector.Update(SteamControllerButtons.Menu, At(2.6));

        Assert.False(detector.Update(PowerOff, At(2.7)));
        Assert.False(detector.Update(PowerOff, At(5.0)));
        Assert.True(detector.Update(PowerOff, At(5.7)));
    }

    /// <summary>
    /// The buttons must be down together. Pressing them one after another over several seconds must
    /// not accumulate into a trigger.
    /// </summary>
    [Fact]
    public void SequentialPressesDoNotAccumulate()
    {
        var detector = Detector();

        for (var t = 0.0; t < 10.0; t += 0.5)
        {
            Assert.False(detector.Update(SteamControllerButtons.Menu, At(t)));
            Assert.False(detector.Update(SteamControllerButtons.View, At(t + 0.25)));
        }
    }

    [Fact]
    public void FiresOnlyOncePerHold()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));
        Assert.True(detector.Update(PowerOff, At(3.0)));
        Assert.False(detector.Update(PowerOff, At(4.0)));
        Assert.False(detector.Update(PowerOff, At(20.0)));
    }

    /// <summary>Rearming needs a full release, not merely dropping to one button.</summary>
    [Fact]
    public void RearmsOnlyAfterEveryButtonIsReleased()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));
        Assert.True(detector.Update(PowerOff, At(3.0)));

        detector.Update(SteamControllerButtons.Menu, At(3.5));
        detector.Update(PowerOff, At(4.0));
        Assert.False(detector.Update(PowerOff, At(7.5)));

        detector.Update(SteamControllerButtons.None, At(8.0));
        detector.Update(PowerOff, At(8.5));
        Assert.True(detector.Update(PowerOff, At(11.5)));
    }

    /// <summary>
    /// After the controller is switched off, no frames arrive, so the detector never sees the
    /// release that would rearm it. Without an explicit reset it stays latched and the chord can
    /// never fire again — which is exactly what happened once powering off stopped exiting.
    /// </summary>
    [Fact]
    public void ResetRearmsAfterAFiredChord()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));
        Assert.True(detector.Update(PowerOff, At(3.0)));

        // The controller goes away: the chord is never seen released.
        detector.Reset();

        detector.Update(PowerOff, At(100.0));
        Assert.True(detector.Update(PowerOff, At(103.0)));
    }

    /// <summary>Extra buttons held at the same time do not block the chord.</summary>
    [Fact]
    public void OtherButtonsDoNotPreventTheChord()
    {
        var detector = Detector();
        var withExtras = PowerOff | SteamControllerButtons.A | SteamControllerButtons.LeftBumper;

        detector.Update(withExtras, At(0));

        Assert.True(detector.Update(withExtras, At(3.0)));
    }

    [Fact]
    public void ReportsHowLongTheChordHasBeenHeld()
    {
        var detector = Detector();

        detector.Update(PowerOff, At(0));
        detector.Update(PowerOff, At(1.5));
        Assert.Equal(1.5, detector.HeldFor.TotalSeconds, 3);

        detector.Update(SteamControllerButtons.None, At(1.6));
        Assert.Equal(TimeSpan.Zero, detector.HeldFor);
    }
}
