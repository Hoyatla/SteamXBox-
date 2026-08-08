using System.Diagnostics;
using System.IO;

namespace SteamXBox.Gui.Services;

public sealed class CoreProcessService : IDisposable
{
    private Process? _coreProcess;
    private int _runningPid;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning
    {
        get { lock (_lock) return _coreProcess != null && !_coreProcess.HasExited; }
    }

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;

    /// <summary>
    /// Takes ownership of a bridge that was already running, if there is one.
    /// </summary>
    /// <remarks>
    /// The configuration window is usually opened onto a bridge somebody else started — the
    /// environment brings it up at boot, and the Controller tile only opens a view onto it. Without
    /// this the window assumed nothing was running: the button offered "Démarrer" over a working
    /// bridge, there was no way to stop it, and pressing Start restarted what was already fine.
    ///
    /// Its standard output cannot be captured after the fact, so the log pane stays empty for a
    /// bridge adopted this way. That is the honest trade: knowing it runs and being able to stop it
    /// matters more than watching lines scroll, and the bridge writes its own log file regardless.
    /// </remarks>
    /// <returns>True when a running bridge was found and adopted.</returns>
    public bool AdoptRunningInstance()
    {
        lock (_lock)
        {
            if (_coreProcess is { HasExited: false })
            {
                return true;
            }

            try
            {
                var self = Environment.ProcessId;
                var found = Process.GetProcessesByName("SteamXBox.Core")
                    .FirstOrDefault(p => p.Id != self);

                if (found is null)
                {
                    return false;
                }

                _coreProcess = found;
                _runningPid = found.Id;

                found.EnableRaisingEvents = true;
                found.Exited += (_, _) => ProcessExited?.Invoke(found.ExitCode);

                OutputReceived?.Invoke($"[INFO] Core déjà en cours (PID {found.Id}) — repris en charge");
                return true;
            }
            catch
            {
                // Access to another process can be refused; then it simply is not adopted.
                return false;
            }
        }
    }

    public string GetCorePath()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var corePath = Path.Combine(dir, "SteamXBox.Core.exe");
        if (File.Exists(corePath))
            return corePath;

        var alt = Path.Combine(dir, "..", "SteamXBox.Core.exe");
        return Path.GetFullPath(alt);
    }

    public bool Start(ProfileData profile)
    {
        lock (_lock)
        {
            if (IsRunning) return false;

            _cts = new CancellationTokenSource();
            var corePath = GetCorePath();

            if (!File.Exists(corePath))
            {
                OutputReceived?.Invoke($"[ERROR] Core introuvable: {corePath}");
                return false;
            }

            // Quoted: profile names are user-chosen and routinely contain spaces.
            var args = $"xbox-run --restart --start-mode {profile.Mode.ToLower()} --switch-button {profile.SwitchButton} "
                     + $"--profile \"{profile.Name}\"";

            var psi = new ProcessStartInfo
            {
                FileName = corePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _coreProcess = process;

            var pid = 0;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    OutputReceived?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    OutputReceived?.Invoke($"[ERR] {e.Data}");
            };

            process.Exited += (_, _) =>
            {
                int code = -1;
                try { code = process.ExitCode; } catch { }
                if (pid != 0 && pid == _runningPid)
                {
                    OutputReceived?.Invoke($"[INFO] Core arrêté (code {code})");
                    ProcessExited?.Invoke(code);
                }
                else
                {
                    OutputReceived?.Invoke($"[INFO] Core arrêté (code {code}) — ignoré (processus suivant déjà démarré)");
                }
            };

            try
            {
                process.Start();
                pid = process.Id;
                _runningPid = pid;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                OutputReceived?.Invoke($"[INFO] Core démarré (PID {pid})");
                return true;
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke($"[ERROR] Impossible de démarrer Core: {ex.Message}");
                _coreProcess = null;
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_coreProcess == null) return;

            try
            {
                OutputReceived?.Invoke("[INFO] Arrêt du Core...");

                var stopFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SteamXBox", "stop.requested");
                Directory.CreateDirectory(Path.GetDirectoryName(stopFile)!);
                File.WriteAllText(stopFile, "");

                if (!_coreProcess.HasExited)
                {
                    _coreProcess.Kill(entireProcessTree: true);
                    _coreProcess.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke($"[WARN] Erreur arrêt: {ex.Message}");
            }
            finally
            {
                _runningPid = 0;
                _coreProcess?.Dispose();
                _coreProcess = null;
            }
        }
    }

    /// <summary>
    /// Releases the handles. <b>Does not stop the bridge.</b>
    /// </summary>
    /// <remarks>
    /// Closing the configuration window must leave the controllers working. This used to stop the
    /// core, which was harmless only because the window owned nothing: it had not started the bridge,
    /// so there was no process to stop. Making Stop able to act on a bridge somebody else started —
    /// which is what the button needed — handed that same power to the window's disposal, and
    /// closing the settings began disconnecting every controller.
    ///
    /// The lifecycle belongs to the environment, which starts the bridge with itself and stops it
    /// when it closes. The only deliberate stop from here is the button.
    /// </remarks>
    public void Dispose()
    {
        lock (_lock)
        {
            _coreProcess?.Dispose();
            _coreProcess = null;
            _runningPid = 0;
        }

        _cts?.Dispose();
    }
}
