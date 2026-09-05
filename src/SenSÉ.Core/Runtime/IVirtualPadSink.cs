namespace SenSÉ.Core.Runtime;

/// <summary>
/// The part of a virtual pad's contract that is the same whatever family it emulates.
/// </summary>
public interface IVirtualPadSink : IAsyncDisposable
{
    ValueTask ConnectAsync(CancellationToken cancellationToken);
}
