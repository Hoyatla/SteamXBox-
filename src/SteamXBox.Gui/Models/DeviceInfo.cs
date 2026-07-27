namespace SteamXBox.Gui.Models;

public sealed class DeviceInfo
{
    public string ProductName { get; init; } = "";
    public string ProductIdHex { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public bool CanOpen { get; init; }
    public bool IsConnected { get; init; }

    public string DisplayName => string.IsNullOrEmpty(ProductName)
        ? "Aucun device"
        : $"{ProductName} ({ProductIdHex})";
}
