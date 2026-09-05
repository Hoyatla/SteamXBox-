using SenSÉ.Core.Input;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// An XInput frame turned into the state the rest of SenSÉ understands.
/// </summary>
/// <remarks>
/// Every mistake worth making here is invisible at runtime: an inverted axis feels like a badly
/// tuned controller, a swapped button like a wrong profile.
/// </remarks>
public class XInputStateMapperTests
{
    private static XInputFrame Frame(
        ushort buttons = 0, short lx = 0, short ly = 0, short rx = 0, short ry = 0,
        byte lt = 0, byte rt = 0)
        => new(buttons, lt, rt, lx, ly, rx, ry);

    private static ControllerState Map(XInputFrame frame)
        => XInputStateMapper.Map(frame, TimeSpan.Zero);

    [Theory]
    [InlineData(0x1000, SteamControllerButtons.A)]
    [InlineData(0x2000, SteamControllerButtons.B)]
    [InlineData(0x4000, SteamControllerButtons.X)]
    [InlineData(0x8000, SteamControllerButtons.Y)]
    [InlineData(0x0100, SteamControllerButtons.LeftBumper)]
    [InlineData(0x0200, SteamControllerButtons.RightBumper)]
    [InlineData(0x0040, SteamControllerButtons.LeftStick)]
    [InlineData(0x0080, SteamControllerButtons.RightStick)]
    [InlineData(0x0001, SteamControllerButtons.DPadUp)]
    [InlineData(0x0002, SteamControllerButtons.DPadDown)]
    [InlineData(0x0004, SteamControllerButtons.DPadLeft)]
    [InlineData(0x0008, SteamControllerButtons.DPadRight)]
    public void MapsEachSharedButton(int raw, SteamControllerButtons expected)
        => Assert.Equal(expected, XInputStateMapper.MapButtons((ushort)raw));

    // Start and Back are what an Xbox pad calls the roles the Steam Controller calls Menu and View.
    // This pairing has to agree with the Xbox output mapping, or a round trip swaps the two.
    [Fact]
    public void StartIsMenuAndBackIsView()
    {
        Assert.Equal(SteamControllerButtons.Menu, XInputStateMapper.MapButtons(0x0010));
        Assert.Equal(SteamControllerButtons.View, XInputStateMapper.MapButtons(0x0020));
    }

    // The grip paddles and the Quick Access button have no counterpart on an Xbox pad. Approximating
    // them onto something else would make that something fire unexpectedly.
    //
    // Steam is no longer on that list: the Xbox button is a real button and it now produces it.
    //
    // QuickAccess stays on it, and that is the point of this test. The Xbox button launches Steam;
    // it is not the mode switch. Mapping it onto QuickAccess to give the pad a switch was done on
    // 14 August and reverted the same day — the switch is the L3+R3 hold, in InputModeHandler.
    [Fact]
    public void NothingIsMappedOntoTheSteamControllerOnlyButtons()
    {
        var everything = XInputStateMapper.MapButtons(0xFFFF);

        foreach (var absent in new[]
        {
            SteamControllerButtons.L4, SteamControllerButtons.R4,
            SteamControllerButtons.L5, SteamControllerButtons.R5,
            SteamControllerButtons.QuickAccess,
        })
        {
            Assert.False(everything.HasFlag(absent), $"{absent} should not be produced");
        }
    }

    // The Xbox button opens Steam, the same as a Steam Controller's Steam button, so it produces the
    // same flag rather than travelling by a second path.
    [Fact]
    public void TheXboxButtonProducesSteam()
        => Assert.Equal(SteamControllerButtons.Steam, XInputStateMapper.MapButtons(0x0400));

    // It only ever arrives from XInputGetStateEx. The documented XInputGetState masks the bit out,
    // so on a machine where the undocumented export cannot be resolved this simply never fires —
    // every other button keeps working.
    [Fact]
    public void SteamComesFromTheGuideBitAlone()
    {
        Assert.False(XInputStateMapper.MapButtons(0xFFFF & ~0x0400)
            .HasFlag(SteamControllerButtons.Steam));
    }

    [Fact]
    public void CombinedButtonsAllComeThrough()
    {
        var both = XInputStateMapper.MapButtons(0x1000 | 0x0200);

        Assert.True(both.HasFlag(SteamControllerButtons.A));
        Assert.True(both.HasFlag(SteamControllerButtons.RightBumper));
    }

    [Fact]
    public void NoButtonsMeansNone()
        => Assert.Equal(SteamControllerButtons.None, XInputStateMapper.MapButtons(0));

    [Fact]
    public void ACentredStickIsZero()
    {
        var state = Map(Frame());

        Assert.Equal(0, state.LeftStick.X);
        Assert.Equal(0, state.LeftStick.Y);
    }

    [Fact]
    public void FullDeflectionIsOne()
    {
        var state = Map(Frame(lx: 32767, ly: 32767, rx: 32767, ry: 32767));

        Assert.Equal(1.0, state.LeftStick.X, 6);
        Assert.Equal(1.0, state.RightStick.Y, 6);
    }

    // The negative end reaches -32768. Dividing by 32767 alone would put it past -1 and let a
    // magnitude exceed 1, which pushes a curve or a dead-zone rescale outside its intended range.
    [Fact]
    public void TheNegativeExtremeIsClampedToMinusOne()
    {
        var state = Map(Frame(lx: short.MinValue, ly: short.MinValue));

        Assert.Equal(-1.0, state.LeftStick.X, 6);
        Assert.Equal(-1.0, state.LeftStick.Y, 6);
    }

    [Fact]
    public void TheAxesAreNotSwappedBetweenSticks()
    {
        var state = Map(Frame(lx: 32767, ry: 32767));

        Assert.Equal(1.0, state.LeftStick.X, 6);
        Assert.Equal(0, state.LeftStick.Y);
        Assert.Equal(0, state.RightStick.X);
        Assert.Equal(1.0, state.RightStick.Y, 6);
    }

    // The mapper reports the stick as the hardware does, up positive. Inverting for the screen is
    // the pointer mapper's job, and doing it in both places would cancel out.
    [Fact]
    public void UpStaysPositive()
        => Assert.True(Map(Frame(ry: 20000)).RightStick.Y > 0);

    [Theory]
    [InlineData((byte)0, 0.0)]
    [InlineData((byte)255, 1.0)]
    public void TriggersNormaliseToZeroOne(byte raw, double expected)
    {
        var state = Map(Frame(lt: raw, rt: raw));

        Assert.Equal(expected, state.LeftTrigger, 6);
        Assert.Equal(expected, state.RightTrigger, 6);
    }

    [Fact]
    public void TriggersAreNotSwapped()
    {
        var state = Map(Frame(lt: 255, rt: 0));

        Assert.Equal(1.0, state.LeftTrigger, 6);
        Assert.Equal(0.0, state.RightTrigger, 6);
    }

    // An Xbox pad has no trackpads. Released, not a pad resting at the centre — which downstream
    // would read as a finger held still in the middle.
    [Fact]
    public void ThePadsAreReportedAsReleased()
    {
        var state = Map(Frame(lx: 32767, buttons: 0x1000));

        Assert.Equal(TouchpadSample.Released, state.LeftPad);
        Assert.Equal(TouchpadSample.Released, state.RightPad);
    }

    [Fact]
    public void TheTimestampIsCarriedThrough()
    {
        var when = TimeSpan.FromMilliseconds(1234);
        Assert.Equal(when, XInputStateMapper.Map(Frame(), when).Timestamp);
    }
}
