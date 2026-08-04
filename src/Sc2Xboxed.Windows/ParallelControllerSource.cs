using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Reads every attached controller at once and merges their frames into one stream.
/// </summary>
/// <remarks>
/// This replaces choosing a controller. The Core used to pick one by a fixed rule and ignore the
/// rest, which is wrong twice over. It makes split-screen impossible by construction — two players
/// need two physical pads feeding two distinct virtual ones — and it is fragile in the ordinary
/// single-player case for a reason that took a long time to find: "the first XInput slot that
/// answers" was, on the author's machine, a third-party virtual gamepad that answers every poll
/// with zeros. The real controller was never read, and every symptom pointed at the mapping code
/// instead.
///
/// <para>
/// One reader task per controller, each writing into a shared channel. Not one loop polling each in
/// turn: a Steam Controller blocks on a HID read while an XInput pad is polled on a timer, and
/// interleaving those in one loop makes each one's latency depend on the other's. Separate tasks
/// keep a slow or absent controller from delaying anyone else.
/// </para>
///
/// <para>
/// The channel is unbounded but drained continuously by the single consumer. Bounded with
/// <c>DropOldest</c> was considered and rejected: silently discarding input frames is how a pointer
/// acquires a stutter nobody can reproduce.
/// </para>
/// </remarks>
public sealed class ParallelControllerSource : IMultiControllerSource
{
    private readonly List<(ControllerIdentity Identity, IPhysicalControllerSource Source)> _children;
    private readonly Action<string>? _log;
    private readonly Func<IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)>>? _rescan;
    private readonly TimeSpan _rescanInterval;
    private readonly List<Task> _liveReaders = [];

    /// <param name="children">Each controller, with the source that reads it.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <param name="rescan">
    /// Re-enumerates what is attached. Supplied rather than performed here so this class stays
    /// unaware of how controllers are discovered; null disables hot-plug entirely.
    /// </param>
    /// <param name="rescanInterval">How often to look. Defaults to three seconds.</param>
    public ParallelControllerSource(
        IEnumerable<(ControllerIdentity Identity, IPhysicalControllerSource Source)> children,
        Action<string>? log = null,
        Func<IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)>>? rescan = null,
        TimeSpan? rescanInterval = null)
    {
        _children = children.ToList();
        _log = log;
        _rescan = rescan;
        _rescanInterval = rescanInterval ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// Starts reading controllers that appear after the session began.
    /// </summary>
    /// <remarks>
    /// Only additions. A controller that goes away ends its own pump on the read error and needs
    /// nothing here — and treating a departure as a reason to rebuild would drop the virtual pads of
    /// players who never touched anything, which in a split-screen game loses a player because
    /// somebody else's controller went to sleep.
    /// </remarks>
    private async Task WatchForArrivalsAsync(
        ChannelWriter<ControllerFrame> writer,
        CancellationToken cancellationToken)
    {
        var known = _children.Select(c => c.Identity.Id).ToHashSet(StringComparer.Ordinal);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_rescanInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)> found;

            try
            {
                found = _rescan!();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"rescanning for controllers: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var child in found.Where(c => !known.Contains(c.Identity.Id)))
            {
                known.Add(child.Identity.Id);
                _children.Add(child);
                _liveReaders.Add(PumpAsync(child.Identity, child.Source, writer, cancellationToken));
                _log?.Invoke($"controller arrived: {child.Identity.DisplayName} [{child.Identity.Id}]");
            }
        }
    }

    public IReadOnlyList<ControllerIdentity> Controllers
        => _children.Select(c => c.Identity).ToList();

    public async IAsyncEnumerable<ControllerFrame> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Bounded, dropping the oldest. I chose unbounded here and wrote that discarding frames was
        // how a pointer acquires a stutter. That was wrong for a real-time input path, and the
        // author measured the consequence: with several controllers feeding faster than the loop
        // drains, the queue grows without limit and every input arrives later than the last —
        // "fortes latences de communications cumulatives". An unbounded queue does not avoid the
        // loss, it converts it into unbounded lag, which is worse because it never recovers.
        //
        // For input, the newest frame is the true state of the controller and a stale one is worth
        // nothing. Dropping the oldest keeps the pointer honest and bounds the delay by design.
        var channel = Channel.CreateBounded<ControllerFrame>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        var readers = _children
            .Select(child => PumpAsync(child.Identity, child.Source, channel.Writer, cancellationToken))
            .ToList();

        // Hot-plug. Without it a controller switched on after the bridge started is never read at
        // all, and the only remedy is restarting the Core by hand — which is what "no controller is
        // supported" looked like on a machine where the pad was working perfectly.
        //
        // A rescan rather than a device-arrival notification: Windows announces HID arrivals and
        // XInput ones through entirely different mechanisms, and a poll every few seconds covers
        // both at a cost nobody can measure. Pads are switched on by hand, so seconds is the right
        // scale.
        var watcher = _rescan is null
            ? Task.CompletedTask
            : WatchForArrivalsAsync(channel.Writer, cancellationToken);

        // Closes the stream once every controller has stopped, so the consumer ends rather than
        // waiting on a channel nobody will ever write to again.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    // Only the initial readers. Waiting on the watcher too would keep the stream
                    // open forever, and waiting on readers added later is what the watcher's own
                    // lifetime covers — the stream must not close while a pad that arrived after
                    // startup is still sending.
                    await Task.WhenAll(readers).ConfigureAwait(false);

                    while (_rescan is not null && !cancellationToken.IsCancellationRequested)
                    {
                        var live = _liveReaders.Where(t => !t.IsCompleted).ToList();
                        if (live.Count == 0)
                        {
                            break;
                        }

                        await Task.WhenAll(live).ConfigureAwait(false);
                    }
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            },
            CancellationToken.None);

        await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <summary>
    /// Drains one controller into the shared channel.
    /// </summary>
    /// <remarks>
    /// A controller that fails takes only itself down. Letting the exception escape would end every
    /// other player's session because one pad's battery ran out, which in a split-screen game means
    /// the match ending for everyone.
    /// </remarks>
    private async Task PumpAsync(
        ControllerIdentity identity,
        IPhysicalControllerSource source,
        ChannelWriter<ControllerFrame> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var state in source.ReadFramesAsync(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!writer.TryWrite(new ControllerFrame(identity, state)))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down; not a fault.
        }
        catch (Exception ex)
        {
            _log?.Invoke($"controller {identity.DisplayName} stopped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var child in _children)
        {
            try
            {
                await child.Source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"disposing {child.Identity.DisplayName}: {ex.Message}");
            }
        }

        _children.Clear();
    }
}
