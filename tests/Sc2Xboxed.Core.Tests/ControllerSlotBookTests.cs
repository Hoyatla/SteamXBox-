using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Which number belongs to which controller, between sessions.
/// </summary>
/// <remarks>
/// "Manette 1" has to be the same pad tomorrow. Numbering by position in the attached list
/// renumbers everybody the moment somebody switches a controller on in a different order — the
/// profiles stay filed correctly, but the number on the chip the user clicks is now someone else's.
/// </remarks>
public class ControllerSlotBookTests
{
    private const string PadA = "bt:44464836686d";
    private const string PadB = "bt:90b685f7696f";
    private const string XboxSlot = "xinput-slot:0";

    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstControllerTakesNumberOne()
        => Assert.Equal(1, new ControllerSlotBook().SlotFor(PadA, Now));

    [Fact]
    public void EachControllerGetsItsOwnNumber()
    {
        var book = new ControllerSlotBook();

        Assert.Equal(1, book.SlotFor(PadA, Now));
        Assert.Equal(2, book.SlotFor(PadB, Now));
    }

    // The point of the class.
    [Fact]
    public void AControllerKeepsItsNumberWhateverTheOrder()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now);
        book.SlotFor(PadB, Now);

        var second = new ControllerSlotBook();
        second.Load(book.Persistable());

        // B switched on first this time; it must still be number two.
        Assert.Equal(2, second.SlotFor(PadB, Now));
        Assert.Equal(1, second.SlotFor(PadA, Now));
    }

    // Next-highest would climb forever in a household that pairs and unpairs pads, and "Manette 7"
    // with two controllers in the room reads as a fault.
    [Fact]
    public void AFreedNumberIsReused()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now);
        book.SlotFor(PadB, Now);
        book.Forget(PadA);

        Assert.Equal(1, book.SlotFor("bt:aabbccddeeff", Now));
    }

    [Fact]
    public void SeeingAControllerAgainDoesNotMoveIt()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now);
        book.SlotFor(PadB, Now);

        Assert.Equal(1, book.SlotFor(PadA, Now.AddDays(3)));
    }

    // By age, never by absence: a pad switched off must keep its number, which is the whole point.
    [Fact]
    public void ASwitchedOffPadKeepsItsNumber()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now);

        Assert.Equal(0, book.DropUnseenSince(Now.AddDays(-30)));
        Assert.True(book.Knows(PadA));
    }

    [Fact]
    public void APadNotSeenForAgesIsForgotten()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now.AddDays(-90));
        book.SlotFor(PadB, Now);

        Assert.Equal(1, book.DropUnseenSince(Now.AddDays(-30)));
        Assert.False(book.Knows(PadA));
        Assert.True(book.Knows(PadB));
    }

    [Fact]
    public void SeeingAPadRefreshesItsAge()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now.AddDays(-90));
        book.SlotFor(PadA, Now);

        Assert.Equal(0, book.DropUnseenSince(Now.AddDays(-30)));
    }

    // A number filed under an XInput slot would claim, tomorrow, whichever pad was switched on first.
    [Fact]
    public void OnlyDurableNumbersAreSaved()
    {
        var book = new ControllerSlotBook();
        book.SlotFor(PadA, Now);
        book.SlotFor(XboxSlot, Now);

        Assert.Single(book.Persistable());
        Assert.Equal(2, book.All().Count);
    }

    [Fact]
    public void SavedSlotKeysAreRefusedOnTheWayBackIn()
    {
        var book = new ControllerSlotBook();
        book.Load(new Dictionary<string, ControllerSlot>
        {
            [XboxSlot] = new(1, Now),
            [PadA] = new(2, Now),
        });

        Assert.False(book.Knows(XboxSlot));
        Assert.Equal(2, book.SlotFor(PadA, Now));
    }

    [Fact]
    public void AnUnnumberedEntryIsRefused()
    {
        var book = new ControllerSlotBook();
        book.Load(new Dictionary<string, ControllerSlot> { [PadA] = new(0, Now) });

        Assert.False(book.Knows(PadA));
    }

    [Fact]
    public void LoadingNothingIsHarmless()
    {
        var book = new ControllerSlotBook();
        book.Load(null);

        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void AControllerWithNoIdGetsNoNumber()
        => Assert.Equal(0, new ControllerSlotBook().SlotFor("", Now));
}
