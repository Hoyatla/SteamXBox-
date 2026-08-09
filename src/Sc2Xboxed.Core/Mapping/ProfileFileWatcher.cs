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

    /// <summary>
    /// Watches every profile on disk, and remembers which ones changed.
    /// </summary>
    /// <remarks>
    /// One file used to be watched — the profile the bridge was launched with — and any change to it
    /// rebuilt every controller's session. That is wrong in both directions: editing the Steam
    /// Controller's profile reset the DualSense's chord timers, its trackball inertia and its
    /// sub-pixel carry, while editing the DualSense's own profile was not noticed at all.
    /// </remarks>
    public ProfileFileWatcher(Action<string>? log = null)
    {
        _log = log;

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamXBox", "profiles");
            Directory.CreateDirectory(directory);

            _watcher = new FileSystemWatcher(directory, "*.json")
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
        lock (_changed)
        {
            _changed.Add(Path.GetFileNameWithoutExtension(e.Name) ?? "");
        }

        _dirty = true;
        _pendingTick = Environment.TickCount;
    }

    /// <summary>Profile names touched since the last consumption.</summary>
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True once the file has changed and then stayed quiet briefly. The settle delay matters because
    /// a single save raises several events and can be observed mid-write, which would parse a
    /// truncated file.
    /// </summary>
    public bool TryConsumeChange(int settleMilliseconds = 250)
        => TryConsumeChange(out _, settleMilliseconds);

    /// <summary>
    /// The same, and says which profiles changed so only their controllers need rebuilding.
    /// </summary>
    /// <remarks>
    /// A set rather than a single name: one save raises several events, and two profiles can be
    /// saved inside the same settle window.
    /// </remarks>
    public bool TryConsumeChange(out IReadOnlyCollection<string> profileNames, int settleMilliseconds = 250)
    {
        profileNames = [];

        if (!_dirty)
        {
            return false;
        }

        if (Environment.TickCount - _pendingTick < settleMilliseconds)
        {
            return false;
        }

        lock (_changed)
        {
            profileNames = _changed.ToArray();
            _changed.Clear();
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
