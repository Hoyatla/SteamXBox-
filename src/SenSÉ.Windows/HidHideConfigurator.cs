using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SenSÉ.Windows;

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

    /// <summary>Whether HidHide is installed on this machine.</summary>
    /// <remarks>
    /// Asked before constructing one, because the constructor throws when the CLI is missing and
    /// "HidHide is not installed" is an ordinary state to report, not an error to handle.
    /// </remarks>
    public static bool IsInstalled => DefaultCliPaths.Any(File.Exists);

    /// <summary>
    /// Puts HidHide back to a state where nothing is hidden.
    /// </summary>
    /// <remarks>
    /// Called when a session starts, before anything is hidden, and it is the insurance policy for
    /// the whole feature. Cloaking outlives the process that asked for it — it survives a crash, a
    /// force-kill and a reboot — so a session that ends badly leaves a controller invisible to every
    /// game on the machine, with nothing on screen to say why. Clearing at startup means the next
    /// launch repairs it, which turns "my controller stopped working everywhere" into something the
    /// user fixes without knowing this feature exists.
    ///
    /// <para>
    /// Every device is unhidden, not only the ones this build would have hidden. A list written by an
    /// older version, or by the user's own HidHide configuration, is exactly what a reset is for.
    /// </para>
    /// </remarks>
    public void Unhide(IReadOnlyList<string> deviceInstancePaths, bool turnCloakOff)
    {
        foreach (var devicePath in deviceInstancePaths)
        {
            Run(new[] { "--dev-unhide", devicePath });
        }

        if (turnCloakOff)
        {
            Run(new[] { "--cloak-off" });
        }
    }

    /// <summary>Whether HidHide is currently cloaking anything at all.</summary>
    public bool IsCloakOn() => Run(new[] { "--cloak-state" }).Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);

    /// <summary>The applications allowed to see what is hidden.</summary>
    public IReadOnlyList<string> AllowedApplications()
        => ApplicationPathRegex()
            .Matches(Run(new[] { "--app-list" }))
            .Select(match => UnescapeCliString(match.Groups["path"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Takes an application back off the allowed list.</summary>
    public void Forget(string applicationPath) => Run(new[] { "--app-unreg", applicationPath });

    /// <summary>
    /// Removes the allowed-list entries that cannot refer to anything.
    /// </summary>
    /// <remarks>
    /// <b>The allowed list is written to and never read back.</b> An entry is added on every launch
    /// and nothing has ever removed one, so the driver accumulates a line per install path, for ever
    /// — measured on the development machine: an entry for a version uninstalled weeks earlier, and
    /// an <b>empty</b> one.
    ///
    /// <para>
    /// <b>Only the empty ones.</b> Removing every entry whose file is missing was the first rule
    /// here and it was too eager: the development machine has an entry for a third-party DualSense
    /// tool that is not installed at that path today, and deleting it would be quietly undoing
    /// somebody's configuration because they happened to move a program. An entry with no path at
    /// all can never match anything and belongs to nobody, which is the only case that is certain.
    /// </para>
    ///
    /// <para>
    /// <b>What was removed is what is reported, and that is checked.</b> The first version returned
    /// what it had asked to remove: the tool refuses <c>--app-unreg ""</c> with "the file name should
    /// include a full path" <b>and exits with code zero</b>, so nothing threw, and the log announced
    /// a cleanup that had not happened. The list is read again afterwards and only the entries that
    /// actually went are named.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> ForgetDeadApplications()
    {
        var before = AllowedApplications();
        var rubbish = before.Where(path => path.Trim().Length == 0).ToArray();

        if (rubbish.Length == 0)
        {
            return [];
        }

        foreach (var path in rubbish)
        {
            Forget(path);
        }

        var after = AllowedApplications();

        return [.. rubbish.Where(path => !after.Contains(path, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Whether anything on the allowed list cannot refer to a file.
    /// </summary>
    /// <remarks>
    /// Kept separate from the removal because the removal cannot always succeed. An empty entry is
    /// not removable through the command-line tool at all — it rejects the argument — so it can only
    /// be reported and taken out through HidHide's own window. Saying so is better than a cleanup
    /// that claims a result it did not get.
    /// </remarks>
    public IReadOnlyList<string> UnremovableRubbish()
        => [.. AllowedApplications().Where(path => path.Trim().Length == 0)];

    /// <summary>What HidHide is currently hiding.</summary>
    public IReadOnlyList<string> HiddenDevicePaths()
        => DeviceInstancePathRegex()
            .Matches(Run(new[] { "--dev-list" }))
            .Select(match => UnescapeCliString(match.Groups["path"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Hides exactly these devices from every application but this one.
    /// </summary>
    /// <remarks>
    /// An explicit list, never a family of vendor and product identifiers, and that was a measured
    /// decision rather than a careful one. A list by model cannot hide a third-party pad — this
    /// machine has one reporting as <c>VID_1532&amp;PID_0A43</c>, which no list of Microsoft and Sony
    /// products would ever match — and it would happily hide the wrong thing, since Microsoft
    /// keyboards and mice share the vendor identifier of Xbox pads, and the virtual pad SenSÉ
    /// creates for games presents itself as an Xbox pad too.
    ///
    /// <para>
    /// The caller passes the devices it actually opened. Nothing else can be hidden, so nothing else
    /// can be hidden by mistake.
    /// </para>
    /// </remarks>
    /// <param name="turnCloakOn">
    /// Whether to switch cloaking on as well as adding to the list. Defaults to switching it on when
    /// there is something new to add, which is not the same question — see the remark.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The list and the switch are two different things.</b> A device named by <c>--dev-hide</c>
    /// is on a list; it is hidden from anything only while cloaking is on. Turning cloaking off does
    /// not clear the list, so a machine can carry a list of four devices and hide none of them.
    /// </para>
    /// <para>
    /// The caller therefore has to be able to say "add nothing, but make sure the switch is on":
    /// when every device SenSÉ holds is already on the list and cloaking is off, none of them is
    /// hidden, and adding nothing is exactly the right amount to add.
    /// </para>
    /// </remarks>
    public HidHideSetupResult HideOnly(
        string applicationPath,
        IReadOnlyList<string> deviceInstancePaths,
        bool? turnCloakOn = null)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            throw new ArgumentException("Application path is required.", nameof(applicationPath));
        }

        var commands = new List<string> { "--app-reg", applicationPath, "--inv-off" };

        foreach (var devicePath in deviceInstancePaths)
        {
            commands.Add("--dev-hide");
            commands.Add(devicePath);
        }

        if (turnCloakOn ?? deviceInstancePaths.Count > 0)
        {
            commands.Add("--cloak-on");
        }

        Run(commands);

        return new HidHideSetupResult(applicationPath, deviceInstancePaths);
    }

    public HidHideSetupResult SetupForSenSÉ(string applicationPath)
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

        // Both pipes are drained at once, and that is not a style choice. Reading one to the end
        // and only then the other is the textbook deadlock: the child blocks writing to the pipe
        // nobody is emptying, the parent blocks reading the pipe the child has stopped filling, and
        // neither ever moves. A pipe buffer is about four kilobytes, so it needs only a talkative
        // error stream — and this runs at every launch, before the controllers are opened.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        // Bounded as well. A tool that never exits must not become a SenSÉ that never starts,
        // and thirty seconds is far beyond anything this tool legitimately takes.
        if (!process.WaitForExit(30_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone, or not ours to kill. The throw below is what matters.
            }

            throw new InvalidOperationException(
                $"HidHideCLI did not answer within thirty seconds and was stopped: {string.Join(' ', arguments)}");
        }

        Task.WaitAll(output, error);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"HidHideCLI exited with code {process.ExitCode}.{Environment.NewLine}{output.Result}{error.Result}");
        }

        return output.Result + error.Result;
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

    /// <summary>
    /// A device path in whatever shape the tool answered in.
    /// </summary>
    /// <remarks>
    /// <b>Two shapes, because the installed tool does not use the one this expected.</b> It was
    /// written for the JSON form, and the HidHide on the development machine answers
    /// <c>--dev-list</c> with lines of <c>--dev-hide "PATH"</c> — so the list came back empty every
    /// time, silently.
    ///
    /// <para>
    /// That was not a cosmetic failure. <see cref="HideOnly"/>'s caller uses this list to tell what
    /// is already hidden from what it is about to hide; an empty answer made every device look
    /// newly hidden, including the ones the <b>user</b> had hidden in their own configuration. Those
    /// were then written into SenSÉ's note as its own, and given back on the way out. The class
    /// promises never to touch a user's configuration, and this quietly broke that promise.
    /// </para>
    ///
    /// <para>
    /// Both forms are accepted rather than the new one alone: which shape a given HidHide answers in
    /// is not something this code gets to decide, and being wrong about it fails silently.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        "(?:--dev-hide\\s+|\"deviceInstancePath\"\\s*:\\s*)\"(?<path>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex DeviceInstancePathRegex();

    /// <summary>An allowed application, in either shape.</summary>
    [GeneratedRegex(
        "(?:--app-reg\\s+|\"applicationPath\"\\s*:\\s*)\"(?<path>[^\"]*)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex ApplicationPathRegex();
}
