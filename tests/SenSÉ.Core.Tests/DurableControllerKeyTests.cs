using SenSÉ.Core.Input;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The key a controller can still be recognised by tomorrow.
/// </summary>
/// <remarks>
/// The identifiers here are the ones the author's machine actually produced. The previous attempt
/// used the HID interface path, whose instance segment Windows reissues on every Bluetooth
/// reconnection — so a profile filed under it could never find its controller again, and nothing
/// said so.
/// </remarks>
public class DurableControllerKeyTests
{
    // A DualSense over Bluetooth, as enumerated on the author's machine.
    private const string BluetoothNode = @"BTHENUM\DEV_44464836686D\7&1B869C0E&1&BLUETOOTHDEVICE_44464836686D";

    // Its HID interface, whose instance segment changes on every reconnection.
    private const string HidInterface =
        @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\8&15F755C8&3&0000";

    [Fact]
    public void TheBluetoothAddressIsTheDurableKey()
        => Assert.Equal("bt:44464836686d", DurableControllerKey.From([HidInterface, BluetoothNode]));

    // The point of the whole class: the HID instance changes, the parent does not.
    [Fact]
    public void ANewHidInstanceStillGivesTheSameKey()
    {
        var reconnected = HidInterface.Replace("8&15F755C8&3&0000", "8&56468DC&4&0000");

        Assert.Equal(
            DurableControllerKey.From([HidInterface, BluetoothNode]),
            DurableControllerKey.From([reconnected, BluetoothNode]));
    }

    [Fact]
    public void TwoPadsWithDifferentAddressesGetDifferentKeys()
        => Assert.NotEqual(
            DurableControllerKey.From([BluetoothNode]),
            DurableControllerKey.From([BluetoothNode.Replace("44464836686D", "90B685F7696F")]));

    [Fact]
    public void TheKeyIgnoresCase()
        => Assert.Equal(
            DurableControllerKey.From([BluetoothNode]),
            DurableControllerKey.From([BluetoothNode.ToLowerInvariant()]));

    [Fact]
    public void TheNearestDurableAncestorWins()
        => Assert.StartsWith("bt:", DurableControllerKey.From([HidInterface, BluetoothNode, @"ROOT\SYSTEM\0000"])!);

    [Fact]
    public void AUsbSerialIsDurableToo()
        => Assert.Equal("usb:vid_054c&pid_0ce6:a1b2c3d4", DurableControllerKey.From([@"USB\VID_054C&PID_0CE6\A1B2C3D4"]));

    // Windows fabricates a serial containing "&" when the device reports none; it is tied to the
    // physical port, so the same pad in another socket would look like a new controller.
    [Fact]
    public void AFabricatedUsbSerialIsRefused()
        => Assert.Null(DurableControllerKey.From([@"USB\VID_054C&PID_0CE6\7&2B8E121E&0&2"]));

    // A key invented from a non-durable identifier is worse than none: it persists, it looks right,
    // and it silently attaches one player's settings to another's pad.
    [Fact]
    public void NothingDurableGivesNoKey()
        => Assert.Null(DurableControllerKey.From([HidInterface, @"ROOT\SYSTEM\0000"]));

    [Fact]
    public void AnEmptyChainGivesNoKey()
        => Assert.Null(DurableControllerKey.From([]));

    [Fact]
    public void BlankEntriesAreSkipped()
        => Assert.Equal("bt:44464836686d", DurableControllerKey.From(["", "   ", BluetoothNode]));

    // A MAC is twelve hexadecimal digits; a shorter run is a different field that starts the same
    // way, and accepting it would produce keys that collide between devices.
    [Fact]
    public void AShortHexRunIsNotAnAddress()
        => Assert.Null(DurableControllerKey.From([@"BTHENUM\DEV_ABCD\7&1"]));

    // The shape a HID interface's parent actually has on the author's machine — the address is one
    // field among several, with no DEV_ marker anywhere. Searching for that marker found nothing,
    // which would have been read as "this pad has no durable identity".
    private const string RealParent =
        @"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\7&1B869C0E&1&44464836686D_C00000000";

    [Fact]
    public void TheRealBluetoothParentYieldsTheAddress()
        => Assert.Equal("bt:44464836686d", DurableControllerKey.From([RealParent]));

    [Fact]
    public void TheRealParentSurvivesAReconnection()
        => Assert.Equal(
            DurableControllerKey.From([HidInterface, RealParent]),
            DurableControllerKey.From([HidInterface.Replace("8&15F755C8&3&0000", "8&56468DC&4&0000"), RealParent]));

    // The vendor and product identifiers sit in an earlier segment and are shared by every pad of
    // the same model; only the last segment may be searched.
    [Fact]
    public void TheVendorFieldsAreNotMistakenForAnAddress()
        => Assert.Equal("bt:44464836686d", DurableControllerKey.From([RealParent.Replace("44464836686D", "90B685F7696F").Replace("90B685F7696F", "44464836686D")]));

    [Fact]
    public void ANonBluetoothNodeIsNotSearchedForAnAddress()
        => Assert.Null(DurableControllerKey.From([@"PCI\VEN_8086&DEV_7A60&SUBSYS_50071458&REV_11\3&11583659&0&A0"]));
}