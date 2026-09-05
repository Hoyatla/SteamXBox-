using SenSÉ.Core.Diagnostics;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The counter line has to be loudest about the second where nothing happened.
/// </summary>
/// <remarks>
/// An instrument that reports activity is quiet exactly when the pipeline stops, which is when it
/// is needed. On 11 August a pointer that had stopped responding produced seconds that were not
/// written down at all, because a finger on the pad with no pointer output did not count as
/// activity. These tests hold that door open.
/// </remarks>
public class RuntimeCounterVisibilityTests
{
	private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

	/// <summary>
	/// A finger laid still on the pad is not a fault.
	/// </summary>
	/// <remarks>
	/// The first version of this counter called it one. "Touched" is not "moving": a resting thumb
	/// is touched on every frame and must move nothing, and reading that as a broken pointer
	/// produced twenty-one imaginary defects in a single session on 12 August. The second is still
	/// worth writing down — the pad was in use — but it is not deaf.
	/// </remarks>
	[Fact]
	public void ARestingFingerIsNotAFault()
	{
		var counters = new RuntimeCounters();

		for (var frame = 0; frame < 60; frame++)
		{
			counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
			counters.PadAt("pad-a/r", touched: true, 0.5, 0.5);
		}

		Assert.True(counters.HasActivity);
		Assert.False(counters.PadIsDeaf);
		Assert.DoesNotContain("PAD SOURD", counters.DrainToLine(OneSecond, "Profile", "SenSÉ"));
	}

	/// <summary>
	/// A finger that travels and moves no pointer is the fault this counter exists for.
	/// </summary>
	[Fact]
	public void AFingerThatTravelsAndMovesNothingIsDeaf()
	{
		var counters = new RuntimeCounters();

		for (var frame = 0; frame < 60; frame++)
		{
			counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
			counters.PadAt("pad-a/r", touched: true, 0.01 * frame, 0.0);
		}

		Assert.True(counters.PadIsDeaf);

		var line = counters.DrainToLine(OneSecond, "Profile", "SenSÉ");
		Assert.Contains("PAD SOURD", line);
		Assert.Contains("travel=", line);
	}

	/// <summary>
	/// Lifting the finger does not count the jump to where the next one lands.
	/// </summary>
	/// <remarks>
	/// Without forgetting the position on release, one stroke ending top-left and the next starting
	/// bottom-right would register the whole diagonal as movement nobody made — and every lift would
	/// manufacture the very fault this is meant to detect.
	/// </remarks>
	[Fact]
	public void LiftingTheFingerDoesNotInventTravel()
	{
		var counters = new RuntimeCounters();

		counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
		counters.PadAt("pad-a/r", touched: true, -1.0, -1.0);

		counters.Frame(false, false, "pad-a");
		counters.PadAt("pad-a/r", touched: false, 0, 0);

		counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
		counters.PadAt("pad-a/r", touched: true, 1.0, 1.0);

		Assert.False(counters.PadIsDeaf);
	}

	/// <summary>
	/// The same silence, but deliberate, reads differently.
	/// </summary>
	/// <remarks>
	/// The overlay owning the pad and the pointer path being broken produce identical output — no
	/// motion — and need opposite fixes. The reason is what separates them.
	/// </remarks>
	[Fact]
	public void SuppressionCarriesItsReason()
	{
		var counters = new RuntimeCounters();

		for (var frame = 0; frame < 60; frame++)
		{
			counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
			counters.PadAt("pad-a/r", touched: true, 0.01 * frame, 0.0);
			counters.PadIgnored("OSK actif");
		}

		var line = counters.DrainToLine(OneSecond, "Profile", "SenSÉ");

		Assert.Contains("pad ignoré=60 (OSK actif)", line);
		Assert.Contains("PAD SOURD (voir la raison)", line);
	}

	/// <summary>
	/// One controller read twice over is named, not just implied by a high frame rate.
	/// </summary>
	[Fact]
	public void MoreThanOneSourceIsCalledOut()
	{
		var counters = new RuntimeCounters();

		for (var frame = 0; frame < 66; frame++)
		{
			counters.Frame(false, false, "pad-a");
			counters.Frame(false, false, "pad-b");
			counters.Frame(false, false, "pad-c");
		}

		Assert.True(counters.HasActivity);

		var line = counters.DrainToLine(OneSecond, "Profile", "SenSÉ");

		Assert.Contains("sources=3", line);
		Assert.Contains("pad-a:66", line);
		Assert.Contains("fps=198", line);
	}

	/// <summary>
	/// A single source stays out of the line, so the useful columns keep their room.
	/// </summary>
	[Fact]
	public void OneSourceIsNotWorthSaying()
	{
		var counters = new RuntimeCounters();

		counters.Frame(false, false, "pad-a");
		counters.MouseMotion(3, 4);

		var line = counters.DrainToLine(OneSecond, "Profile", "SenSÉ");

		Assert.DoesNotContain("sources=", line);
		Assert.DoesNotContain("PAD SOURD", line);
	}

	/// <summary>
	/// A moving pointer is never called deaf.
	/// </summary>
	[Fact]
	public void APadThatMovesThePointerIsNotDeaf()
	{
		var counters = new RuntimeCounters();

		counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
		counters.MouseMotion(2, 1);

		Assert.False(counters.PadIsDeaf);
		Assert.DoesNotContain("PAD SOURD", counters.DrainToLine(OneSecond, "Profile", "SenSÉ"));
	}

	/// <summary>
	/// Draining clears everything, or one bad second stains the rest of the session.
	/// </summary>
	[Fact]
	public void DrainingResetsTheReasonAndTheSources()
	{
		var counters = new RuntimeCounters();

		counters.Frame(rightTouched: true, leftTouched: false, "pad-a");
		counters.Frame(false, false, "pad-b");
		counters.PadIgnored("OSK actif");
		counters.DrainToLine(OneSecond, "Profile", "SenSÉ");

		Assert.False(counters.HasActivity);
		Assert.False(counters.PadIsDeaf);

		counters.Frame(false, false, "pad-a");
		counters.MouseMotion(1, 1);

		var line = counters.DrainToLine(OneSecond, "Profile", "SenSÉ");

		Assert.DoesNotContain("OSK actif", line);
		Assert.DoesNotContain("sources=", line);
	}
}
