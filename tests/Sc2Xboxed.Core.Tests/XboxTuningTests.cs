using Sc2Xboxed.Core.Mapping;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The Xbox tab's sticks, triggers and vibration controls existed for a long time without doing
/// anything. These assert that each one now has a real, bounded effect — and that the defaults
/// reproduce the previous behaviour, so wiring them up changes nothing on its own.
/// </summary>
public class XboxTuningTests
{
    [Fact]
    public void DefaultsPassSticksAndTriggersThroughUnchanged()
    {
        var tuning = new XboxTuning { StickDeadZone = 0.0 };

        var (x, y) = tuning.ApplyStick(0.5, 0.0);
        Assert.Equal(0.5, x, 3);
        Assert.Equal(0.0, y, 3);

        Assert.Equal(0.42, tuning.ApplyTrigger(0.42), 3);
        Assert.Equal(1.0, tuning.ApplyVibration(1.0), 3);
    }

    /// <summary>
    /// Radial, not per-axis. A per-axis dead zone leaves a square hole in a round stick, which makes
    /// diagonals feel notched: a diagonal shorter than the dead zone must read as centre.
    /// </summary>
    [Fact]
    public void StickDeadZoneIsRadial()
    {
        var tuning = new XboxTuning { StickDeadZone = 0.2 };

        // Magnitude 0.14, under the dead zone, even though each axis alone is under it too.
        var (x, y) = tuning.ApplyStick(0.1, 0.1);
        Assert.Equal(0.0, x, 3);
        Assert.Equal(0.0, y, 3);
    }

    /// <summary>Movement past the dead zone starts from zero rather than jumping to its raw value.</summary>
    [Fact]
    public void StickOutputIsRescaledPastTheDeadZone()
    {
        var tuning = new XboxTuning { StickDeadZone = 0.25 };

        var (justPast, _) = tuning.ApplyStick(0.26, 0.0);
        Assert.True(justPast < 0.05, $"jumped to {justPast}");

        var (full, _) = tuning.ApplyStick(1.0, 0.0);
        Assert.Equal(1.0, full, 3);
    }

    [Fact]
    public void StickCurveBelowOneFavoursFineAim()
    {
        var linear = new XboxTuning { StickDeadZone = 0.0, StickCurve = 1.0 };
        var fine = new XboxTuning { StickDeadZone = 0.0, StickCurve = 2.0 };

        var (linearX, _) = linear.ApplyStick(0.5, 0.0);
        var (fineX, _) = fine.ApplyStick(0.5, 0.0);

        Assert.True(fineX < linearX, $"curve 2.0 gave {fineX}, not less than {linearX}");
    }

    [Fact]
    public void StickSensitivityCannotExceedFullDeflection()
    {
        var tuning = new XboxTuning { StickDeadZone = 0.0, StickSensitivity = 3.0 };

        var (x, y) = tuning.ApplyStick(0.8, 0.0);

        Assert.Equal(1.0, x, 3);
        Assert.Equal(0.0, y, 3);
    }

    [Fact]
    public void TriggerThresholdSuppressesRestingTravel()
    {
        var tuning = new XboxTuning { TriggerThreshold = 0.2 };

        Assert.Equal(0.0, tuning.ApplyTrigger(0.15), 3);
        Assert.Equal(0.0, tuning.ApplyTrigger(0.2), 3);
        Assert.True(tuning.ApplyTrigger(0.3) > 0.0);
    }

    /// <summary>A hair trigger: full output before the trigger bottoms out.</summary>
    [Fact]
    public void TriggerFullPointShortensTheThrow()
    {
        var tuning = new XboxTuning { TriggerFullPoint = 0.5 };

        Assert.Equal(1.0, tuning.ApplyTrigger(0.5), 3);
        Assert.Equal(1.0, tuning.ApplyTrigger(1.0), 3);
        Assert.Equal(0.5, tuning.ApplyTrigger(0.25), 3);
    }

    /// <summary>
    /// A profile could hold a full point at or below the threshold, which would divide by zero or
    /// invert the trigger. The clamp has to hold whatever the file says.
    /// </summary>
    [Theory]
    [InlineData(0.8, 0.1)]
    [InlineData(0.5, 0.5)]
    [InlineData(-1.0, 5.0)]
    public void InvertedTriggerRangeStaysUsable(double threshold, double fullPoint)
    {
        var tuning = new XboxTuning { TriggerThreshold = threshold, TriggerFullPoint = fullPoint };

        foreach (var input in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var result = tuning.ApplyTrigger(input);
            Assert.True(double.IsFinite(result), $"{input} gave {result}");
            Assert.InRange(result, 0.0, 1.0);
        }
    }

    [Fact]
    public void VibrationCanBeSilencedAndScaled()
    {
        Assert.Equal(0.0, new XboxTuning { VibrationEnabled = false }.ApplyVibration(1.0), 3);
        Assert.Equal(0.5, new XboxTuning { VibrationIntensity = 0.5 }.ApplyVibration(1.0), 3);
        Assert.Equal(0.0, new XboxTuning { VibrationIntensity = 0.0 }.ApplyVibration(1.0), 3);
    }

    /// <summary>Trigger haptics stay off until someone proves the hardware has them.</summary>
    [Fact]
    public void TriggerHapticsAreOffByDefault()
    {
        Assert.False(new XboxTuning().TriggerHapticsEnabled);
    }

    /// <summary>
    /// Pad forwarding is off by default too. The runtime only ever drove the grip motors, and
    /// turning this on by default would have made an upgrade change how every game feels.
    /// </summary>
    [Fact]
    public void PadForwardingIsOffByDefault()
    {
        Assert.False(new XboxTuning().HapticForwarding);
    }
}
