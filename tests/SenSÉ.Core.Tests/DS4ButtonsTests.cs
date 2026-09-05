using SenSÉ.Core.Output;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The bit positions a virtual DualShock 4 expects.
/// </summary>
/// <remarks>
/// These numbers are not a convention this project is free to choose: <c>ViGEmDS4Sink</c> hands the
/// bitfield to ViGEm's <c>SetButtonsFull</c> verbatim, so they must be ViGEm's own. Getting them
/// wrong fails silently — it compiles, it runs, the virtual pad appears, and the game reads a
/// different button than the one pressed.
///
/// <para>
/// That is exactly what shipped: the table ran from bit 0 to bit 11 instead of bit 4 to bit 15, the
/// exact mirror. Cross fired L2, L1 fired Triangle, and the four bits that fell into the low nibble
/// — Share, Options, L3, R3 — were overwritten by the d-pad on the next line and did nothing at all.
/// Reported as "the buttons don't work in a game", and no amount of testing the input side could
/// have found it: everything upstream was correct.
/// </para>
///
/// <para>
/// Values read from Nefarius.ViGEm.Client 1.21.256 on 15 August 2026.
/// </para>
/// </remarks>
public class DS4ButtonsTests
{
    [Theory]
    [InlineData(DS4Buttons.Square, 0x0010)]
    [InlineData(DS4Buttons.Cross, 0x0020)]
    [InlineData(DS4Buttons.Circle, 0x0040)]
    [InlineData(DS4Buttons.Triangle, 0x0080)]
    [InlineData(DS4Buttons.ShoulderLeft, 0x0100)]
    [InlineData(DS4Buttons.ShoulderRight, 0x0200)]
    [InlineData(DS4Buttons.TriggerLeft, 0x0400)]
    [InlineData(DS4Buttons.TriggerRight, 0x0800)]
    [InlineData(DS4Buttons.Share, 0x1000)]
    [InlineData(DS4Buttons.Options, 0x2000)]
    [InlineData(DS4Buttons.ThumbLeft, 0x4000)]
    [InlineData(DS4Buttons.ThumbRight, 0x8000)]
    public void EachButtonSitsWhereViGEmPutsIt(DS4Buttons bouton, int attendu)
    {
        Assert.Equal(attendu, (int)bouton);
    }

    /// <summary>
    /// Nothing may fall in the low nibble: that is the d-pad hat, and the sink overwrites it.
    /// </summary>
    [Fact]
    public void NoButtonLandsInTheDpadNibble()
    {
        foreach (DS4Buttons bouton in Enum.GetValues<DS4Buttons>())
        {
            Assert.Equal(0, (int)bouton & 0x000F);
        }
    }
}
