using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Sc2Xboxed.Windows;

public sealed partial class HidHideConfigurator
{
    private static readonly string[] DefaultCliPaths =
    {
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x86\HidHideCLI.exe"
    };

    private readonly string _cliPath;

    public HidHideConfigurator()
        : this(FindCliPath())
    {
    }

    public HidHideConfigurator(string cliPath)
    {
        _cliPath = cliPath;
    }

    public HidHideSetupResult SetupForSc2Xboxed(string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            throw new ArgumentException("Application path is required.", nameof(applicationPath));
        }

        var hiddenDevices = DiscoverValveSteamControllerDevicePaths();
        var commands = new List<string>
        {
            "--app-reg",
            applicationPath,
            "--inv-off"
        };

        foreach (var devicePath in hiddenDevices)
        {
            commands.Add("--dev-hide");
            commands.Add(devicePath);
        }

        commands.Add("--cloak-on");
        Run(commands);

        return new HidHideSetupResult(applicationPath, hiddenDevices);
    }

    public void DisableCloaking()
    {
        Run(new[] { "--cloak-off" });
    }

    public string GetStatus()
    {
        return Run(new[] { "--cloak-state", "--inv-state", "--app-list", "--dev-list" });
    }

    public IReadOnlyList<string> DiscoverValveSteamControllerDevicePaths()
    {
        var output = Run(new[] { "--dev-all" });
        return DeviceInstancePathRegex()
            .Matches(output)
            .Select(match => UnescapeCliString(match.Groups["path"].Value))
            .Where(IsSteamControllerDevicePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSteamControllerDevicePath(string devicePath)
    {
        return devicePath.Contains(@"VID_28DE", StringComparison.OrdinalIgnoreCase) &&
            (devicePath.Contains(@"PID_1302", StringComparison.OrdinalIgnoreCase) ||
             devicePath.Contains(@"PID_1303", StringComparison.OrdinalIgnoreCase) ||
             devicePath.Contains(@"PID_1304", StringComparison.OrdinalIgnoreCase));
    }

    private string Run(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cliPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start HidHide CLI at '{_cliPath}'.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"HidHideCLI exited with code {process.ExitCode}.{Environment.NewLine}{output}{error}");
        }

        return output + error;
    }

    private static string FindCliPath()
    {
        var path = DefaultCliPaths.FirstOrDefault(File.Exists);
        return path ?? throw new FileNotFoundException(
            "HidHideCLI.exe was not found. Install HidHide from Nefarius Software Solutions first.");
    }

    private static string UnescapeCliString(string value)
    {
        return value.Replace(@"\\", @"\", StringComparison.Ordinal);
    }

    [GeneratedRegex("\"deviceInstancePath\"\\s*:\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceInstancePathRegex();
}
