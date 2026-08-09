using SteamXBox.Tools.Declarative;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The two host capabilities a declarative tool is allowed to reach: opening something, and saving
/// one file the user pointed at.
/// </summary>
public class LaunchVocabularyTests
{
    [Theory]
    [InlineData("open.settings:bluetooth", LaunchKind.Settings, "bluetooth")]
    [InlineData("open.settings:display", LaunchKind.Settings, "display")]
    [InlineData("open.folder:Documents", LaunchKind.Folder, "documents")]
    public void TheKnownKindsAreRead(string does, LaunchKind kind, string argument)
    {
        var target = LaunchVocabulary.Parse(does);

        Assert.Equal(kind, target.Kind);
        Assert.Equal(argument, target.Argument);
    }

    [Fact]
    public void AWebAddressKeepsItsScheme()
        => Assert.Equal(
            new LaunchTarget(LaunchKind.Web, "https://example.org/"),
            LaunchVocabulary.Parse("open.web:https://example.org"));

    // The one launch that is deliberately absent: running a program turns installing a tool into
    // running its author's code, which is what the manifest format exists to avoid.
    [Theory]
    [InlineData("open.app:C:\\Windows\\System32\\cmd.exe")]
    [InlineData("run:cmd.exe")]
    [InlineData("exec:powershell")]
    public void RunningAProgramIsNotInTheVocabulary(string does)
        => Assert.Equal(LaunchKind.None, LaunchVocabulary.Parse(does).Kind);

    // A settings page is a name, never a whole URI: accepting a scheme here would accept every other
    // scheme spelled the same way, and a scheme is how a program gets launched on Windows.
    [Theory]
    [InlineData("open.settings:ms-settings:bluetooth")]
    [InlineData("open.settings:file:///C:/")]
    [InlineData("open.settings:../../evil")]
    public void ASettingsPageIsANameNotAUri(string does)
        => Assert.Equal(LaunchKind.None, LaunchVocabulary.Parse(does).Kind);

    // file: in particular would walk straight past the rule about reading the disk.
    [Theory]
    [InlineData("open.web:file:///C:/Windows/win.ini")]
    [InlineData("open.web:javascript:alert(1)")]
    [InlineData("open.web:not a url")]
    public void OnlyHttpAndHttpsCountAsWeb(string does)
        => Assert.Equal(LaunchKind.None, LaunchVocabulary.Parse(does).Kind);

    [Theory]
    [InlineData("open.folder:C:\\Windows")]
    [InlineData("open.folder:anywhere")]
    public void OnlyWindowsOwnFoldersCanBeOpened(string does)
        => Assert.Equal(LaunchKind.None, LaunchVocabulary.Parse(does).Kind);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("open.settings")]
    [InlineData("open.settings:")]
    [InlineData(":bluetooth")]
    public void NonsenseNamesNothing(string? does)
        => Assert.Equal(LaunchKind.None, LaunchVocabulary.Parse(does).Kind);
}

public class FileGrantTests
{
    [Fact]
    public void AGrantCoversTheFileItWasIssuedFor()
    {
        var grant = FileGrant.Issue(@"C:\Users\Someone\Documents\note.txt");

        Assert.NotNull(grant);
        Assert.True(grant!.Covers(@"C:\Users\Someone\Documents\note.txt"));
    }

    // The whole point: one file, not its neighbours and not its directory.
    [Theory]
    [InlineData(@"C:\Users\Someone\Documents\other.txt")]
    [InlineData(@"C:\Users\Someone\Documents")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    public void AGrantCoversNothingElse(string other)
    {
        var grant = FileGrant.Issue(@"C:\Users\Someone\Documents\note.txt");

        Assert.False(grant!.Covers(other));
    }

    // Resolved before comparing, so a granted path with .. in the middle cannot reach elsewhere.
    [Fact]
    public void ATraversalDoesNotEscapeTheGrant()
    {
        var grant = FileGrant.Issue(@"C:\Users\Someone\Documents\note.txt");

        Assert.False(grant!.Covers(@"C:\Users\Someone\Documents\..\..\..\Windows\win.ini"));
    }

    [Fact]
    public void AGrantIsSpentOnce()
    {
        var grant = FileGrant.Issue(@"C:\Users\Someone\Documents\note.txt");
        grant!.Spend();

        Assert.False(grant.Covers(@"C:\Users\Someone\Documents\note.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Users\Someone\Documents\")]
    public void NothingDesignatedGrantsNothing(string? chosen)
        => Assert.Null(FileGrant.Issue(chosen));

    // A tool proposes a name, not a location.
    [Theory]
    [InlineData(@"..\..\Windows\evil.txt", "txt", "evil.txt")]
    [InlineData(@"C:\Windows\note.txt", "txt", "note.txt")]
    [InlineData("rapport", "txt", "rapport.txt")]
    [InlineData("rapport.txt", "txt", "rapport.txt")]
    [InlineData("", "txt", "document.txt")]
    public void ASuggestionIsAFileNameOnly(string suggested, string extension, string expected)
        => Assert.Equal(expected, FileGrant.SafeSuggestion(new SaveRequest(suggested, extension)));

    [Fact]
    public void InvalidCharactersAreReplacedRatherThanRefused()
        => Assert.Equal("a_b_c.txt", FileGrant.SafeSuggestion(new SaveRequest("a?b|c", ".txt")));

    // A colon is not merely an invalid character on Windows, it is a drive qualifier, so the part
    // before it is a location and GetFileName drops it. That is the wanted answer here — a
    // suggestion carries no location — and it is why the name is reduced before it is cleaned
    // rather than after.
    [Fact]
    public void ADriveQualifierIsDroppedRatherThanEscaped()
        => Assert.Equal("b_c.txt", FileGrant.SafeSuggestion(new SaveRequest("a:b|c", ".txt")));
}
