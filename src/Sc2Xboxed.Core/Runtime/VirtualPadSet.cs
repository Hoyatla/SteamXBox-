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
/// Pads are created on first use, not up front. A controller that is attached but never touched
/// would otherwise still appear to every game as a connected player — which in a split-screen title
/// means a phantom second player occupying a slot, and in a single-player one means the game
/// choosing the wrong pad. A pad appears when its controller actually sends something.
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
    private readonly Dictionary<string, IVirtualXbox360Sink> _pads = new(StringComparer.Ordinal);
    private readonly Func<IVirtualXbox360Sink> _factory;
    private readonly Action<string>? _log;

    /// <param name="factory">Creates one virtual pad. Called once per physical controller.</param>
    /// <param name="log">Optional diagnostic sink.</param>
    public VirtualPadSet(Func<IVirtualXbox360Sink> factory, Action<string>? log = null)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>How many virtual pads are currently connected.</summary>
    public int Count => _pads.Count;

    /// <summary>The controllers that have a virtual pad, in creation order.</summary>
    public IReadOnlyCollection<string> ControllerIds => _pads.Keys.ToList();

    /// <summary>
    /// The virtual pad belonging to one controller, creating and connecting it on first use.
    /// </summary>
    public async ValueTask<IVirtualXbox360Sink> ForAsync(string controllerId, CancellationToken cancellationToken)
    {
        if (_pads.TryGetValue(controllerId, out var existing))
        {
            return existing;
        }

        var pad = _factory();
        await pad.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _pads[controllerId] = pad;
        _log?.Invoke($"virtual pad {_pads.Count} connected for {controllerId}");

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
        foreach (var pad in _pads.Values.ToList())
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
    /// Releases the virtual pad of a controller that has gone away.
    /// </summary>
    /// <remarks>
    /// Left connected, it stays visible to games as a player who never presses anything — a
    /// split-screen match holding a slot open for someone whose controller went to sleep.
    /// </remarks>
    public async ValueTask ForgetAsync(string controllerId)
    {
        if (!_pads.Remove(controllerId, out var pad))
        {
            return;
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
        foreach (var id in _pads.Keys.ToList())
        {
            await ForgetAsync(id).ConfigureAwait(false);
        }
    }
}
