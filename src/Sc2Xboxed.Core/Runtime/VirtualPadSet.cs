using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// One virtual Xbox pad per physical controller.
/// </summary>
/// <remarks>
/// The reason the whole multi-controller change exists. Reading several pads at once is only half
/// of it: if they all feed one virtual pad, a split-screen game still sees a single player, and two
/// people pressing at once produce one incoherent stream of input rather than two players. Keeping
/// them apart all the way to the game is what makes the second player real.
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
/// The factory is injected so this can be tested without ViGEm, which needs a driver and real
/// hardware. What is worth testing here is the bookkeeping — one pad per controller, no duplicates,
/// released on disconnect — and none of that is about ViGEm.
/// </para>
/// </remarks>
public sealed class VirtualPadSet : IAsyncDisposable
{
    // The pad of a controller that goes away is released from a reader task while the main loop
    // may be creating or submitting to other pads, so the dictionary is never touched without
    // holding this. No await happens under the lock.
    private readonly object _gate = new();
    private readonly Dictionary<string, IVirtualXbox360Sink> _pads = new(StringComparer.Ordinal);
    private readonly Func<IVirtualXbox360Sink> _factory;
    private readonly Action<string>? _log;
    private readonly Func<IDisposable>? _recordCreation;

    /// <param name="factory">Creates one virtual pad. Called once per physical controller.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    /// <param name="recordCreation">
    /// Optional. Called around the connection, and disposed once it has completed, so a caller that
    /// knows how to read the device tree can write down the records the new pad brought into being.
    /// </param>
    /// <remarks>
    /// The hook wraps the connection rather than the factory because the device does not exist until
    /// the connection completes — a snapshot taken around <paramref name="factory"/> would see
    /// nothing new. It is a callback rather than a direct call because this class is deliberately
    /// free of Windows: the device tree lives a layer out, and is testable only on a real machine.
    /// </remarks>
    public VirtualPadSet(
        Func<IVirtualXbox360Sink> factory,
        Action<string>? log = null,
        Func<IDisposable>? recordCreation = null)
    {
        _factory = factory;
        _log = log;
        _recordCreation = recordCreation;
    }

    /// <summary>How many virtual pads are currently connected.</summary>
    public int Count
    {
        get { lock (_gate) return _pads.Count; }
    }

    /// <summary>Whether a controller already has a virtual pad connected.</summary>
    /// <remarks>
    /// The cheap check the frame loop uses before asking <see cref="ForAsync"/> to create one: pads
    /// are now connected at attach time, so on the steady path this answers true and the request
    /// never has to go through the async connect path.
    /// </remarks>
    public bool Has(string controllerId)
    {
        lock (_gate) return _pads.ContainsKey(controllerId);
    }

    /// <summary>The controllers that have a virtual pad, in creation order.</summary>
    public IReadOnlyCollection<string> ControllerIds
    {
        get { lock (_gate) return _pads.Keys.ToList(); }
    }

    /// <summary>
    /// The virtual pad belonging to one controller, connecting it when the controller does not have
    /// one yet.
    /// </summary>
    public async ValueTask<IVirtualXbox360Sink> ForAsync(string controllerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_pads.TryGetValue(controllerId, out var existing))
            {
                return existing;
            }
        }

        var pad = _factory();

        using (_recordCreation?.Invoke())
        {
            await pad.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        IVirtualXbox360Sink? redundant = null;
        lock (_gate)
        {
            if (_pads.TryGetValue(controllerId, out var existing))
            {
                // The controller left and came back while ours was being connected. Hand out the
                // pad that won and discard the one just built, outside the lock.
                redundant = pad;
                pad = existing;
            }
            else
            {
                _pads[controllerId] = pad;
                _log?.Invoke($"virtual pad {_pads.Count} connected for {controllerId}");
            }
        }

        if (redundant is not null)
        {
            await redundant.DisposeAsync().ConfigureAwait(false);
        }

        return pad;
    }

    /// <summary>Sends one report to every connected pad.</summary>
    /// <remarks>
    /// For the transitions that must apply to everyone at once — leaving Xbox mode, handing the
    /// controllers to Steam. Sending neutral to only the pad that happened to send the last frame
    /// would leave the other players' pads holding whatever they were last told, which in a game
    /// reads as a stuck stick or a held trigger.
    /// </remarks>
    public async ValueTask SubmitAllAsync(Xbox360Report report, CancellationToken cancellationToken)
    {
        List<IVirtualXbox360Sink> pads;
        lock (_gate)
        {
            pads = _pads.Values.ToList();
        }

        foreach (var pad in pads)
        {
            try
            {
                await pad.SubmitAsync(report, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Invoke($"submitting to a virtual pad: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sends one report to one controller's pad, and to no other.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="SubmitAllAsync"/>, for what concerns a single player. Leaving
    /// Xbox mode is the case: the pad of the controller that switched must go neutral, and the pads
    /// of everyone still playing must not — a whole-set neutralisation there drops the sticks of
    /// every other player at once, which in a game reads as everybody's controller failing at the
    /// moment one person changed mode.
    ///
    /// <para>
    /// Does nothing when the controller has no pad. That is not an error: a controller that left
    /// between the decision and this call has already had its pad released, and creating one here
    /// just to neutralise it would connect a device to the game to say nothing with it.
    /// </para>
    /// </remarks>
    public async ValueTask SubmitForAsync(string controllerId, Xbox360Report report, CancellationToken cancellationToken)
    {
        IVirtualXbox360Sink? pad;

        lock (_gate)
        {
            _pads.TryGetValue(controllerId, out pad);
        }

        if (pad is null)
        {
            return;
        }

        try
        {
            await pad.SubmitAsync(report, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reported, never propagated: one driver refusing a report must not take down the loop
            // that feeds every other player.
            _log?.Invoke($"submitting to the virtual pad of {controllerId}: {ex.GetType().Name}: {ex.Message}");
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
        IVirtualXbox360Sink? pad;
        lock (_gate)
        {
            if (!_pads.Remove(controllerId, out pad))
            {
                return;
            }
        }

        try
        {
            // Neutral before releasing: a pad disposed while holding a deflected stick can leave the
            // game reading that deflection until it notices the disconnection.
            await pad.SubmitAsync(Xbox360Report.Neutral, CancellationToken.None).ConfigureAwait(false);
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
        List<string> ids;
        lock (_gate)
        {
            ids = _pads.Keys.ToList();
        }

        foreach (var id in ids)
        {
            await ForgetAsync(id).ConfigureAwait(false);
        }
    }
}
