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
/// </remarks>
public sealed class OskInstanceSet : IAsyncDisposable
{
    private readonly Dictionary<string, OskInstance> _instances = new(StringComparer.Ordinal);
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
    public int Count => _instances.Count;

    /// <summary>Whether this controller currently has a keyboard.</summary>
    public bool IsOpen(string controllerId) => _instances.ContainsKey(controllerId);

    /// <summary>The sender feeding this controller's keyboard, or null when it has none.</summary>
    public PadDataSender? SenderFor(string controllerId)
        => _instances.TryGetValue(controllerId, out var instance) ? instance.Sender : null;

    /// <summary>This controller's keyboard, or null when it has none.</summary>
    public OskInstance? InstanceFor(string controllerId)
        => _instances.TryGetValue(controllerId, out var instance) ? instance : null;

    /// <summary>
    /// Opens this controller's keyboard, or returns the one it already has.
    /// </summary>
    /// <remarks>
    /// The suffix is derived from the controller id on both sides — here and in the overlay, from
    /// the same function. A suffix that disagreed would be a pipe that never connects, with nothing
    /// anywhere to say why.
    /// </remarks>
    public OskInstance Open(string controllerId)
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

    /// <summary>Closes this controller's keyboard and releases its channels.</summary>
    public async ValueTask CloseAsync(string controllerId)
    {
        if (!_instances.Remove(controllerId, out var instance))
        {
            return;
        }

        await instance.DisposeAsync().ConfigureAwait(false);
        _log?.Invoke($"keyboard closed for {controllerId}");
    }

    /// <summary>Every open keyboard, for the transitions that must reach all of them.</summary>
    public IReadOnlyCollection<OskInstance> All => _instances.Values;

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _instances.Keys.ToList())
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
