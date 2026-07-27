using System.IO;
using HidSharp;
using SteamXBox.Gui.Models;

namespace SteamXBox.Gui.Services;

public sealed class DeviceDetectionService : IDisposable
{
    private System.Threading.Timer? _timer;

    private const int ValveVendorId = 0x28DE;
    private static readonly HashSet<int> KnownControllerProducts =
    [
        0x1102, 0x1142, 0x1205, 0x1302, 0x1303, 0x1304
    ];

    public DeviceInfo CurrentDevice { get; private set; } = new() { IsConnected = false };
    public event Action<DeviceInfo>? DeviceChanged;

    public void StartPolling(int intervalMs = 3000)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, intervalMs);
    }

    public void StopPolling()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(ValveVendorId)
                .Where(d => KnownControllerProducts.Contains(d.ProductID))
                .ToList();

            if (devices.Count == 0)
            {
                var off = new DeviceInfo { IsConnected = false };
                CurrentDevice = off;
                DeviceChanged?.Invoke(off);
                return;
            }

            var dev = devices.First();
            var name = dev.GetProductName();
            if (string.IsNullOrWhiteSpace(name))
                name = $"Valve Controller (PID 0x{dev.ProductID:X4})";

            var info = new DeviceInfo
            {
                ProductName = name,
                ProductIdHex = $"0x{dev.ProductID:X4}",
                DevicePath = dev.DevicePath,
                CanOpen = dev.TryOpen(out _),
                IsConnected = true,
            };

            CurrentDevice = info;
            DeviceChanged?.Invoke(info);
        }
        catch
        {
            var off = new DeviceInfo { IsConnected = false };
            CurrentDevice = off;
            DeviceChanged?.Invoke(off);
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
