using SenSÉ.Core.Runtime;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The command line the core is started with.
/// </summary>
/// <remarks>
/// Tested because getting it wrong is invisible: the core starts, the console appears, and the
/// controller simply does nothing useful. That is exactly what happened — the environment launched
/// the core with no arguments, so there was no virtual gamepad and no mapping, and the failure
/// looked like two unrelated bugs.
/// </remarks>
public class CoreCommandLineTests
{
    // Without this verb the core never creates the virtual gamepad, so switching to Xbox does
    // nothing at all.
    [Fact]
    public void AlwaysAsksForTheXboxRunVerb()
        => Assert.StartsWith("xbox-run ", CoreCommandLine.BuildRun("Default", "Profile", "quick-access"));

    // Without this the mapping is never loaded and every profile shortcut is dead.
    [Fact]
    public void AlwaysCarriesAProfile()
        => Assert.Contains("--profile \"perso\"", CoreCommandLine.BuildRun("perso", "Profile", "quick-access"));

    [Fact]
    public void CarriesTheSwitchButton()
    {
        var args = CoreCommandLine.BuildRun("perso", "Xbox360", "quick-access");

        Assert.Contains("--switch-button quick-access", args);
    }

    // The Xbox layout travels inside the profile since the merge, so it must never be passed on
    // the command line: a profile changed while the core runs would stay stale.
    [Fact]
    public void NoLongerPassesASeparateXboxProfile()
        => Assert.DoesNotContain("--xbox-profile", CoreCommandLine.BuildRun("perso", "Xbox360", "quick-access"));

    // The profile stores the mode capitalised; the command line expects lower case.
    [Theory]
    [InlineData("Profile", "--start-mode profile")]
    [InlineData("Xbox360", "--start-mode xbox360")]
    [InlineData("XBOX360", "--start-mode xbox360")]
    public void LowerCasesTheMode(string stored, string expected)
        => Assert.Contains(expected, CoreCommandLine.BuildRun("Default", stored, "quick-access"));

    // Unquoted, "Mon profil" arrives as two arguments and the core loads a profile that does not
    // exist — silently, since a missing profile falls back to the defaults.
    [Fact]
    public void QuotesNamesThatContainSpaces()
    {
        var args = CoreCommandLine.BuildRun("Mon profil", "Profile", "quick-access");

        Assert.Contains("--profile \"Mon profil\"", args);
    }

    // A first run has no profile recorded yet. Starting on the defaults beats not starting.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToTheDefaultsWhenNothingIsRecorded(string empty)
    {
        var args = CoreCommandLine.BuildRun(empty, empty, empty);

        Assert.Contains($"--profile \"{CoreCommandLine.DefaultProfile}\"", args);
        Assert.Contains($"--switch-button {CoreCommandLine.DefaultSwitchButton}", args);
        Assert.Contains("--start-mode profile", args);
    }

    [Fact]
    public void RestartsAnyBridgeAlreadyRunning()
        => Assert.Contains("--restart", CoreCommandLine.BuildRun("Default", "Profile", "quick-access"));
}
