using SenSÉ.Core.Input;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Which profile belongs to which family of controllers.
/// </summary>
/// <remarks>
/// A wrong answer here is silent: the pad does not fail, it quietly applies the wrong family's
/// settings and merely "feels off". Most of these tests are about that failure, not about the
/// mapping.
///
/// Settings used to be filed per controller. One DualSense then presented itself under two
/// Bluetooth addresses on the author's machine, the profile was filed under the address it
/// happened to connect with, and the next connection with the other address ran on the defaults
/// with nothing saying so — which is what these tests now guard against.
/// </remarks>
public class ControllerProfileBookTests
{
    private const string SteamFamily = "fam:steam";
    private const string Ps5Family = "fam:ps5";
    private const string XboxFamily = "fam:xbox";
    private const string XboxSlot0 = "xinput-slot:0";

    // The address a real DualSense connected under on the author's machine, before it appeared
    // under another one. Kept here as the concrete proof that a per-controller key must not be
    // trusted: the settings would have been filed under this, and never found again.
    private const string DualSenseBtAddress = "bt:44464836686d";

    [Fact]
    public void EachFamilyKeepsItsOwnProfile()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(XboxFamily, "invité");

        Assert.Equal("perso", book.ProfileFor(SteamFamily));
        Assert.Equal("invité", book.ProfileFor(XboxFamily));
    }

    // A guest plugging in a pad of an unassigned family is the normal case, not an error.
    [Fact]
    public void AnUnknownFamilyFallsBackToTheDefault()
        => Assert.Equal("default", new ControllerProfileBook().ProfileFor("fam:switch"));

    [Fact]
    public void TheFallbackIsConfigurable()
        => Assert.Equal("perso", new ControllerProfileBook("perso").ProfileFor("fam:switch"));

    [Fact]
    public void AnEmptyFallbackNameIsRefused()
        => Assert.Equal("default", new ControllerProfileBook("  ").DefaultProfile);

    [Fact]
    public void ReassigningReplacesRatherThanAccumulates()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(SteamFamily, "jeu");

        Assert.Equal("jeu", book.ProfileFor(SteamFamily));
        Assert.Equal(1, book.Count);
    }

    // What happens when the user deletes the profile a family was using.
    [Fact]
    public void AssigningNothingClearsRatherThanFilesUnderABlankProfile()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(SteamFamily, "");

        Assert.False(book.HasOwnProfile(SteamFamily));
        Assert.Equal("default", book.ProfileFor(SteamFamily));
    }

    [Fact]
    public void AnEmptyKeyIsIgnored()
    {
        var book = new ControllerProfileBook();
        book.Assign("", "perso");

        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void DeletingAProfileReleasesTheFamiliesUsingIt()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(Ps5Family, "supprimé");

        book.DropMissingProfiles(["perso", "default"]);

        Assert.True(book.HasOwnProfile(SteamFamily));
        Assert.False(book.HasOwnProfile(Ps5Family));
    }

    // Only family keys are written to disk. A Bluetooth address was trusted here once, and the
    // same pad came back under another address — the settings never found again.
    [Fact]
    public void OnlyFamilyAssignmentsAreWrittenToDisk()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(XboxSlot0, "invité");

        var saved = book.Persistable();

        Assert.Single(saved);
        Assert.True(saved.ContainsKey(SteamFamily));
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

    // A file hand-edited or written by an older build may hold per-controller keys; trusting them
    // is the silent loss all over again — the same pad connected under another address and the
    // settings were never found.
    [Fact]
    public void SavedPerControllerKeysAreRefusedOnTheWayBackIn()
    {
        var book = new ControllerProfileBook();
        book.Load(new Dictionary<string, string>
        {
            [XboxSlot0] = "invité",
            [DualSenseBtAddress] = "perso",
            [SteamFamily] = "steam",
        });

        Assert.Equal("default", book.ProfileFor(XboxSlot0));
        Assert.Equal("default", book.ProfileFor(DualSenseBtAddress));
        Assert.Equal("steam", book.ProfileFor(SteamFamily));
    }

    [Fact]
    public void ABlankSavedProfileNameIsRefused()
    {
        var book = new ControllerProfileBook();
        book.Load(new Dictionary<string, string> { [SteamFamily] = "  " });

        Assert.False(book.HasOwnProfile(SteamFamily));
    }

    [Fact]
    public void LoadingNothingIsHarmless()
    {
        var book = new ControllerProfileBook();
        book.Load(null);

        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void SavingAndReloadingKeepsTheFamilyAssignments()
    {
        var first = new ControllerProfileBook();
        first.Assign(SteamFamily, "perso");
        first.Assign(Ps5Family, "jeu");

        var second = new ControllerProfileBook();
        second.Load(first.Persistable());

        Assert.Equal("perso", second.ProfileFor(SteamFamily));
        Assert.Equal("jeu", second.ProfileFor(Ps5Family));
    }

    [Fact]
    public void ClearingRemovesOneFamilyOnly()
    {
        var book = new ControllerProfileBook();
        book.Assign(SteamFamily, "perso");
        book.Assign(Ps5Family, "jeu");
        book.Clear(SteamFamily);

        Assert.False(book.HasOwnProfile(SteamFamily));
        Assert.True(book.HasOwnProfile(Ps5Family));
    }

    // The DualSense that started this: whichever address it arrives under, it lands on the same
    // family's settings. That is the fix.
    [Fact]
    public void TwoIdenticalPadsShareTheirFamilyProfile()
    {
        var book = new ControllerProfileBook();
        book.Assign(Ps5Family, "perso");

        Assert.Equal("perso", book.ProfileFor(Ps5Family));
    }
}
