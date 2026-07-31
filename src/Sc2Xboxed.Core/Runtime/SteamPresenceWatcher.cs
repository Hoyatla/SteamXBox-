using System.Diagnostics;

namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Tracks whether Steam is running and derives controller ownership from it, so SteamXBox hands
/// the controller over when Steam starts and takes it back when Steam closes — including when the
/// user closes Steam from Steam's own UI rather than through a SteamXBox shortcut.
/// </summary>
public sealed class SteamPresenceWatcher
{
    public const string SteamProcessName = "steam";

    /// <summary>
    /// Delay before reclaiming after Steam disappears. Steam relaunches itself during updates, so
    /// reclaiming instantly would make ownership flap.
    /// </summary>
    private static readonly TimeSpan DefaultReclaimGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Delay before giving up on a Steam launch we requested. Steam can take many seconds to show
    /// up in the process list, and reclaiming during that window would steal the controller back
    /// from under it.
    /// </summary>
    private static readonly TimeSpan DefaultLaunchGrace = TimeSpan.FromSeconds(25);

    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _reclaimGrace;
    private readonly TimeSpan _launchGrace;
    private readonly Func<bool> _isSteamRunning;

    private int _lastPollTick;
    private bool _hasPolled;
    private bool _steamObserved;
    private DateTimeOffset? _absentSince;

    public SteamPresenceWatcher(
        TimeSpan? pollInterval = null,
        TimeSpan? reclaimGrace = null,
        TimeSpan? launchGrace = null,
        Func<bool>? isSteamRunning = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _reclaimGrace = reclaimGrace ?? DefaultReclaimGrace;
        _launchGrace = launchGrace ?? DefaultLaunchGrace;
        _isSteamRunning = isSteamRunning ?? IsSteamProcessRunning;
    }

    public ControllerOwner Owner { get; private set; } = ControllerOwner.SteamXBox;

    /// <summary>Last observed process state, refreshed by <see cref="Poll"/>.</summary>
    public bool SteamRunning { get; private set; }

    /// <summary>
    /// Re-evaluates ownership, at most once per poll interval. Returns true when
    /// <see cref="Owner"/> changed, so callers can run the release/reclaim transition.
    /// </summary>
    public bool Poll(DateTimeOffset now)
    {
        int tick = Environment.TickCount;
        if (_hasPolled && tick - _lastPollTick < _pollInterval.TotalMilliseconds)
        {
            return false;
        }

        _lastPollTick = tick;
        _hasPolled = true;

        SteamRunning = _isSteamRunning();
        var previous = Owner;

        if (SteamRunning)
        {
            _steamObserved = true;
            _absentSince = null;
            Owner = ControllerOwner.Steam;
        }
        else if (Owner == ControllerOwner.Steam)
        {
            _absentSince ??= now;

            // A launch we requested but never saw start gets the longer grace period.
            var grace = _steamObserved ? _reclaimGrace : _launchGrace;
            if (now - _absentSince.Value >= grace)
            {
                Owner = ControllerOwner.SteamXBox;
                _absentSince = null;
                _steamObserved = false;
            }
        }

        return Owner != previous;
    }

    /// <summary>
    /// Hands ownership to Steam immediately, before the process is observable. Used when the user
    /// asks SteamXBox to launch Steam: waiting for the next poll would keep SteamXBox writing to
    /// the device while Steam is starting up.
    /// </summary>
    public void HandOverToSteam(DateTimeOffset now)
    {
        Owner = ControllerOwner.Steam;
        _steamObserved = false;
        _absentSince = now;
    }

    /// <summary>
    /// Takes ownership back immediately, skipping the grace period. Used after SteamXBox itself
    /// killed Steam, where there is nothing to wait for.
    /// </summary>
    public void TakeOwnership()
    {
        Owner = ControllerOwner.SteamXBox;
        _steamObserved = false;
        _absentSince = null;
        SteamRunning = false;

        // Force the next Poll to actually query instead of returning the cached interval result.
        _hasPolled = false;
    }

    private static bool IsSteamProcessRunning()
    {
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcessesByName(SteamProcessName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            // GetProcessesByName hands back live handles; polling every second would leak them.
            if (processes is not null)
            {
                foreach (var process in processes)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
    }
}
