using System.IO;
using HidSharp;

namespace SenSÉ.Shell.Devices;

public sealed class DeviceDetectionService : IDisposable
{
    private System.Threading.Timer? _timer;

    private const int ValveVendorId = 0x28DE;
    private static readonly HashSet<int> KnownControllerProducts =
    [
        0x1102, 0x1142, 0x1205, 0x1302, 0x1303, 0x1304
    ];

    private const int SonyVendorId = 0x054C;
    private const int DualSenseProductId = 0x0CE6;

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

            // The other two families. This service decided whether a controller was connected and
            // only ever looked for Valve devices, so with a DualSense or an Xbox pad in hand it
            // reported "disconnected" — and everything downstream inherited that: the status card,
            // the strip refresh, and "start automatically when the controller is detected", which
            // could therefore never fire for the two families most likely to want it.
            var dualSense = DeviceList.Local.GetHidDevices(SonyVendorId, DualSenseProductId)
                .Where(d => d.GetMaxInputReportLength() >= 10)
                .ToList();

            var xinputSlots = SenSÉ.Windows.XInputControllerSource.ConnectedSlots();

            if (devices.Count == 0 && dualSense.Count == 0 && xinputSlots.Count == 0)
            {
                var off = new DeviceInfo { IsConnected = false };
                CurrentDevice = off;
                DeviceChanged?.Invoke(off);
                return;
            }

            // A Valve controller still takes precedence in the name shown, because it is the one
            // with trackpads and haptics and the only one whose model matters to the settings. The
            // other two only need to make "connected" true.
            if (devices.Count == 0)
            {
                var other = dualSense.Count > 0
                    ? new DeviceInfo { IsConnected = true, ProductName = "Manette PS5 (DualSense)" }
                    : new DeviceInfo { IsConnected = true, ProductName = $"Manette Xbox (slot {xinputSlots[0] + 1})" };

                CurrentDevice = other;
                DeviceChanged?.Invoke(other);
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
