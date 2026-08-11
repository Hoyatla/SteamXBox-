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
    private readonly Action<ControllerIdentity>? _onLeft;
    private readonly Func<IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)>>? _rescan;
    private readonly TimeSpan _rescanInterval;
    private readonly List<Task> _liveReaders = [];
    private readonly List<Task> _allReaders = [];
    private int _enumerations;

    /// <summary>
    /// This source's own life, so that letting go of it actually stops it.
    /// </summary>
    /// <remarks>
    /// <b>Everything here used to run on the caller's token, which is the session's.</b> So a source
    /// the caller had finished with kept its arrivals watcher running until the whole session ended:
    /// rescanning every three seconds, opening devices, and announcing controllers alongside its
    /// replacement.
    ///
    /// <para>
    /// That is what "the same controller arrives twice" was. The loop that waits for a pad to come
    /// back builds a new source each time round and never released the old one, so a session with
    /// two power-off cycles ran three watchers at once — each on its own three-second beat, hence
    /// arrivals eight tenths of a second apart from a loop that waits three seconds.
    /// </para>
    /// </remarks>
    private readonly CancellationTokenSource _own = new();

    /// <summary>The links between this source's life and each caller's, kept until disposal.</summary>
    private readonly List<CancellationTokenSource> _lifetimes = [];

    /// <summary>
    /// Reader tasks still running, and how many times the stream has been enumerated.
    /// </summary>
    /// <remarks>
    /// Counted because a frame rate that climbs on its own has exactly two explanations and they
    /// need opposite fixes: several readers on one device, or one reader per device with the device
    /// simply emitting faster. From outside they look identical — the controller list stays correct
    /// either way, because duplicate readers all carry the same identity and collapse into one
    /// session.
    ///
    /// <see cref="Enumerations"/> is the sharper of the two. This class exposes an iterator, and an
    /// iterator re-entered builds a second channel and a second watcher while the first set of
    /// readers keeps running. Nothing stops them, so their number can only grow.
    /// </remarks>
    public int ActiveReaders => _allReaders.Count(t => !t.IsCompleted);

    /// <inheritdoc cref="ActiveReaders"/>
    public int Enumerations => _enumerations;

    /// <param name="children">Each controller, with the source that reads it.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <param name="onLeft">
    /// Invoked from the reader's own task when a controller's stream ends. The sink uses it to
    /// release the controller's virtual pad so a game does not keep a phantom player whose pad went
    /// away.
    /// </param>
    /// <param name="rescan">
    /// Re-enumerates what is attached. Supplied rather than performed here so this class stays
    /// unaware of how controllers are discovered; null disables hot-plug entirely.
    /// </param>
    /// <param name="rescanInterval">How often to look. Defaults to three seconds.</param>
    public ParallelControllerSource(
        IEnumerable<(ControllerIdentity Identity, IPhysicalControllerSource Source)> children,
        Action<string>? log = null,
        Action<ControllerIdentity>? onLeft = null,
        Func<IReadOnlyList<(ControllerIdentity Identity, IPhysicalControllerSource Source)>>? rescan = null,
        TimeSpan? rescanInterval = null)
    {
        _children = children.ToList();
        _log = log;
        _onLeft = onLeft;
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
        while (!cancellationToken.IsCancellationRequested)
        {
            // Recomputed each round rather than kept, so a controller that left is genuinely
            // forgotten and switching it back on brings it back. A set held across rounds made a
            // departure permanent for the rest of the session.
            HashSet<string> known;
            lock (_children)
            {
                known = _children.Select(c => c.Identity.Id).ToHashSet(StringComparer.Ordinal);
            }

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

            foreach (var child in found)
            {
                if (known.Contains(child.Identity.Id))
                {
                    // Already being read, so this second source for the same controller is surplus —
                    // and it was simply dropped on the floor before. A rescan builds one source per
                    // attached controller every three seconds, whether or not it is new, so on a
                    // machine with a controller connected this discarded an undisposed
                    // IAsyncDisposable twenty times a minute for the length of the session.
                    await SafelyDispose(child.Source).ConfigureAwait(false);
                    continue;
                }

                known.Add(child.Identity.Id);
                _children.Add(child);
                var pump = PumpAsync(child.Identity, child.Source, writer, cancellationToken);
                _liveReaders.Add(pump);
                _allReaders.Add(pump);
                _log?.Invoke($"controller arrived: {child.Identity.DisplayName} [{child.Identity.Id}]");
            }
        }
    }

    /// <summary>Lets go of a source we are not going to read, whatever it thinks of that.</summary>
    /// <remarks>
    /// A source that throws while being disposed must not end the rescan: the next controller to be
    /// plugged in would then never be noticed, which is a far worse outcome than one handle staying
    /// open.
    /// </remarks>
    private async Task SafelyDispose(IPhysicalControllerSource source)
    {
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"discarding a surplus source: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public IReadOnlyList<ControllerIdentity> Controllers
        => _children.Select(c => c.Identity).ToList();

    /// <summary>
    /// Asks one controller to power itself off.
    /// </summary>
    /// <param name="identity">The controller whose frame triggered the chord. Not "any attached pad":
    /// with several players, only the one whose buttons were held should go to sleep.</param>
    /// <returns>True when the command was handed to the device; false when the controller was gone,
    /// does not support power-off (an Xbox pad), or refused.</returns>
    /// <remarks>
    /// The matching controller could have left between the frame arriving and this call. Locking
    /// does not help with that — a device can always disappear mid-stream — so this reports the
    /// absence as false rather than throwing, which is the same answer the sources give when they
    /// have no open stream.
    /// </remarks>
    public bool TrySendPowerOff(ControllerIdentity identity)
    {
        lock (_children)
        {
            var child = _children.FirstOrDefault(c => c.Identity.Id == identity.Id);
            return child.Source is IPowerControl power && power.SendPowerOff();
        }
    }

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

        _enumerations++;

        // The caller's life and this source's own, whichever ends first. Without the second half,
        // disposing this object stopped nothing at all.
        //
        // NOT a "using". It was, and that was a bug with a long reach: breaking out of the
        // enumeration ends the iterator, which disposed the linked source — and a disposed linked
        // source no longer forwards anything, so cancelling _own afterwards reached nobody. The
        // watcher went on rescanning every three seconds for the rest of the session.
        //
        // The consequence in the product was not obvious from here: when Steam started, SteamXBox
        // handed the controller over and released it from HidHide, and six tenths of a second later
        // the orphaned watcher's rescan hid it again. Steam therefore never saw the controller, and
        // the hand-over that had just been logged as done had been undone.
        //
        // Kept and disposed with this object instead, so the order can no longer matter.
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _own.Token);

        lock (_lifetimes)
        {
            _lifetimes.Add(lifetime);
        }

        cancellationToken = lifetime.Token;

        var readers = _children
            .Select(child => PumpAsync(child.Identity, child.Source, channel.Writer, cancellationToken))
            .ToList();

        _allReaders.AddRange(readers);

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
        // A source that outlives its controller has to say so itself. The XInput one keeps polling
        // an empty slot rather than ending its stream — a pad that sleeps often returns on another
        // slot — so without this, switching an Xbox pad off was the one departure nobody heard.
        Action? departed = null;

        if (source is IReportsDeparture reporter)
        {
            departed = () =>
            {
                _log?.Invoke($"controller left: {identity.DisplayName} [{identity.Id}] (still watching for it)");

                try
                {
                    _onLeft?.Invoke(identity);
                }
                catch (Exception ex)
                {
                    // One listener's failure is not this controller's problem, and certainly not
                    // the other players'.
                    _log?.Invoke($"onLeft for {identity.DisplayName} failed: {ex.GetType().Name}: {ex.Message}");
                }
            };

            reporter.Departed += departed;
        }

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
        finally
        {
            if (departed is not null && source is IReportsDeparture stillReporting)
            {
                // Unsubscribed before anything else: this closure holds the identity and the log,
                // and a source kept alive for reconnection would otherwise hold them for ever.
                stillReporting.Departed -= departed;
            }

            // Removed once its stream ends. The set only ever grew before: a controller that went
            // away stayed listed for the rest of the session, and — worse — the arrivals watcher
            // still considered it known, so switching it back on never brought it back.
            lock (_children)
            {
                _children.RemoveAll(c => c.Identity.Id == identity.Id);
            }

            _log?.Invoke($"controller left: {identity.DisplayName} [{identity.Id}]");

            // Let the consumer release whatever that controller owned — its virtual pad above all.
            // Without this a pad whose controller went to sleep stays visible to games as a player
            // who never presses anything.
            try
            {
                _onLeft?.Invoke(identity);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"releasing {identity.DisplayName} after departure: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // First, so that the arrivals watcher stops rescanning before the devices under it are
        // closed. The other order leaves a watcher enumerating hardware that is being taken away.
        try
        {
            await _own.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Disposed twice; the first call already stopped everything.
        }

        // After the cancellation, never before: disposing a linked source stops it forwarding, so
        // releasing these first would leave the watchers running on tokens nothing can cancel.
        lock (_lifetimes)
        {
            foreach (var lifetime in _lifetimes)
            {
                try { lifetime.Dispose(); } catch (ObjectDisposedException) { }
            }

            _lifetimes.Clear();
        }

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
