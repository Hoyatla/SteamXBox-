using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// One overlay keyboard per controller, with its own channels.
/// </summary>
/// <remarks>
/// Everything about the keyboard used to be singular — one pipe, one haptic pipe, one set of signal
/// files, one resident window — and every one of those quietly assumed a single typist. Two players
/// asking at the same time collided on all four: the second pipe server could not bind, the second
/// window answered the first player's show signal, and both controllers' frames landed in the same
/// reader.
///
/// <para>
/// Created on demand rather than up front. A keyboard nobody asked for is a process and four
/// channels for nothing, and with four pads attached that is four idle windows.
/// </para>
///
/// <para>
/// The set owns the channels and the lifetime; where the keys go once typed is the overlay's
/// business, and it stays that way — the bridge never learns what was typed.
/// </para>
///
/// <para>
/// Safe to call from any thread. The controller reader tasks run on their own threads and close a
/// keyboard through <see cref="CloseAsync"/> when their pad disappears — <c>onLeft</c> calls it
/// fire-and-forget — at the same time the frame loop is opening keyboards and looking them up.
/// Without a guard the two would corrupt the dictionary: the Steam hand-over reopens every pad
/// while the previous readers are still winding down, and one such concurrent update poisoned the
/// collection so badly that every later <see cref="Open"/> threw, even from the loop's own thread.
/// Every access is therefore serialised on one gate. <see cref="CloseAsync"/> removes under the
/// gate but disposes outside it — a disposal that blocks on the overlay must not hold the gate the
/// loop needs for its next lookup.
/// </para>
/// </remarks>
public sealed class OskInstanceSet : IAsyncDisposable
{
    private readonly Dictionary<string, OskInstance> _instances = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Action<string>? _log;
    private readonly Func<HapticCommand, CancellationToken, ValueTask>? _hapticDispatch;

    /// <param name="hapticDispatch">
    /// Routes one overlay's haptic request into the core's single HID stream. Null when the core has
    /// no haptic sink, in which case no per-instance receiver is opened.
    /// </param>
    public OskInstanceSet(
        Action<string>? log = null,
        Func<HapticCommand, CancellationToken, ValueTask>? hapticDispatch = null)
    {
        _log = log;
        _hapticDispatch = hapticDispatch;
    }

    /// <summary>How many keyboards are open.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _instances.Count;
            }
        }
    }

    /// <summary>Whether this controller currently has a keyboard.</summary>
    public bool IsOpen(string controllerId)
    {
        lock (_gate)
        {
            return _instances.ContainsKey(controllerId);
        }
    }

    /// <summary>The sender feeding this controller's keyboard, or null when it has none.</summary>
    public PadDataSender? SenderFor(string controllerId)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(controllerId, out var instance) ? instance.Sender : null;
        }
    }

    /// <summary>This controller's keyboard, or null when it has none.</summary>
    public OskInstance? InstanceFor(string controllerId)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(controllerId, out var instance) ? instance : null;
        }
    }

    /// <summary>
    /// Opens this controller's keyboard, or returns the one it already has.
    /// </summary>
    /// <remarks>
    /// The suffix is derived from the controller id on both sides — here and in the overlay, from
    /// the same function. A suffix that disagreed would be a pipe that never connects, with nothing
    /// anywhere to say why.
    ///
    /// <para>
    /// The whole method holds the gate: a controller being re-opened mid hand-over must not get a
    /// second sender while the first is still registered for it. Nothing here awaits, so the gate is
    /// never held across a suspension.
    /// </para>
    /// </remarks>
    public OskInstance Open(string controllerId)
    {
        lock (_gate)
        {
            if (_instances.TryGetValue(controllerId, out var existing))
            {
                return existing;
            }

            var naming = OskInstanceNaming.For(controllerId);
            var sender = new PadDataSender(naming.PadPipeName);
            sender.Start();

            var hapticReceiver = _hapticDispatch is null
                ? null
                : new HapticRequestReceiver(_hapticDispatch, _log, naming.HapticPipeName);
            hapticReceiver?.Start();

            var instance = new OskInstance(controllerId, naming, sender, hapticReceiver);
            _instances[controllerId] = instance;

            _log?.Invoke($"keyboard opened for {controllerId} on {naming.PadPipeName}");

            return instance;
        }
    }

    /// <summary>Closes this controller's keyboard and releases its channels.</summary>
    public async ValueTask CloseAsync(string controllerId)
    {
        // Remove under the gate, dispose outside it: disposal can block on the overlay, and a
        // blocked CloseAsync must not stall the loop's next Open or lookup.
        OskInstance? instance;
        lock (_gate)
        {
            if (!_instances.Remove(controllerId, out instance))
            {
                return;
            }
        }

        await instance.DisposeAsync().ConfigureAwait(false);
        _log?.Invoke($"keyboard closed for {controllerId}");
    }

    /// <summary>
    /// Every open keyboard, for the transitions that must reach all of them.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a live view: the caller walks it while other threads may be closing
    /// keyboards, and enumerating the dictionary itself under those conditions would throw.
    /// </remarks>
    public IReadOnlyCollection<OskInstance> All
    {
        get
        {
            lock (_gate)
            {
                return new List<OskInstance>(_instances.Values);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<string> ids;
        lock (_gate)
        {
            ids = _instances.Keys.ToList();
        }

        foreach (var id in ids)
        {
            await CloseAsync(id).ConfigureAwait(false);
        }
    }
}

/// <summary>One controller's keyboard: its names, the pipe feeding it and its haptic receiver.</summary>
public sealed class OskInstance : IAsyncDisposable
{
    internal OskInstance(
        string controllerId,
        OskInstanceNaming naming,
        PadDataSender sender,
        HapticRequestReceiver? hapticReceiver)
    {
        ControllerId = controllerId;
        Naming = naming;
        Sender = sender;
        _hapticReceiver = hapticReceiver;
    }

    /// <summary>The controller this keyboard belongs to.</summary>
    public string ControllerId { get; }

    /// <summary>Every channel name for this keyboard.</summary>
    public OskInstanceNaming Naming { get; }

    /// <summary>The pipe carrying this controller's frames to its keyboard.</summary>
    public PadDataSender Sender { get; }

    private readonly HapticRequestReceiver? _hapticReceiver;

    public async ValueTask DisposeAsync()
    {
        if (_hapticReceiver is not null)
        {
            await _hapticReceiver.DisposeAsync().ConfigureAwait(false);
        }

        await Sender.DisposeAsync().ConfigureAwait(false);
    }
}
