using HidSharp;

namespace Sc2Xboxed.Hid;

public sealed class SteamHidDiscovery
{
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
        return DeviceList.Local
            .GetHidDevices(SteamHidConstants.ValveVendorId)
            .Where(device => SteamHidConstants.IsKnownSteamControllerProduct(device.ProductID))
            .Select(device => new HidDeviceCandidate(
                device,
                SafeInt(device.GetMaxInputReportLength),
                SafeInt(device.GetMaxOutputReportLength),
                SafeInt(device.GetMaxFeatureReportLength),
                CanOpen(device)))
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return $"<unavailable: {exception.GetType().Name}>";
        }
    }

    private static int SafeInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
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
