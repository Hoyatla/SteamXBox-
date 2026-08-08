using Sc2Xboxed.Core.Input;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The key a controller's profile is filed under.
/// </summary>
/// <remarks>
/// Every connected controller is an input, so each carries its own profile. That only means
/// something if the same controller is recognised again tomorrow — otherwise two players' settings
/// quietly swap between sessions, and nothing looks broken.
/// </remarks>
public class ControllerIdentityTests
{
    // The three interfaces of one Bluetooth Steam Controller, as this machine enumerates them.
    private const string Col01 =
        @"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&0228de_pid&1303_rev&0100_d1af66b5aa8d&col01#9&112a9d53&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string Col03 =
        @"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&0228de_pid&1303_rev&0100_d1af66b5aa8d&col03#9&112a9d53&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    // A controller exposes several HID collections. They are one device and must share one profile.
    [Fact]
    public void EveryInterfaceOfOneControllerGivesTheSameKey()
        => Assert.Equal(
            ControllerIdentityFactory.FromHidPath(Col01),
            ControllerIdentityFactory.FromHidPath(Col03));

    // Windows varies the case of these paths between enumerations; the same controller must not
    // look like a new one because of it.
    [Fact]
    public void TheKeyIgnoresCase()
        => Assert.Equal(
            ControllerIdentityFactory.FromHidPath(Col01),
            ControllerIdentityFactory.FromHidPath(Col01.ToUpperInvariant()));

    // The path a real DualSense enumerates under, taken from the HID probe on the author's machine.
    // No "&col" marker anywhere, which is what made the old normalisation a no-op on this pad.
    private const string DualSense =
        @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&15f755c8&3&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    // The trailing GUID is the HID class interface: identical on every HID device, so it carries no
    // identity and only makes the key sensitive to how the path is spelled.
    [Fact]
    public void TheClassInterfaceGuidIsNotPartOfTheKey()
        => Assert.DoesNotContain("4d1e55b2", ControllerIdentityFactory.FromHidPath(DualSense));

    [Fact]
    public void ADualSenseKeepsItsVendorProductAndInstance()
    {
        var key = ControllerIdentityFactory.FromHidPath(DualSense);

        Assert.Contains("vid&0002054c", key);
        Assert.Contains("pid&0ce6", key);
        Assert.Contains("15f755c8", key);
    }

    // What the duplicate reader came from: two enumerations of one pad must give one key.
    [Fact]
    public void TwoEnumerationsOfOneDualSenseGiveOneKey()
        => Assert.Equal(
            ControllerIdentityFactory.FromHidPath(DualSense),
            ControllerIdentityFactory.FromHidPath(DualSense.ToUpperInvariant()));

    [Fact]
    public void TwoDualSensesStillGetDifferentKeys()
        => Assert.NotEqual(
            ControllerIdentityFactory.FromHidPath(DualSense),
            ControllerIdentityFactory.FromHidPath(DualSense.Replace("15f755c8", "99aabbcc")));

    [Fact]
    public void ADualSenseKeyIsNotStableEither()
        => Assert.False(ControllerIdentityFactory.IsStable(ControllerIdentityFactory.FromHidPath(DualSense)));

    [Fact]
    public void TheKeyCarriesTheVendorAndProduct()
    {
        var key = ControllerIdentityFactory.FromHidPath(Col03);

        Assert.Contains("vid&0228de", key);
        Assert.Contains("pid&1303", key);
    }

    [Fact]
    public void TwoDifferentDevicesGetDifferentKeys()
    {
        var other = Col03.Replace("d1af66b5aa8d", "aabbccddeeff");

        Assert.NotEqual(
            ControllerIdentityFactory.FromHidPath(Col03),
            ControllerIdentityFactory.FromHidPath(other));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPathStillGivesAKey(string empty)
        => Assert.False(string.IsNullOrEmpty(ControllerIdentityFactory.FromHidPath(empty)));

    [Fact]
    public void AHidKeyIsNotStable()
        => Assert.False(ControllerIdentityFactory.IsStable(ControllerIdentityFactory.FromHidPath(Col03)));

    // The weakness is deliberate and marked: XInput offers a slot and nothing else, and slots are
    // handed out in connection order. Anything filed under one must be treated as provisional.
    [Fact]
    public void AnXInputKeyIsNotStable()
        => Assert.False(ControllerIdentityFactory.IsStable(ControllerIdentityFactory.FromXInputSlot(0)));

    [Fact]
    public void EachXInputSlotGetsItsOwnKey()
        => Assert.NotEqual(
            ControllerIdentityFactory.FromXInputSlot(0),
            ControllerIdentityFactory.FromXInputSlot(1));

    // What durability now means: a key built from something burned into the device.
    [Fact]
    public void ABluetoothAddressKeyIsStable()
        => Assert.True(ControllerIdentityFactory.IsStable("bt:44464836686d"));

    [Fact]
    public void AUsbSerialKeyIsStable()
        => Assert.True(ControllerIdentityFactory.IsStable("usb:vid_054c&pid_0ce6:a1b2c3d4"));
}