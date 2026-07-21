namespace Sc2Xboxed.Hid;

public sealed record SteamHidDeviceInfo(
    int VendorId,
    int ProductId,
    string ReleaseNumber,
    string ProductName,
    string Manufacturer,
    string SerialNumber,
    string DevicePath,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength,
    bool CanOpen,
    string? OpenError)
{
    public bool IsValveDevice => VendorId == SteamHidConstants.ValveVendorId;

    public bool IsKnownSteamController => IsValveDevice && SteamHidConstants.IsKnownSteamControllerProduct(ProductId);

    public string ProductIdHex => $"0x{ProductId:X4}";
}
