using System.Diagnostics;
using System.IO;
using SteamXBox.Gui.Models;

namespace SteamXBox.Gui.Services;

public sealed class DeviceDetectionService : IDisposable
{
    private System.Threading.Timer? _timer;
    private readonly CoreProcessService _core;

    public DeviceInfo CurrentDevice { get; private set; } = new() { IsConnected = false };
    public event Action<DeviceInfo>? DeviceChanged;

    public DeviceDetectionService(CoreProcessService core)
    {
        _core = core;
    }

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
            var corePath = _core.GetCorePath();
            if (!File.Exists(corePath)) return;

            var psi = new ProcessStartInfo
            {
                FileName = corePath,
                Arguments = "hid-list",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var connected = !output.Contains("No Valve HID device found");
            var productName = "";
            var productId = "";
            var devicePath = "";

            if (connected)
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Product:"))
                        productName = trimmed["Product:".Length..].Trim();
                    else if (trimmed.StartsWith("Product ID:"))
                        productId = trimmed["Product ID:".Length..].Trim();
                    else if (trimmed.StartsWith("Device Path:"))
                        devicePath = trimmed["Device Path:".Length..].Trim();
                    else if (trimmed.StartsWith("  Product:"))
                        productName = trimmed["  Product:".Length..].Trim();
                }
            }

            var device = new DeviceInfo
            {
                ProductName = productName,
                ProductIdHex = productId,
                DevicePath = devicePath,
                CanOpen = connected,
                IsConnected = connected,
            };

            CurrentDevice = device;
            DeviceChanged?.Invoke(device);
        }
        catch
        {
            var disconnected = new DeviceInfo { IsConnected = false };
            CurrentDevice = disconnected;
            DeviceChanged?.Invoke(disconnected);
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
