using System.IO;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Raises a flag when the active profile file changes on disk, so settings can be applied without
/// restarting the core process.
/// </summary>
/// <remarks>
/// The editor's Apply button used to require a full stop and start before anything took effect,
/// which made tuning by feel impractical.
/// </remarks>
public sealed class ProfileFileWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action<string>? _log;
    private int _pendingTick;
    private volatile bool _dirty;

    public ProfileFileWatcher(string profileName, Action<string>? log = null)
    {
        _log = log;

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamXBox", "profiles");
            Directory.CreateDirectory(directory);

            _watcher = new FileSystemWatcher(directory, $"{profileName}.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Renamed += OnChanged;
        }
        catch (Exception exception)
        {
            _log?.Invoke($"profile watcher unavailable: {exception.GetType().Name}: {exception.Message}");
            _watcher = null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        _dirty = true;
        _pendingTick = Environment.TickCount;
    }

    /// <summary>
    /// True once the file has changed and then stayed quiet briefly. The settle delay matters because
    /// a single save raises several events and can be observed mid-write, which would parse a
    /// truncated file.
    /// </summary>
    public bool TryConsumeChange(int settleMilliseconds = 250)
    {
        if (!_dirty)
        {
            return false;
        }

        if (Environment.TickCount - _pendingTick < settleMilliseconds)
        {
            return false;
        }

        _dirty = false;
        return true;
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Renamed -= OnChanged;
            _watcher.Dispose();
        }
        catch { }
    }
}
