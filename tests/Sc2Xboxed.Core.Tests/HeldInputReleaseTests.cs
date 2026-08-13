using Sc2Xboxed.Core.Mapping;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Whatever the mapper presses, the mapper releases.
/// </summary>
/// <remarks>
/// The two triggers press the mouse buttons and the two volume keys stay down while held. All four
/// are machine-wide: they are not delivered to a window, they change the state of the input device
/// itself. A press that never gets its release does not fail quietly in SteamXBox — it fails in
/// every other program on the machine.
///
/// The mouse case is the one that was reported, and it does not look like what it is. The cursor
/// still moves, because moving is a separate event from pressing. What stops is everything else:
/// with the left button held, every gesture is the middle of a drag, clicks land nowhere and no
/// window answers. "The mouse stopped responding" is exactly how a latched button presents.
///
/// The original edge suspended the release along with the press, which is right for a shortcut —
/// a key typed under the overlay is a ghost in the middle of a word — and wrong for a hold.
/// </remarks>
public class HeldInputReleaseTests
{
	/// <summary>
	/// Pull the trigger, open the overlay, let the trigger go. The button must come back up.
	/// </summary>
	/// <remarks>
	/// The reported sequence. Under the old edge the release branch required the binding to be
	/// live, so releasing under the overlay emitted nothing while the flag still followed the
	/// controller down to false — after which no edge remained to fire, ever. The button stayed
	/// down until the machine was restarted.
	/// </remarks>
	[Fact]
	public void ReleasingUnderTheOverlayStillReleases()
	{
		bool prev = false, owed = false;
		int downs = 0, ups = 0;

		// Live, trigger pulled: the button goes down.
		ProfileMapper.HandleHold(ref prev, ref owed, current: true, enabled: true, () => downs++, () => ups++);
		Assert.Equal(1, downs);
		Assert.Equal(0, ups);

		// The overlay opens. The trigger is still held, and nothing has changed for the desktop.
		// Then the user lets go, with the binding suspended.
		ProfileMapper.HandleHold(ref prev, ref owed, current: false, enabled: false, () => downs++, () => ups++);

		Assert.Equal(1, ups);
		Assert.False(owed);

		// The overlay closes. Nothing further may fire: the debt is settled, not deferred.
		ProfileMapper.HandleHold(ref prev, ref owed, current: false, enabled: true, () => downs++, () => ups++);
		Assert.Equal(1, downs);
		Assert.Equal(1, ups);
	}

	/// <summary>
	/// A press that never happened is never released.
	/// </summary>
	/// <remarks>
	/// The other half, and the reason the release cannot simply be made unconditional. A trigger
	/// pulled and released entirely under the overlay commits a key there; if the mapper also sent
	/// a mouse-up for it, the desktop would receive a button release it never saw pressed.
	/// </remarks>
	[Fact]
	public void APressSuppressedByTheOverlayOwesNothing()
	{
		bool prev = false, owed = false;
		int downs = 0, ups = 0;

		ProfileMapper.HandleHold(ref prev, ref owed, current: true, enabled: false, () => downs++, () => ups++);
		ProfileMapper.HandleHold(ref prev, ref owed, current: false, enabled: false, () => downs++, () => ups++);

		Assert.Equal(0, downs);
		Assert.Equal(0, ups);
		Assert.False(owed);
	}

	/// <summary>
	/// Holding across a frame does not press twice.
	/// </summary>
	[Fact]
	public void HoldingDoesNotRepeatThePress()
	{
		bool prev = false, owed = false;
		int downs = 0, ups = 0;

		for (var frame = 0; frame < 5; frame++)
			ProfileMapper.HandleHold(ref prev, ref owed, current: true, enabled: true, () => downs++, () => ups++);

		Assert.Equal(1, downs);
		Assert.True(owed);

		ProfileMapper.HandleHold(ref prev, ref owed, current: false, enabled: true, () => downs++, () => ups++);
		Assert.Equal(1, ups);
		Assert.False(owed);
	}

	/// <summary>
	/// The plain edge, unchanged: an ordinary press and release while live.
	/// </summary>
	[Fact]
	public void TheOrdinaryPathIsUntouched()
	{
		bool prev = false, owed = false;
		int downs = 0, ups = 0;

		ProfileMapper.HandleHold(ref prev, ref owed, current: true, enabled: true, () => downs++, () => ups++);
		ProfileMapper.HandleHold(ref prev, ref owed, current: false, enabled: true, () => downs++, () => ups++);

		Assert.Equal(1, downs);
		Assert.Equal(1, ups);
		Assert.False(owed);
	}
}
