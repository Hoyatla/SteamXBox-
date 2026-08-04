using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Which profile belongs to which controller.
/// </summary>
/// <remarks>
/// A wrong answer here is silent: the pad does not fail, it quietly applies the other player's
/// settings and merely "feels off". Most of these tests are about that failure, not about the
/// mapping.
/// </remarks>
public class ControllerProfileBookTests
{
    private const string SteamPad = "hid:vid&0228de_pid&1303_rev&0100_d1af66b5aa8d";
    private const string OtherSteamPad = "hid:vid&0228de_pid&1303_rev&0100_aabbccddeeff";
    private const string XboxSlot0 = "xinput-slot:0";
    private const string XboxSlot1 = "xinput-slot:1";

    [Fact]
    public void EachControllerKeepsItsOwnProfile()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(XboxSlot1, "invité");

        Assert.Equal("perso", book.ProfileFor(SteamPad));
        Assert.Equal("invité", book.ProfileFor(XboxSlot1));
    }

    // A guest plugging in a second pad is the normal case, not an error.
    [Fact]
    public void AnUnknownControllerFallsBackToTheDefault()
        => Assert.Equal("default", new ControllerProfileBook().ProfileFor("never-seen"));

    [Fact]
    public void TheFallbackIsConfigurable()
        => Assert.Equal("perso", new ControllerProfileBook("perso").ProfileFor("never-seen"));

    [Fact]
    public void AnEmptyFallbackNameIsRefused()
        => Assert.Equal("default", new ControllerProfileBook("  ").DefaultProfile);

    [Fact]
    public void ReassigningReplacesRatherThanAccumulates()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(SteamPad, "jeu");

        Assert.Equal("jeu", book.ProfileFor(SteamPad));
        Assert.Equal(1, book.Count);
    }

    // What happens when the user deletes the profile a controller was using.
    [Fact]
    public void AssigningNothingClearsRatherThanFilesUnderABlankProfile()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(SteamPad, "");

        Assert.False(book.HasOwnProfile(SteamPad));
        Assert.Equal("default", book.ProfileFor(SteamPad));
    }

    [Fact]
    public void AControllerWithNoIdIsIgnored()
    {
        var book = new ControllerProfileBook();
        book.Assign("", "perso");

        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void DeletingAProfileReleasesTheControllersUsingIt()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(OtherSteamPad, "supprimé");

        book.DropMissingProfiles(["perso", "default"]);

        Assert.True(book.HasOwnProfile(SteamPad));
        Assert.False(book.HasOwnProfile(OtherSteamPad));
    }

    // The decision this class exists for. An XInput slot names the order somebody switched their
    // controllers on in, so persisting it restores one player's settings onto whoever is first
    // tomorrow.
    [Fact]
    public void OnlyDurableAssignmentsAreWrittenToDisk()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(XboxSlot0, "invité");

        var saved = book.Persistable();

        Assert.Single(saved);
        Assert.True(saved.ContainsKey(SteamPad));
        Assert.False(saved.ContainsKey(XboxSlot0));
    }

    // Kept all the same: within one session the slot is exactly right, and refusing it would mean
    // an Xbox pad could never have a profile at all.
    [Fact]
    public void AnUnstableAssignmentStillWorksForThisSession()
    {
        var book = new ControllerProfileBook();
        book.Assign(XboxSlot0, "invité");

        Assert.Equal("invité", book.ProfileFor(XboxSlot0));

        // Live for this session, absent from what gets saved: the two halves of the decision.
        Assert.Single(book.All());
        Assert.Empty(book.Persistable());
    }

    // A file hand-edited or written by an older build may hold slot keys; trusting them is the
    // silent swap all over again.
    [Fact]
    public void SavedSlotKeysAreRefusedOnTheWayBackIn()
    {
        var book = new ControllerProfileBook();
        book.Load(new Dictionary<string, string> { [XboxSlot0] = "invité", [SteamPad] = "perso" });

        Assert.Equal("default", book.ProfileFor(XboxSlot0));
        Assert.Equal("perso", book.ProfileFor(SteamPad));
    }

    [Fact]
    public void ABlankSavedProfileNameIsRefused()
    {
        var book = new ControllerProfileBook();
        book.Load(new Dictionary<string, string> { [SteamPad] = "  " });

        Assert.False(book.HasOwnProfile(SteamPad));
    }

    [Fact]
    public void LoadingNothingIsHarmless()
    {
        var book = new ControllerProfileBook();
        book.Load(null);

        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void SavingAndReloadingKeepsTheDurableAssignments()
    {
        var first = new ControllerProfileBook();
        first.Assign(SteamPad, "perso");
        first.Assign(OtherSteamPad, "jeu");

        var second = new ControllerProfileBook();
        second.Load(first.Persistable());

        Assert.Equal("perso", second.ProfileFor(SteamPad));
        Assert.Equal("jeu", second.ProfileFor(OtherSteamPad));
    }

    [Fact]
    public void ClearingRemovesOneControllerOnly()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamPad, "perso");
        book.Assign(OtherSteamPad, "jeu");
        book.Clear(SteamPad);

        Assert.False(book.HasOwnProfile(SteamPad));
        Assert.True(book.HasOwnProfile(OtherSteamPad));
    }
}
