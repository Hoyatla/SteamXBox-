using HidSharp;

namespace Sc2Xboxed.Hid;

public sealed class SteamHidProbe
{
    private readonly SteamHidDiscovery _discovery;

    public SteamHidProbe()
        : this(new SteamHidDiscovery())
    {
    }

    public SteamHidProbe(SteamHidDiscovery discovery)
    {
        _discovery = discovery;
    }

    public IReadOnlyList<HidInputReportSnapshot> CaptureInputReports(
        TimeSpan duration,
        int maxReports = 64,
        int readTimeoutMs = 100)
    {
        var device = _discovery.FindPreferredControllerDevice()
            ?? throw new InvalidOperationException("No known Valve Steam Controller HID interface was found.");

        if (!device.TryOpen(out HidStream stream))
        {
            throw new IOException($"Unable to open HID device {device.DevicePath}.");
        }

        using (stream)
        {
            stream.ReadTimeout = readTimeoutMs;

            var reportLength = Math.Max(1, device.GetMaxInputReportLength());
            var buffer = new byte[reportLength];
            var deadline = DateTimeOffset.UtcNow + duration;
            var reports = new List<HidInputReportSnapshot>(maxReports);

            while (DateTimeOffset.UtcNow < deadline && reports.Count < maxReports)
            {
                try
                {
                    var bytesRead = stream.Read(buffer);
                    if (bytesRead <= 0)
                    {
                        continue;
                    }

                    var copy = new byte[bytesRead];
                    Array.Copy(buffer, copy, bytesRead);
                    reports.Add(new HidInputReportSnapshot(DateTimeOffset.UtcNow, copy));
                }
                catch (TimeoutException)
                {
                    // A timeout is expected during probing when the controller is idle.
                }
            }

            return reports;
        }
    }
}
