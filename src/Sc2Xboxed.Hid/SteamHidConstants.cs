namespace Sc2Xboxed.Hid;

public static class SteamHidConstants
{
    public const int ValveVendorId = 0x28DE;
    public const int SteamController2015UsbProductId = 0x1102;
    public const int SteamController2015DongleProductId = 0x1142;
    public const int SteamDeckControllerProductId = 0x1205;
    public const int SteamController2026ProductId = 0x1302;
    public const int SteamController2026BluetoothProductId = 0x1303;
    public const int SteamPuckProductId = 0x1304;

    public static bool IsKnownSteamControllerProduct(int productId)
    {
        return productId is
            SteamController2015UsbProductId or
            SteamController2015DongleProductId or
            SteamDeckControllerProductId or
            SteamController2026ProductId or
            SteamController2026BluetoothProductId or
            SteamPuckProductId;
    }
}
