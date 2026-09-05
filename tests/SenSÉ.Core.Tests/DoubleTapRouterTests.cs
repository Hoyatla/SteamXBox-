using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

public class DoubleTapRouterTests
{
    private const int Shift = 0x10;
    private const int RightShift = 0xA1;
    private const int LetterA = 0x41;

    /// <summary>The section key as it is on a Swiss French keyboard, where this was measured.</summary>
    private const int Section = 0xBF;

    private static DoubleTapRouter<string> Router() => new(
    [
        ("launcher", new HashSet<int> { Shift, RightShift }),
        ("clear", new HashSet<int> { Section }),
    ]);

    /// <summary>Taps a key down then up, and returns what fired.</summary>
    private static IReadOnlyList<string> Tap(DoubleTapRouter<string> router, int key, long at)
    {
        var fired = router.Press(key, at);
        router.Release(key);

        return fired;
    }

    [Fact]
    public void OneTapFiresNothing()
        => Assert.Empty(Tap(Router(), Section, 0));

    [Fact]
    public void TwoTapsFireTheirOwnGesture()
    {
        var router = Router();

        Tap(router, Section, 0);

        Assert.Equal(["clear"], Tap(router, Section, 100));
    }

    [Fact]
    public void EachGestureHasItsOwnKey()
    {
        var router = Router();

        Tap(router, Shift, 0);

        Assert.Equal(["launcher"], Tap(router, Shift, 100));
    }

    // Shift is reported under three codes depending on the side; a gesture that only recognised one
    // of them would work on half the keyboard.
    [Fact]
    public void EitherSideOfAKeyCountsAsTheSameGesture()
    {
        var router = Router();

        Tap(router, Shift, 0);

        Assert.Equal(["launcher"], Tap(router, RightShift, 100));
    }

    [Fact]
    public void TwoTapsTooFarApartFireNothing()
    {
        var router = Router();

        Tap(router, Section, 0);

        Assert.Empty(Tap(router, Section, 5000));
    }

    // The rule between gestures, and the reason this is worth testing. Section, then something else,
    // then Section is three keystrokes and no gesture — the first press was dismissing something.
    [Fact]
    public void AnotherKeyInBetweenCancelsTheGesture()
    {
        var router = Router();

        Tap(router, Section, 0);
        Tap(router, LetterA, 50);

        Assert.Empty(Tap(router, Section, 100));
    }

    // And the gestures cancel each other, since one is "another key" as far as the other is
    // concerned.
    [Fact]
    public void OneGesturesKeyCancelsTheOther()
    {
        var router = Router();

        Tap(router, Section, 0);
        Tap(router, Shift, 50);

        Assert.Empty(Tap(router, Section, 100));
    }

    // Completing one gesture must not fire the other as well.
    [Fact]
    public void OnlyTheGestureThatWasCompletedFires()
    {
        var router = Router();

        Tap(router, Section, 0);

        Assert.Equal(["clear"], Tap(router, Section, 100));
    }

    // Windows repeats key-down while a key is held. Counting those would fire the gesture at the
    // keyboard's repeat rate the moment somebody leant on the key.
    [Fact]
    public void HoldingAKeyDownFiresNothing()
    {
        var router = Router();

        router.Press(Section, 0);

        for (var repeat = 1; repeat <= 20; repeat++)
        {
            Assert.Empty(router.Press(Section, repeat * 30));
        }
    }
}
