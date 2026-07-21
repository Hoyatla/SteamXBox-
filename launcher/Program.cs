using System.Diagnostics;
using System.Threading;

var baseDirectory = AppContext.BaseDirectory;
var coreExecutable = Path.Combine(baseDirectory, "SteamXBox.Core.exe");
var residentLauncher = Path.Combine(baseDirectory, "SteamXBox-Resident.cmd");

if (!File.Exists(coreExecutable))
{
    Console.Error.WriteLine($"Missing required runtime file: {coreExecutable}");
    return 2;
}

if (args.Length == 0)
{
    return RunResidentOnce(residentLauncher, [
        "xbox-run",
        "--restart",
        "--switch-button",
        "steam-or-quick-access"
    ]);
}

if (IsStopCommand(args[0]))
{
    RequestResidentStop();
    return RunCore(coreExecutable, args);
}

if (IsXboxRunCommand(args[0]))
{
    return RunResidentOnce(residentLauncher, args);
}

return RunCore(coreExecutable, args);

static bool IsStopCommand(string command)
{
    return string.Equals(command, "stop", StringComparison.OrdinalIgnoreCase);
}

static bool IsXboxRunCommand(string command)
{
    return string.Equals(command, "xbox-run", StringComparison.OrdinalIgnoreCase);
}

static int RunCore(string executable, IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = executable,
        WorkingDirectory = Path.GetDirectoryName(executable)!,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"Failed to start {executable}");
        return 3;
    }

    process.WaitForExit();
    return process.ExitCode;
}

static int RunResidentOnce(string residentLauncher, IReadOnlyList<string> arguments)
{
    using var mutex = new Mutex(true, @"Local\SteamXBoxResidentLauncher", out var createdNew);
    if (!createdNew)
    {
        return 0;
    }

    return RunResident(residentLauncher, arguments);
}

static int RunResident(string residentLauncher, IReadOnlyList<string> arguments)
{
    if (!File.Exists(residentLauncher))
    {
        Console.Error.WriteLine($"Missing resident launcher: {residentLauncher}");
        return 2;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = "cmd.exe",
        WorkingDirectory = Path.GetDirectoryName(residentLauncher)!,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    startInfo.ArgumentList.Add("/d");
    startInfo.ArgumentList.Add("/c");
    startInfo.ArgumentList.Add(residentLauncher);

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"Failed to start {residentLauncher}");
        return 3;
    }

    process.WaitForExit();
    return process.ExitCode;
}

static void RequestResidentStop()
{
    var stateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox");

    Directory.CreateDirectory(stateDirectory);
    File.WriteAllText(Path.Combine(stateDirectory, "stop.requested"), "stop");
}
