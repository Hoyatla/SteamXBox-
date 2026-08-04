using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SteamXBox.Shell.Devices;

/// <summary>Whether a kernel driver is installed, and which version.</summary>
/// <param name="Installed">True when the driver service is registered on this machine.</param>
/// <param name="Version">File version of the driver binary, or null when it could not be read.</param>
public readonly record struct DriverStatus(bool Installed, string? Version)
{
    public static readonly DriverStatus Missing = new(false, null);
}

/// <summary>
/// Detects the two kernel drivers SteamXBox depends on: ViGEmBus and HidHide.
/// </summary>
/// <remarks>
/// Detection is deliberately version-agnostic. It asks whether the driver service is registered and
/// then reports whatever version it finds; it never compares against a version SteamXBox was built
/// for. Both projects ship updates on their own schedule, and a check that recognised only the
/// versions known at build time would start reporting "not installed" the day a user updated —
/// the exact opposite of what a diagnostics screen is for. Anything the operating system will load
/// is something SteamXBox accepts.
///
/// The service key is the thing being tested, not the driver file. A driver can live anywhere the
/// installer put it, and future versions are free to move it; the service registration under
/// <c>CurrentControlSet\Services</c> is what makes it loadable and is stable across versions. The
/// file is only opened afterwards, to read a version string, and failing to find it does not change
/// the verdict.
/// </remarks>
public static class DriverDetection
{
    /// <summary>Service names registered by the two installers. Stable across their releases.</summary>
    private const string ViGEmBusService = "ViGEmBus";
    private const string HidHideService = "HidHide";

    public static DriverStatus ViGEmBus() => Detect(ViGEmBusService);

    public static DriverStatus HidHide() => Detect(HidHideService);

    private static DriverStatus Detect(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");

            if (key is null)
            {
                return DriverStatus.Missing;
            }

            // Installed. The version is a bonus: a driver whose file has been moved or whose path
            // cannot be resolved is still installed, and saying otherwise would be a lie.
            return new DriverStatus(true, ReadVersion(key.GetValue("ImagePath") as string, serviceName));
        }
        catch
        {
            // Reading HKLM can fail on a locked-down machine. Unknown is not the same as absent, but
            // a diagnostics line has only the two states, and claiming a driver is present when we
            // could not look is the more dangerous error.
            return DriverStatus.Missing;
        }
    }

    private static string? ReadVersion(string? imagePath, string serviceName)
    {
        foreach (var candidate in CandidatePaths(imagePath, serviceName))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var version = FileVersionInfo.GetVersionInfo(candidate).FileVersion;
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths(string? imagePath, string serviceName)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            // ImagePath for a kernel driver is an NT path: \SystemRoot\System32\drivers\x.sys, or
            // occasionally \??\C:\... for one installed outside the system tree.
            var path = imagePath.Trim();

            if (path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            {
                yield return path[4..];
            }
            else if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    path[@"\SystemRoot\".Length..]);
            }
            else if (Path.IsPathRooted(path))
            {
                yield return path;
            }
            else
            {
                // Relative paths are relative to the Windows directory.
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
            }
        }

        // Where both installers put their driver today, as a fallback when ImagePath is absent.
        yield return Path.Combine(system, "drivers", serviceName + ".sys");
    }
}
