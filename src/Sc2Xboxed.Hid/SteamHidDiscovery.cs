using HidSharp;

namespace Sc2Xboxed.Hid;

public sealed class SteamHidDiscovery
{
    private readonly Action<string>? _log;

    public SteamHidDiscovery() : this(null) { }

    public SteamHidDiscovery(Action<string>? log)
    {
        _log = log;
    }

    private void Log(string msg) => _log?.Invoke($"[Discovery] {msg}");

    public IReadOnlyList<SteamHidDeviceInfo> ListValveDevices()
    {
        return DeviceList.Local
            .GetHidDevices(SteamHidConstants.ValveVendorId)
            .Select(ToInfo)
            .OrderByDescending(device => device.IsKnownSteamController)
            .ThenBy(device => device.ProductId)
            .ThenBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public HidDevice? FindPreferredControllerDevice()
    {
        var allValve = DeviceList.Local
            .GetHidDevices(SteamHidConstants.ValveVendorId)
            .ToArray();
        Log($"FindPreferred: {allValve.Length} total Valve HID devices");

        var candidates = allValve
            .Where(device =>
            {
                bool known = SteamHidConstants.IsKnownSteamControllerProduct(device.ProductID);
                if (!known) Log($"  Skipping PID=0x{device.ProductID:X4} (not a known product)");
                return known;
            })
            .Select(device =>
            {
                var c = new HidDeviceCandidate(
                    device,
                    SafeInt(device.GetMaxInputReportLength),
                    SafeInt(device.GetMaxOutputReportLength),
                    SafeInt(device.GetMaxFeatureReportLength),
                    CanOpen(device));
                Log($"  Candidate PID=0x{device.ProductID:X4} input={c.InputReportLength} output={c.OutputReportLength} feature={c.FeatureReportLength} canOpen={c.CanOpen} isControllerState={c.IsControllerStateInterface} path={device.DevicePath}");
                return c;
            })
            .ToArray();

        var preferred = candidates
            .Where(candidate => candidate.IsControllerStateInterface)
            .OrderByDescending(candidate => candidate.CanOpen)
            .ThenByDescending(candidate => candidate.Device.ProductID == SteamHidConstants.SteamController2026ProductId)
            .ThenByDescending(candidate => candidate.Device.ProductID == SteamHidConstants.SteamController2026BluetoothProductId)
            .ThenByDescending(candidate => candidate.Device.ProductID == SteamHidConstants.SteamPuckProductId)
            .ThenByDescending(candidate => candidate.OutputReportLength > 0)
            .ThenByDescending(candidate => candidate.FeatureReportLength > 0)
            .ThenByDescending(candidate => candidate.InputReportLength)
            .Select(candidate => candidate.Device)
            .FirstOrDefault();

        if (preferred is not null)
        {
            Log($"  Preferred: PID=0x{preferred.ProductID:X4} path={preferred.DevicePath}");
            return preferred;
        }

        Log("  No candidate passed IsControllerStateInterface filter. Trying all openable Valve devices...");

        var fallback = candidates
            .Where(candidate => candidate.CanOpen && candidate.InputReportLength > 0)
            .OrderByDescending(candidate => candidate.Device.ProductID == SteamHidConstants.SteamController2026ProductId)
            .ThenByDescending(candidate => candidate.Device.ProductID == SteamHidConstants.SteamController2026BluetoothProductId)
            .ThenByDescending(candidate => candidate.InputReportLength)
            .Select(candidate => candidate.Device)
            .FirstOrDefault();

        if (fallback is not null)
        {
            Log($"  Fallback: PID=0x{fallback.ProductID:X4} path={fallback.DevicePath}");
            return fallback;
        }

        Log("  No openable device found at all.");
        return null;
    }

    private static SteamHidDeviceInfo ToInfo(HidDevice device)
    {
        var canOpen = false;
        string? openError = null;

        try
        {
            if (device.TryOpen(out HidStream stream))
            {
                canOpen = true;
                stream.Dispose();
            }
            else
            {
                openError = "TryOpen returned false.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            openError = exception.Message;
        }

        return new SteamHidDeviceInfo(
            device.VendorID,
            device.ProductID,
            device.ReleaseNumber.ToString(),
            SafeString(device.GetProductName),
            SafeString(device.GetManufacturer),
            SafeString(device.GetSerialNumber),
            device.DevicePath,
            SafeInt(device.GetMaxInputReportLength),
            SafeInt(device.GetMaxOutputReportLength),
            SafeInt(device.GetMaxFeatureReportLength),
            canOpen,
            openError);
    }

    private static bool HasUsableInput(HidDevice device)
    {
        return SafeInt(device.GetMaxInputReportLength) > 0;
    }

    private static bool CanOpen(HidDevice device)
    {
        try
        {
            if (!device.TryOpen(out HidStream stream))
            {
                return false;
            }

            stream.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeString(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static int SafeInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }

    private sealed record HidDeviceCandidate(
        HidDevice Device,
        int InputReportLength,
        int OutputReportLength,
        int FeatureReportLength,
        bool CanOpen)
    {
        public bool IsControllerStateInterface =>
            InputReportLength >= 54 &&
            OutputReportLength > 0 &&
            FeatureReportLength > 0;
    }
}
