using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The double-Shift gesture, apart from the hook that feeds it.
/// </summary>
/// <remarks>
/// The hook cannot be tested — it needs a message loop, a real keyboard and a machine nobody else is
/// typing on — but the gesture is where this fails in ways nobody notices. Every case below is one
/// that would look like "the launcher opens by itself" or "it never opens".
/// </remarks>
public class DoubleTapDetectorTests
{
    private static DoubleTapDetector New() => new(windowMs: 350);

    [Fact]
    public void TwoTapsInsideTheWindowFire()
    {
        var gesture = New();

        Assert.False(gesture.Press(1000));
        gesture.Release();
        Assert.True(gesture.Press(1200));
    }

    [Fact]
    public void TwoTapsTooFarApartDoNot()
    {
        var gesture = New();

        Assert.False(gesture.Press(1000));
        gesture.Release();
        Assert.False(gesture.Press(1400));
    }

    // The late second tap becomes the new first, so tapping slowly then quickly still works instead
    // of needing a pause to "reset".
    [Fact]
    public void ALateTapStartsTheGestureAgain()
    {
        var gesture = New();

        gesture.Press(1000);
        gesture.Release();
        gesture.Press(2000);
        gesture.Release();

        Assert.True(gesture.Press(2100));
    }

    // Windows repeats key-down while a key is held. Counting those would fire at the keyboard's
    // repeat rate the moment somebody leans on Shift.
    [Fact]
    public void AutoRepeatIsNotASecondTap()
    {
        var gesture = New();

        Assert.False(gesture.Press(1000));

        for (var t = 1030; t < 1300; t += 30)
        {
            Assert.False(gesture.Press(t));
        }
    }

    // Without consuming the pair, a third tap would pair with the second and fire again — the window
    // opening and closing while somebody taps.
    [Fact]
    public void AThirdTapDoesNotFireAgainOnItsOwn()
    {
        var gesture = New();

        gesture.Press(1000);
        gesture.Release();
        Assert.True(gesture.Press(1100));
        gesture.Release();

        Assert.False(gesture.Press(1200));
    }

    // Four taps are two gestures, which is what a user who taps twice then twice again expects.
    [Fact]
    public void FourTapsAreTwoGestures()
    {
        var gesture = New();
        var fired = 0;

        foreach (var t in new[] { 1000, 1100, 1200, 1300 })
        {
            if (gesture.Press(t)) fired++;
            gesture.Release();
        }

        Assert.Equal(2, fired);
    }

    // Shift is a modifier before it is a shortcut: typing "Hello World" presses it, releases it, and
    // presses it again for the second capital. By the clock that is a double tap.
    [Fact]
    public void AKeyPressedInBetweenCancelsTheGesture()
    {
        var gesture = New();

        gesture.Press(1000);
        gesture.Release();
        gesture.Interrupt();

        Assert.False(gesture.Press(1100));
    }

    [Fact]
    public void ACancelledGestureCanStartAgainImmediately()
    {
        var gesture = New();

        gesture.Press(1000);
        gesture.Release();
        gesture.Interrupt();

        Assert.False(gesture.Press(1100));
        gesture.Release();
        Assert.True(gesture.Press(1200));
    }
}
