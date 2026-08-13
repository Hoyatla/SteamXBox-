using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Osk;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// A keyboard that claims the pad has to still be there to keep it.
/// </summary>
/// <remarks>
/// While the overlay owns a controller the pointer path is skipped, which is right — the pads are
/// aiming at letters, not moving a cursor. The claim was recorded when the toggle button was pressed
/// and never checked again. Once the overlay was made resident it could hide without the core
/// hearing it, and the claim outlived the keyboard: measured on 12 August, the last toggle of the
/// session was at 02:21:30 and the controller moved no cursor for the remaining ten seconds, while
/// the physical mouse worked throughout.
/// </remarks>
public class OverlayClaimTests
{
	private static readonly DateTimeOffset Start = new(2026, 8, 12, 2, 21, 30, TimeSpan.Zero);

	private static ProfileMapper Claimed()
	{
		var mapper = new ProfileMapper { OskActive = true };
		return mapper;
	}

	/// <summary>
	/// A claim nobody backs up lapses, and the pad comes back.
	/// </summary>
	[Fact]
	public void AnUnbackedClaimLapses()
	{
		var mapper = Claimed();

		// Not showing, but only just: the keyboard is allowed time to appear.
		Assert.False(mapper.ReconcileOverlay(overlayShowing: false, Start));
		Assert.True(mapper.OskActive);

		Assert.False(mapper.ReconcileOverlay(false, Start.AddSeconds(5)));
		Assert.True(mapper.OskActive);

		// Past the grace, the claim is dropped and the caller is told once.
		Assert.True(mapper.ReconcileOverlay(false, Start.AddSeconds(7)));
		Assert.False(mapper.OskActive);

		// Told once, not every frame afterwards.
		Assert.False(mapper.ReconcileOverlay(false, Start.AddSeconds(8)));
		Assert.False(mapper.OskActive);
	}

	/// <summary>
	/// A keyboard that is on screen keeps the pad for as long as it likes.
	/// </summary>
	[Fact]
	public void AKeyboardThatIsThereIsLeftAlone()
	{
		var mapper = Claimed();

		for (var minute = 0; minute < 30; minute++)
		{
			Assert.False(mapper.ReconcileOverlay(overlayShowing: true, Start.AddMinutes(minute)));
		}

		Assert.True(mapper.OskActive);
	}

	/// <summary>
	/// A keyboard slow to appear is not cancelled out from under itself.
	/// </summary>
	/// <remarks>
	/// The cold start was about four seconds before the overlay became resident, and is still the
	/// path taken the first time. Cancelling the claim inside that window would make the very first
	/// press of the session look like it did nothing.
	/// </remarks>
	[Fact]
	public void AColdStartIsGivenTimeToAppear()
	{
		var mapper = Claimed();

		Assert.False(mapper.ReconcileOverlay(false, Start));
		Assert.False(mapper.ReconcileOverlay(false, Start.AddSeconds(4)));
		Assert.True(mapper.ReconcileOverlay(true, Start.AddSeconds(4.5)) == false);
		Assert.True(mapper.OskActive);

		// And having appeared, the clock is forgotten: a later hide starts its own grace.
		Assert.False(mapper.ReconcileOverlay(false, Start.AddSeconds(5)));
		Assert.True(mapper.OskActive);
		Assert.True(mapper.ReconcileOverlay(false, Start.AddSeconds(12)));
		Assert.False(mapper.OskActive);
	}

	/// <summary>
	/// Nothing is reconciled when no claim was made.
	/// </summary>
	[Fact]
	public void NoClaimMeansNothingToGiveBack()
	{
		var mapper = new ProfileMapper { OskActive = false };

		Assert.False(mapper.ReconcileOverlay(false, Start));
		Assert.False(mapper.ReconcileOverlay(false, Start.AddMinutes(10)));
		Assert.False(mapper.OskActive);
	}

	/// <summary>
	/// Giving the pad back also puts the daisywheel away.
	/// </summary>
	/// <remarks>
	/// It is part of the same typing state. Left standing, the controller would be back on its
	/// profile while still believing ABXY pick characters.
	/// </remarks>
	[Fact]
	public void TheDaisywheelGoesWithIt()
	{
		var mapper = new ProfileMapper { OskActive = true, DaisywheelActive = true };

		// The first look only starts the clock — the grace is counted from when the discrepancy was
		// first seen, not from any absolute time, so a mapper cannot be reconciled on its first frame.
		Assert.False(mapper.ReconcileOverlay(false, Start));
		Assert.True(mapper.ReconcileOverlay(false, Start.AddSeconds(7)));
		Assert.False(mapper.DaisywheelActive);
	}

	/// <summary>
	/// The two ends derive the same beat file name from the same controller.
	/// </summary>
	/// <remarks>
	/// A suffix that disagrees between the overlay and the core is a report nobody reads — the core
	/// would watch a file the keyboard never writes and give the pad back while it is being typed on.
	/// </remarks>
	[Fact]
	public void BothEndsAgreeOnTheBeatFile()
	{
		const string controller = "usb:vid_3537&pid_100a:00838bfc41";

		var core = OskInstanceNaming.For(controller);
		var overlay = OskInstanceNaming.FromSuffix(core.Suffix);

		Assert.Equal(core.VisibleBeatFile, overlay.VisibleBeatFile);
		Assert.EndsWith(".signal", core.VisibleBeatFile);

		// And it is not one of the orders: a report sharing a name with a command would be deleted
		// by whichever end read it first.
		Assert.NotEqual(core.ShowSignalFile, core.VisibleBeatFile);
		Assert.NotEqual(core.CloseSignalFile, core.VisibleBeatFile);
		Assert.NotEqual(core.ExitSignalFile, core.VisibleBeatFile);
	}
}
