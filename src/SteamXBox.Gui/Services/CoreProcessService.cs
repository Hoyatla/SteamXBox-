using System.Diagnostics;
using System.IO;
using SteamXBox.Gui.Models;

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
            var xboxProfile = App.SettingsSvc.Settings.LastXboxProfile;
            var args = $"xbox-run --restart --start-mode {profile.Mode.ToLower()} --switch-button {profile.SwitchButton} "
                     + $"--profile \"{profile.Name}\" --xbox-profile \"{xboxProfile}\"";

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

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
