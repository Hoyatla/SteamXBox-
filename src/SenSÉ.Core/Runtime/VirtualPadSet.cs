using SenSÉ.Core.Input;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Runtime;

/// <summary>
/// One virtual pad per physical controller, shaped for the controller's family.
/// </summary>
/// <remarks>
/// The reason the whole multi-controller change exists. Reading several pads at once is only half
/// of it: if they all feed one virtual pad, a split-screen game still sees a single player, and two
/// people pressing at once produce one incoherent stream of input rather than two players. Keeping
/// them apart all the way to the game is what makes the second player real.
///
/// <para>
/// The family decides the pad's shape. A Steam Controller or an Xbox pad becomes a virtual Xbox 360
/// pad; a DualSense becomes a virtual DualShock 4, so a game reads it as the PlayStation pad its
/// labels promise rather than an Xbox controller it does not resemble. Each controller gets exactly
/// one pad, of the family it arrived as, for the whole time it is attached.
/// </para>
///
/// <para>
/// A pad is connected as soon as its controller is attached, and released when the controller
/// leaves. It used to be created on the first frame the controller sent in Xbox mode, which meant
/// switching to Xbox while a game was already running hot-plugged a brand-new device into it — and
/// games that enumerate controllers at launch never see a pad that appears mid-session, so the
/// switch read as "the controller stopped responding". The pad now exists before the game starts;
/// in Profile mode it simply receives neutral reports. The cost is the one the old design existed
/// to avoid: an attached controller occupies an XInput slot even before any game uses it.
/// </para>
///
/// <para>
/// The factories are injected so this can be tested without ViGEm, which needs a driver and real
/// hardware. What is worth testing here is the bookkeeping — one pad per controller, no duplicates,
/// released on disconnect — and none of that is about ViGEm.
/// </para>
/// </remarks>
public sealed class VirtualPadSet : IAsyncDisposable
{
    // The pad of a controller that goes away is released from a reader task while the main loop
    // may be creating or submitting to other pads, so the dictionaries are never touched without
    // holding this. No await happens under the lock.
    private readonly object _gate = new();
    private readonly Dictionary<string, IVirtualXbox360Sink> _xboxPads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IVirtualDS4Sink> _ds4Pads = new(StringComparer.Ordinal);
    private readonly Func<ControllerIdentity, IVirtualXbox360Sink> _xboxFactory;
    private readonly Func<ControllerIdentity, IVirtualDS4Sink> _ds4Factory;
    private readonly Action<string>? _log;
    private readonly Func<IDisposable>? _recordCreation;

    /// <param name="xboxFactory">Creates one virtual Xbox 360 pad. Called once per Xbox-family controller.</param>
    /// <param name="ds4Factory">Creates one virtual DualShock 4 pad. Called once per DualSense controller.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <param name="recordCreation">
    /// Optional. Called around the connection, and disposed once it has completed, so a caller that
    /// knows how to read the device tree can write down the records the new pad brought into being.
    /// </param>
    /// <remarks>
    /// The hook wraps the connection rather than the factory because the device does not exist until
    /// the connection completes — a snapshot taken around a factory call would see nothing new. It
    /// is a callback rather than a direct call because this class is deliberately free of Windows:
    /// the device tree lives a layer out, and is testable only on a real machine.
    ///
    /// <para>
    /// The factory receives the controller's <see cref="ControllerIdentity"/>, not just its id,
    /// because the pad's rumble has to be routed back to the physical controller that owns it — by
    /// kind, by XInput slot, by HID path — and none of that can be derived from the id alone.
    /// </para>
    /// </remarks>
    public VirtualPadSet(
        Func<ControllerIdentity, IVirtualXbox360Sink> xboxFactory,
        Func<ControllerIdentity, IVirtualDS4Sink> ds4Factory,
        Action<string>? log = null,
        Func<IDisposable>? recordCreation = null)
    {
        _xboxFactory = xboxFactory;
        _ds4Factory = ds4Factory;
        _log = log;
        _recordCreation = recordCreation;
    }

    /// <summary>How many virtual pads are currently connected.</summary>
    public int Count
    {
        get { lock (_gate) return _xboxPads.Count + _ds4Pads.Count; }
    }

    /// <summary>Whether a controller already has a virtual pad connected.</summary>
    /// <remarks>
    /// The cheap check the frame loop uses before asking the family-aware connect methods to create
    /// one: pads are now connected at attach time, so on the steady path this answers true and the
    /// request never has to go through the async connect path.
    /// </remarks>
    public bool Has(string controllerId)
    {
        lock (_gate)
        {
            return _xboxPads.ContainsKey(controllerId) || _ds4Pads.ContainsKey(controllerId);
        }
    }

    /// <summary>The controllers that have a virtual pad, in creation order.</summary>
    public IReadOnlyCollection<string> ControllerIds
    {
        get
        {
            lock (_gate)
            {
                return _xboxPads.Keys.Concat(_ds4Pads.Keys).ToList();
            }
        }
    }

    /// <summary>
    /// The virtual Xbox 360 pad belonging to one controller, connecting it when the controller does
    /// not have one yet.
    /// </summary>
    public ValueTask<IVirtualXbox360Sink> ForAsync(ControllerIdentity identity, CancellationToken cancellationToken)
        => ConnectAsync(identity, cancellationToken, _xboxPads, _xboxFactory);

    /// <summary>
    /// The virtual DualShock 4 pad belonging to one controller, connecting it when the controller
    /// does not have one yet.
    /// </summary>
    public ValueTask<IVirtualDS4Sink> ForDS4Async(ControllerIdentity identity, CancellationToken cancellationToken)
        => ConnectAsync(identity, cancellationToken, _ds4Pads, _ds4Factory);

    private async ValueTask<T> ConnectAsync<T>(
        ControllerIdentity identity,
        CancellationToken cancellationToken,
        Dictionary<string, T> pads,
        Func<ControllerIdentity, T> factory) where T : IVirtualPadSink
    {
        var controllerId = identity.Id;

        lock (_gate)
        {
            if (pads.TryGetValue(controllerId, out var existing))
            {
                return existing;
            }
        }

        var pad = factory(identity);

        using (_recordCreation?.Invoke())
        {
            await pad.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        T? redundant = default;
        lock (_gate)
        {
            if (pads.TryGetValue(controllerId, out var existing))
            {
                // The controller left and came back while ours was being connected. Hand out the
                // pad that won and discard the one just built, outside the lock.
                redundant = pad;
                pad = existing;
            }
            else
            {
                pads[controllerId] = pad;
                _log?.Invoke($"virtual pad {Count} connected for {controllerId}");
            }
        }

        if (redundant is not null)
        {
            await redundant.DisposeAsync().ConfigureAwait(false);
        }

        return pad;
    }

    /// <summary>Sends the neutral report to one controller's pad, of whichever family it is.</summary>
    /// <remarks>
    /// The transition that must affect one player and no other — leaving Xbox mode, neutralising a
    /// pad that switched to Profile — has to say the family's own neutral: a DualShock 4 report to
    /// an Xbox pad is nonsense, and so is the reverse. This picks the report the pad was created
    /// with.
    ///
    /// <para>
    /// Does nothing when the controller has no pad. That is not an error: a controller that left
    /// between the decision and this call has already had its pad released, and creating one here
    /// just to neutralise it would connect a device to the game to say nothing with it.
    /// </para>
    /// </remarks>
    public async ValueTask NeutralizeForAsync(string controllerId, CancellationToken cancellationToken)
    {
        IVirtualXbox360Sink? xboxPad;
        IVirtualDS4Sink? ds4Pad;

        lock (_gate)
        {
            _xboxPads.TryGetValue(controllerId, out xboxPad);
            _ds4Pads.TryGetValue(controllerId, out ds4Pad);
        }

        if (xboxPad is not null)
        {
            try
            {
                await xboxPad.SubmitAsync(Xbox360Report.Neutral, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Reported, never propagated: one driver refusing a report must not take down the
                // loop that feeds every other player.
                _log?.Invoke($"neutralising the virtual pad of {controllerId}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (ds4Pad is not null)
        {
            try
            {
                await ds4Pad.SubmitAsync(DS4Report.Neutral, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Invoke($"neutralising the virtual pad of {controllerId}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Sends the neutral report to every connected pad, each of its own family.</summary>
    /// <remarks>
    /// For the transitions that must apply to everyone at once — leaving Xbox mode, handing the
    /// controllers to Steam. Sending neutral to only the pad that happened to send the last frame
    /// would leave the other players' pads holding whatever they were last told, which in a game
    /// reads as a stuck stick or a held trigger.
    /// </remarks>
    public async ValueTask NeutralizeAllAsync(CancellationToken cancellationToken)
    {
        foreach (var controllerId in ControllerIds)
        {
            await NeutralizeForAsync(controllerId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the virtual pad of a controller that has gone away.
    /// </summary>
    /// <remarks>
    /// Left connected, it stays visible to games as a player who never presses anything — a
    /// split-screen match holding a slot open for someone whose controller went to sleep.
    /// </remarks>
    public async ValueTask ForgetAsync(string controllerId)
    {
        IVirtualXbox360Sink? xboxPad;
        IVirtualDS4Sink? ds4Pad;

        lock (_gate)
        {
            _xboxPads.Remove(controllerId, out xboxPad);
            _ds4Pads.Remove(controllerId, out ds4Pad);
        }

        if (xboxPad is not null)
        {
            await ReleaseAsync(controllerId, xboxPad).ConfigureAwait(false);
        }

        if (ds4Pad is not null)
        {
            await ReleaseAsync(controllerId, ds4Pad).ConfigureAwait(false);
        }
    }

    private async ValueTask ReleaseAsync(string controllerId, IVirtualXbox360Sink pad)
    {
        try
        {
            await pad.SubmitAsync(Xbox360Report.Neutral, CancellationToken.None).ConfigureAwait(false);
            await pad.DisposeAsync().ConfigureAwait(false);
            _log?.Invoke($"virtual pad released for {controllerId}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"releasing the virtual pad of {controllerId}: {ex.Message}");
        }
    }

    private async ValueTask ReleaseAsync(string controllerId, IVirtualDS4Sink pad)
    {
        try
        {
            await pad.SubmitAsync(DS4Report.Neutral, CancellationToken.None).ConfigureAwait(false);
            await pad.DisposeAsync().ConfigureAwait(false);
            _log?.Invoke($"virtual pad released for {controllerId}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"releasing the virtual pad of {controllerId}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var controllerId in ControllerIds)
        {
            await ForgetAsync(controllerId).ConfigureAwait(false);
        }
    }
}
