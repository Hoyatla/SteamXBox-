using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Every channel name for one controller's own keyboard.
/// </summary>
/// <remarks>
/// The failure this guards is silent by nature: a suffix that disagrees between the bridge and the
/// overlay is a pipe that never connects, with no error anywhere — just a keyboard that shows and
/// receives nothing.
/// </remarks>
public class OskInstanceNamingTests
{
    private const string PadA = "bt:44464836686d";
    private const string PadB = "bt:90b685f7696f";

    // Kept identical to what shipped before, so an overlay started without an instance still finds
    // its pipes.
    [Fact]
    public void TheDefaultInstanceKeepsTheHistoricalNames()
    {
        Assert.Equal("SenSÉ_OskPad", OskInstanceNaming.Default.PadPipeName);
        Assert.Equal("SenSÉ_OskHaptic", OskInstanceNaming.Default.HapticPipeName);
        Assert.Equal("osk-show.signal", OskInstanceNaming.Default.ShowSignalFile);
    }

    [Fact]
    public void AControllerGetsItsOwnNames()
    {
        var naming = OskInstanceNaming.For(PadA);

        Assert.NotEqual(OskInstanceNaming.Default.PadPipeName, naming.PadPipeName);
        Assert.NotEqual(OskInstanceNaming.Default.ShowSignalFile, naming.ShowSignalFile);
    }

    // The point: two players asking at once must not share a single channel.
    [Fact]
    public void TwoControllersNeverShareAChannel()
    {
        var a = OskInstanceNaming.For(PadA);
        var b = OskInstanceNaming.For(PadB);

        Assert.NotEqual(a.PadPipeName, b.PadPipeName);
        Assert.NotEqual(a.HapticPipeName, b.HapticPipeName);
        Assert.NotEqual(a.ShowSignalFile, b.ShowSignalFile);
        Assert.NotEqual(a.CloseSignalFile, b.CloseSignalFile);
        Assert.NotEqual(a.ExitSignalFile, b.ExitSignalFile);
    }

    // The two ends derive the names separately; they must land on the same ones.
    [Fact]
    public void TheSuffixRoundTripsThroughACommandLine()
    {
        var origin = OskInstanceNaming.For(PadA);
        var rebuilt = OskInstanceNaming.FromSuffix(origin.Suffix);

        Assert.Equal(origin.PadPipeName, rebuilt.PadPipeName);
        Assert.Equal(origin.HapticPipeName, rebuilt.HapticPipeName);
        Assert.Equal(origin.ShowSignalFile, rebuilt.ShowSignalFile);
    }

    // string.GetHashCode is randomised per process: the two ends would derive different names and
    // the pipe would never connect, with nothing to say why. Pinned so it cannot drift.
    [Fact]
    public void TheDigestIsStableAndNotTheRuntimeHash()
    {
        Assert.Equal(OskInstanceNaming.Digest(PadA), OskInstanceNaming.Digest(PadA));
        Assert.Equal(8, OskInstanceNaming.Digest(PadA).Length);
        Assert.NotEqual(OskInstanceNaming.Digest(PadA), OskInstanceNaming.Digest(PadB));
    }

    // A durable key holds characters a file name refuses, and has no bounded length.
    [Fact]
    public void TheSuffixIsSafeForBothAPipeAndAFileName()
    {
        var naming = OskInstanceNaming.For(@"bt:44:46:48\36/686d");

        Assert.All(naming.Suffix, c => Assert.True(char.IsLetterOrDigit(c)));
        Assert.DoesNotContain('\\', naming.ShowSignalFile);
        Assert.DoesNotContain(':', naming.PadPipeName[12..]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoControllerMeansTheDefaultInstance(string? id)
        => Assert.Equal(OskInstanceNaming.Default.PadPipeName, OskInstanceNaming.For(id).PadPipeName);

    [Fact]
    public void AnEmptySuffixRebuildsTheDefault()
        => Assert.Equal(OskInstanceNaming.Default.ShowSignalFile, OskInstanceNaming.FromSuffix("").ShowSignalFile);

    [Fact]
    public void TheThreeSignalsAreDistinct()
    {
        var naming = OskInstanceNaming.For(PadA);

        Assert.NotEqual(naming.ShowSignalFile, naming.CloseSignalFile);
        Assert.NotEqual(naming.CloseSignalFile, naming.ExitSignalFile);
        Assert.NotEqual(naming.ShowSignalFile, naming.ExitSignalFile);
    }
}
