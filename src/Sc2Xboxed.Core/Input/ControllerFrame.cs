namespace Sc2Xboxed.Core.Input;

/// <summary>
/// One frame, and the controller it came from.
/// </summary>
/// <param name="Source">Which controller produced it.</param>
/// <param name="State">What that controller reported.</param>
/// <remarks>
/// The identity travels with the frame rather than being held next to the stream, because with
/// several controllers read at once there is no such thing as "the controller that just sent
/// something": by the time a frame is handled, another pad may already have produced two more. A
/// field read alongside the stream would be right most of the time and wrong under exactly the
/// conditions this exists for — two people playing at once.
/// </remarks>
public readonly record struct ControllerFrame(ControllerIdentity Source, ControllerState State);

/// <summary>
/// A source that reads several controllers at once.
/// </summary>
/// <remarks>
/// Separate from the single-controller interface rather than replacing it: one pad read on its own
/// is still the common case, and the sources that know how to talk to a device have no business
/// knowing how many others exist.
/// </remarks>
public interface IMultiControllerSource : IAsyncDisposable
{
    /// <summary>Frames from every controller, interleaved in arrival order.</summary>
    IAsyncEnumerable<ControllerFrame> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>The controllers currently being read.</summary>
    IReadOnlyList<ControllerIdentity> Controllers { get; }
}
