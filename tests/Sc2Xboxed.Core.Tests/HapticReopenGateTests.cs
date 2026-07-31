using Sc2Xboxed.Hid;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Guards the reopen cooldown in <see cref="TritonHapticSink"/>. Haptics were silently dead twice
/// because of the same arithmetic: a sentinel tick value that overflows the elapsed-time subtraction
/// into a negative number, which then always compares as "still cooling down".
/// </summary>
public class HapticReopenGateTests
{
    [Fact]
    public void NeverAttemptedIsNotCoolingDown()
    {
        Assert.False(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: false, lastOpenAttemptTick: 0, nowTick: 0));
    }

    [Fact]
    public void RecentAttemptIsCoolingDown()
    {
        Assert.True(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: true, lastOpenAttemptTick: 1_000_000, nowTick: 1_000_500));
    }

    [Fact]
    public void ElapsedCooldownAllowsAnotherAttempt()
    {
        Assert.False(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: true, lastOpenAttemptTick: 1_000_000, nowTick: 1_003_000));
    }

    /// <summary>
    /// Environment.TickCount wraps roughly every 49 days. The subtraction must stay correct across
    /// the wrap, which is why the cooldown is expressed as a difference rather than a comparison.
    /// </summary>
    [Fact]
    public void SurvivesTickCountWraparound()
    {
        Assert.True(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: true, lastOpenAttemptTick: int.MaxValue - 500, nowTick: int.MinValue + 500));
        Assert.False(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: true, lastOpenAttemptTick: int.MaxValue - 500, nowTick: int.MinValue + 3000));
    }

    /// <summary>
    /// The exact regression: Reset() used to back-date the tick to int.MinValue to mean "retry now".
    /// Reset() runs when the controller is handed to Steam, so this left haptics dead for the rest of
    /// the session once Steam had been opened.
    /// </summary>
    [Fact]
    public void MinValueSentinelWouldHaveBlockedEveryReopen()
    {
        Assert.True(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: true, lastOpenAttemptTick: int.MinValue, nowTick: 1_000_000));

        // Clearing the flag is the correct way to ask for an immediate retry.
        Assert.False(TritonHapticSink.IsCoolingDown(hasAttemptedOpen: false, lastOpenAttemptTick: int.MinValue, nowTick: 1_000_000));
    }
}
