using System.Diagnostics;
using System.Threading;

var baseDirectory = AppContext.BaseDirectory;
var coreExecutable = Path.Combine(baseDirectory, "SteamXBox.Core.exe");
var stateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SteamXBox");
var stopFile = Path.Combine(stateDirectory, "stop.requested");

if (!File.Exists(coreExecutable))
{
    Console.Error.WriteLine($"Missing required runtime file: {coreExecutable}");
    return 2;
}

if (args.Length > 0 && string.Equals(args[0], "stop", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(stateDirectory);
    File.WriteAllText(stopFile, "stop");
    KillRunningCore();
    return 0;
}

if (args.Length > 0)
{
    var exitCode = RunCore(coreExecutable, args);
    if (File.Exists(stopFile)) File.Delete(stopFile);
    return exitCode;
}

using var mutex = new Mutex(true, @"Local\SteamXBox", out var createdNew);
if (!createdNew)
{
    return 0;
}

if (File.Exists(stopFile)) File.Delete(stopFile);

while (true)
{
    if (File.Exists(stopFile)) { File.Delete(stopFile); return 0; }

    if (!HasControllerDevice(coreExecutable))
    {
        Thread.Sleep(5000);
        continue;
    }

    var core = StartCoreProcess(coreExecutable, ["xbox-run", "--switch-button", "quick-access"]);
    if (core is null) { Thread.Sleep(5000); continue; }

    core.WaitForExit();

    if (File.Exists(stopFile)) { File.Delete(stopFile); return 0; }

    Thread.Sleep(core.ExitCode == 0 ? 1000 : 5000);
}

static bool HasControllerDevice(string coreExecutable)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = coreExecutable,
            Arguments = "hid-list",
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi);
        if (proc is null) return false;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10000);
        return !output.Contains("No Valve HID device found.");
    }
    catch
    {
        return false;
    }
}

static Process? StartCoreProcess(string executable, string[] arguments)
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
    return Process.Start(startInfo);
}

static int RunCore(string executable, IReadOnlyList<string> arguments)
{
    var process = StartCoreProcess(executable, arguments.ToArray());
    if (process is null) return 3;
    process.WaitForExit();
    return process.ExitCode;
}

static void KillRunningCore()
{
    foreach (var process in Process.GetProcessesByName("SteamXBox.Core"))
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}
